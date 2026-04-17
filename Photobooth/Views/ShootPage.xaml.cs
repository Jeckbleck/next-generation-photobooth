using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Serilog;

namespace Photobooth.Views
{
    public partial class ShootPage : Page
    {
        private readonly Camera.CameraService _camera = App.Camera;

        private readonly List<string> _capturedPaths = new();
        private bool _shooting;
        private bool _evfRunning;

        public ShootPage()
        {
            InitializeComponent();

            string sessionDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(sessionDir);
            _camera.SessionDirectory = sessionDir;

            Log.Information("Navigated to ShootPage — session dir: {SessionDir}", sessionDir);

            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // --- Lifecycle -----------------------------------------------------------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _camera.EvfFrameReady      += OnEvfFrame;
            _camera.CameraDisconnected += OnCameraDisconnected;
            _camera.Error              += OnCameraError;

            Log.Information("Initializing camera");
            bool ok = _camera.Initialize();
            if (!ok)
            {
                Log.Error("Camera initialization failed — no camera detected");
                StatusText.Text = "No camera detected. Connect camera and restart.";
                StartButton.IsEnabled = false;
                return;
            }

            _evfRunning = true;
            _camera.StartLiveView();
            PumpEvf();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Log.Information("ShootPage unloaded — stopping EVF");
            _evfRunning = false;
            _camera.StopLiveView();

            _camera.EvfFrameReady      -= OnEvfFrame;
            _camera.CameraDisconnected -= OnCameraDisconnected;
            _camera.Error              -= OnCameraError;
        }

        // --- EVF pump ------------------------------------------------------------

        private async void PumpEvf()
        {
            Log.Debug("EVF pump started");
            while (_evfRunning)
            {
                _camera.RequestEvfFrame();
                await Task.Delay(33);
            }
            Log.Debug("EVF pump stopped");
        }

        private void OnEvfFrame(object? sender, BitmapSource frame)
        {
            Dispatcher.Invoke(() => EvfImage.Source = frame);
        }

        // --- Photo sequence ------------------------------------------------------

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_shooting) return;
            _shooting = true;
            StartButton.IsEnabled = false;
            _capturedPaths.Clear();
            ResetDots();

            Log.Information("Photo sequence started");

            for (int i = 1; i <= 3; i++)
            {
                Log.Information("Countdown for photo {PhotoNumber}/3", i);
                await RunCountdown(3);

                StatusText.Text = $"Photo {i} of 3 — smile!";

                try
                {
                    Log.Information("Taking photo {PhotoNumber}/3", i);
                    string path = await _camera.TakePictureAsync();
                    _capturedPaths.Add(path);
                    Log.Information("Photo {PhotoNumber} saved: {Path}", i, path);
                }
                catch (OperationCanceledException)
                {
                    Log.Error("Photo {PhotoNumber} timed out waiting for download", i);
                    StatusText.Text = "Photo timed out — check camera.";
                    _shooting = false;
                    StartButton.IsEnabled = true;
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unexpected error on photo {PhotoNumber}", i);
                }

                await FlashAnimation();
                LightUpDot(i);

                if (i < 3)
                {
                    StatusText.Text = "Next photo in 3 seconds…";
                    await Task.Delay(3000);
                }
            }

            Log.Information("Photo sequence complete — {Count} photos captured", _capturedPaths.Count);
            StatusText.Text = "All done!";
            await Task.Delay(800);

            Dispatcher.Invoke(() =>
            {
                var window = Window.GetWindow(this) as MainWindow;
                window?.NavigateTo(new ResultsPage(_capturedPaths));
            });
        }

        private async Task RunCountdown(int seconds)
        {
            CountdownText.Visibility = Visibility.Visible;
            for (int s = seconds; s >= 1; s--)
            {
                CountdownText.Text = s.ToString();
                await Task.Delay(1000);
            }
            CountdownText.Visibility = Visibility.Collapsed;
        }

        private Task FlashAnimation()
        {
            var tcs = new TaskCompletionSource();
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
            anim.Completed += (_, _) => tcs.SetResult();
            FlashOverlay.BeginAnimation(UIElement.OpacityProperty, anim);
            return tcs.Task;
        }

        // --- Dot indicators ------------------------------------------------------

        private void ResetDots()
        {
            Dot1.Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44));
            Dot2.Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44));
            Dot3.Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44));
        }

        private void LightUpDot(int n)
        {
            var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            Dispatcher.Invoke(() =>
            {
                if (n == 1) Dot1.Fill = accent;
                if (n == 2) Dot2.Fill = accent;
                if (n == 3) Dot3.Fill = accent;
            });
        }

        // --- Error / disconnect --------------------------------------------------

        private void OnCameraDisconnected(object? sender, EventArgs e)
        {
            Log.Warning("Camera disconnected during shoot page");
            Dispatcher.Invoke(() => StatusText.Text = "Camera disconnected.");
        }

        private void OnCameraError(object? sender, string msg)
        {
            Log.Error("Camera error on shoot page: {Message}", msg);
            // Only surface to UI if we're not mid-sequence — don't overwrite countdown/status
            if (!_shooting)
                Dispatcher.Invoke(() => StatusText.Text = $"Error: {msg}");
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User navigated back to GreetingPage from ShootPage");
            _evfRunning = false;
            var window = Window.GetWindow(this) as MainWindow;
            window?.NavigateTo(new GreetingPage());
        }
    }
}
