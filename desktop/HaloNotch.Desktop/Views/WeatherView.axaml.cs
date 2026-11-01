using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using static HaloNotch.Loc;

namespace HaloNotch.Views;

/// Weather tab: now + details on the left, sunrise/sunset, 12 hours and 7 days on the right, sky-tinted background.
public partial class WeatherView : UserControl
{
    public event Action? OpenSettings;

    public WeatherView()
    {
        InitializeComponent();
        DataContext = Hub.Weather;
        HumidityLabel.Text = L("Humidity").ToUpperInvariant();
        WindLabel.Text = L("Wind").ToUpperInvariant();
        UvLabel.Text = L("UV max").ToUpperInvariant();
        PressureLabel.Text = L("Pressure").ToUpperInvariant();
        SettingsButton.Content = L("Weather Settings");
        Hub.Weather.PropertyChanged += (_, _) => Tint();
        Tint();
    }

    void Tint()
    {
        var top = Hub.Weather.Data is { } d ? Color.Parse(WeatherCode.Tint(d.Code, d.IsDay)) : Color.Parse("#2A2D33");
        Sky.Background = new LinearGradientBrush
        {
            StartPoint = new(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops = { new GradientStop(top, 0), new GradientStop(Color.FromArgb(255, (byte)(top.R / 2), (byte)(top.G / 2), (byte)(top.B / 2)), 1) },
        };
    }

    void OnSettings(object? sender, RoutedEventArgs e) => OpenSettings?.Invoke();
}
