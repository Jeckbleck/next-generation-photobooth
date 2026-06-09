using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Photobooth.Camera;
using Serilog;

namespace Photobooth.Views;

public partial class CameraPreviewWindow : Window
{
    private readonly CameraService _camera = App.Services.GetRequiredService<CameraService>();
    private EvfPump? _pump;

    public CameraPreviewWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = _camera.ModelName ?? "Camera";

        _pump = new EvfPump(
            _camera,
            Dispatcher,
            frame =>
            {
                PreviewImage.Source        = frame;
                WaitingText.Visibility     = Visibility.Collapsed;
            },
            onStall: () => WaitingText.Text = "No frames received. Check camera.",
            watchdogMs: 100);

        _pump.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _pump?.Stop();
        Log.Information("CameraPreviewWindow closed");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
