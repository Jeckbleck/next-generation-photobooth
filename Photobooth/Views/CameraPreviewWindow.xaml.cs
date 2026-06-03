using System;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Photobooth.Camera;
using Serilog;

namespace Photobooth.Views
{
    public partial class CameraPreviewWindow : Window
    {
        private readonly CameraService _camera = App.Services.GetRequiredService<CameraService>();

        private bool _evfRunning;
        private bool _evfFramePending;
        private DateTime _lastFrameTime;
        private Timer? _watchdog;

        public CameraPreviewWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StatusText.Text  = _camera.ModelName ?? "Camera";
            _lastFrameTime   = DateTime.UtcNow;
            _evfRunning      = true;

            _camera.EvfFrameReady += OnEvfFrame;
            _camera.StartLiveView();
            RequestNextFrame();

            // Watchdog: restarts the frame pump if it stalls
            _watchdog = new Timer(_ =>
            {
                if (!_evfRunning) return;

                _evfFramePending = false;
                RequestNextFrame();

                if ((DateTime.UtcNow - _lastFrameTime).TotalSeconds > 5)
                {
                    Dispatcher.BeginInvoke(() =>
                        WaitingText.Text = "No frames received. Check camera.");
                }
            }, null, 100, 100);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _evfRunning = false;
            _watchdog?.Dispose();
            _camera.EvfFrameReady -= OnEvfFrame;
            _camera.StopLiveView();
            Log.Information("CameraPreviewWindow closed");
        }

        private void RequestNextFrame()
        {
            if (!_evfRunning || _evfFramePending) return;
            _evfFramePending = true;
            _camera.RequestEvfFrame();
        }

        private void OnEvfFrame(object? sender, BitmapSource frame)
        {
            _evfFramePending = false;
            _lastFrameTime   = DateTime.UtcNow;

            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                PreviewImage.Source = frame;
                WaitingText.Visibility = Visibility.Collapsed;
            });

            RequestNextFrame();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
