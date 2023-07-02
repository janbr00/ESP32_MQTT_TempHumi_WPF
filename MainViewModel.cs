using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using HiveMQtt.Client;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HiveMQtt.Client.Options;
using System.Windows;
using System.Windows.Input;
using System.Collections.ObjectModel;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;
using System.Windows.Controls;
using LiveChartsCore.Drawing;
using System.Globalization;

namespace ESP32_MQTT_TempHumi
{
    partial class MainViewModel : ObservableObject
    {
        #region Felder
        private ObservableCollection<DateTimePoint> temperatur = new ObservableCollection<DateTimePoint>();
        private ObservableCollection<DateTimePoint> feuchtigkeit = new ObservableCollection<DateTimePoint>();
        private NumberFormatInfo _provider = new NumberFormatInfo();
        #endregion

        #region Eigenschaften
        [ObservableProperty]
        private ISeries[] _series;

        [ObservableProperty]
        private ObservableCollection<ISeries> _tempSeries = new ObservableCollection<ISeries>();

        [ObservableProperty]
        private ObservableCollection<ISeries> _humiSeries = new ObservableCollection<ISeries>();

        [ObservableProperty]
        private LineSeries<DateTimePoint> _tmp = new LineSeries<DateTimePoint>()
        {
            YToolTipLabelFormatter = ChartPoint => $"{new DateTime((long)ChartPoint.SecondaryValue):G} Temperatur: {ChartPoint.PrimaryValue}°C",
            Fill = null,
            Stroke = new SolidColorPaint(SKColors.Red, 2),
            LineSmoothness = 0,

            DataPadding = new LvcPoint(5, 0),

            GeometrySize = 2,
            GeometryStroke = new SolidColorPaint(SKColors.Red, 2),
        };

        [ObservableProperty]
        private LineSeries<DateTimePoint> _hmi = new LineSeries<DateTimePoint>()
        {
            YToolTipLabelFormatter = ChartPoint => $"{new DateTime((long)ChartPoint.SecondaryValue):G} Feuchtigkeit: {ChartPoint.PrimaryValue}%",
            Fill = null,
            Stroke = new SolidColorPaint(SKColors.Red, 2),
            LineSmoothness = 0,

            DataPadding = new LvcPoint(5, 0),

            GeometrySize = 2,
            GeometryStroke = new SolidColorPaint(SKColors.Red, 2),
        };

        [ObservableProperty]
        private List<double> _messwerte = new List<double>();

        public bool StatusHeizung { get; set; }

        public RelayCommand Heizung { get; set; }

        public Axis[] XAxes { get; set; } = new Axis[]
        {
            new Axis
            {
                Name = "",
                NamePaint = new SolidColorPaint(SKColors.Black),

                LabelsPaint = new SolidColorPaint(SKColors.Black),
                TextSize = 10,

                Labeler = value => value > 0 ? new DateTime((long)value).ToString("g") : "",
                MinStep = TimeSpan.FromMinutes(10).Ticks,

                ShowSeparatorLines = false
            }
        };

        public Axis[] YAxesTemperatur { get; set; } = new Axis[]
        {
            new Axis
            {
                Name = "Temperatur in °C",
                NamePaint = new SolidColorPaint(SKColors.Black),
                LabelsPaint = new SolidColorPaint(SKColors.Black),
                TextSize = 10,
                MinLimit = 25,
                MaxLimit = 35,
                ShowSeparatorLines= false
            }
        };

        public Axis[] YAxesFeuchtigkeit { get; set; } = new Axis[]
        {
            new Axis
            {
                Name = "Feuchtigkeit in %",
                NamePaint = new SolidColorPaint(SKColors.Black),
                LabelsPaint = new SolidColorPaint(SKColors.Black),
                TextSize = 10,
                MinLimit = 40,
                MaxLimit = 80,
                ShowSeparatorLines= false
            }
        };
        #endregion

        #region Konstruktor
        public MainViewModel()
        {
            Subscribe();
            _provider.NumberDecimalSeparator = ".";
        }
        #endregion

        #region Methoden
        public async void Subscribe()
        {
            var options = new HiveMQClientOptions();
            options.Host = "192.168.178.36";
            options.Port = 1883;

            var client = new HiveMQClient(options);
            var connectResult = await client.ConnectAsync().ConfigureAwait(false);

            var topicTemp = await client.SubscribeAsync("sensor/DHT22/temperatur").ConfigureAwait(false);
            var topicHumi = await client.SubscribeAsync("sensor/DHT22/feuchtigkeit").ConfigureAwait(false);

            client.OnMessageReceived += (sender, args) =>
            {
                string topic = args.PublishMessage.Topic;
                string msg = args.PublishMessage?.PayloadAsString;

                switch (topic)
                {
                    case "sensor/DHT22/temperatur":
                        double temp = double.Parse(msg, _provider);
                        this.temperatur.Add(new DateTimePoint(DateTime.Now, temp));
                        Tmp.Values = temperatur;
                        TempSeries.Add(Tmp);
                        Messwerte.Add(temp);
                        break;
                    case "sensor/DHT22/feuchtigkeit":
                        double humi = double.Parse(msg, _provider);
                        this.feuchtigkeit.Add(new DateTimePoint(DateTime.Now, humi));
                        Hmi.Values = feuchtigkeit;
                        HumiSeries.Add(Hmi);
                        break;
                    default:
                        break;
                }
            };
        }

        [RelayCommand]
        public async void Publish()
        {
            var options = new HiveMQClientOptions();
            options.Host = "192.168.178.36";
            options.Port = 1883;

            var client = new HiveMQClient(options);
            var connectResult = await client.ConnectAsync().ConfigureAwait(false);

            if (StatusHeizung == true)
            {
                var topicAus = await client.PublishAsync("led/ESP32/message", "0").ConfigureAwait(false);
                StatusHeizung = false;
            }
            else
            {
                var topicAn = await client.PublishAsync("led/ESP32/message", "1").ConfigureAwait(false);
                StatusHeizung = true;
            }
        }

        [RelayCommand]
        public void ExportToCSV()
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fileName = "messwerte.csv";
            string filePath = Path.Combine(desktopPath, fileName);
            WriteToCsv(temperatur, feuchtigkeit, filePath);
        }

        public void WriteToCsv(ObservableCollection<DateTimePoint> temperaturMesswerte, ObservableCollection<DateTimePoint> feuchtigkeitMesswerte, string path)
        {
            StringBuilder csvContent = new StringBuilder();
            csvContent.AppendLine("Datum, Temperatur");

            foreach (var tmesswert in temperaturMesswerte)
            {
                csvContent.AppendLine($"{tmesswert.DateTime}, {tmesswert.Value}");
            }

            csvContent.AppendLine("");

            csvContent.AppendLine("Datum, Feuchtigkeit");
            foreach (var fmesswert in feuchtigkeitMesswerte)
            {
                csvContent.AppendLine($"{fmesswert.DateTime}, {fmesswert.Value}");
            }
            File.WriteAllText(path, csvContent.ToString());
        }
        #endregion
    }
}
