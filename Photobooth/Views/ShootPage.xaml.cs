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
        private bool _evfFramePending;
        private bool _sequenceAborted;
        private DateTime _lastEvfFrameTime;
        private System.Threading.Timer? _evfWatchdog;

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

            // Camera is initialized at app startup; just check it's still connected
            if (!_camera.IsConnected)
            {
                Log.Warning("ShootPage loaded but camera not connected");
                StatusText.Text = "No camera detected — go back and check the connection.";
                StartButton.IsEnabled = false;
                return;
            }

            StartEvf();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Log.Information("ShootPage unloaded — stopping EVF");
            _evfWatchdog?.Dispose();
            _evfRunning = false;
            _camera.StopLiveView();

            _camera.EvfFrameReady      -= OnEvfFrame;
            _camera.CameraDisconnected -= OnCameraDisconnected;
            _camera.Error              -= OnCameraError;
        }

        // --- EVF -----------------------------------------------------------------

        private void StartEvf()
        {
            _lastEvfFrameTime = DateTime.UtcNow; // start the stall clock from now, not epoch
            _evfRunning = true;
            _camera.StartLiveView();
            RequestNextEvfFrame();

            _evfWatchdog = new System.Threading.Timer(_ =>
            {
                if (!_evfRunning || _shooting) return;

                _evfFramePending = false;
                RequestNextEvfFrame();

                // If no EVF frame has arrived in 5 s, tell the user the preview is unavailable
                if ((DateTime.UtcNow - _lastEvfFrameTime).TotalSeconds > 5)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (!_shooting && StatusText.Text != "Camera preview unavailable.")
                        {
                            Log.Warning("EVF stall detected — no frame for >5 s");
                            StatusText.Text = "Camera preview unavailable.";
                        }
                    });
                }
            }, null, 500, 500);
        }

        private void RequestNextEvfFrame()
        {
            if (!_evfRunning || _shooting || _evfFramePending) return;
            _evfFramePending = true;
            _camera.RequestEvfFrame();
        }

        private void OnEvfFrame(object? sender, BitmapSource frame)
        {
            _evfFramePending = false;
            _lastEvfFrameTime = DateTime.UtcNow;
            Dispatcher.BeginInvoke(() =>
            {
                EvfImage.Source = frame;
                // Clear the stall warning once frames resume
                if (StatusText.Text == "Camera preview unavailable.")
                    StatusText.Text = string.Empty;
            });
            RequestNextEvfFrame();
        }

        // --- Photo sequence ------------------------------------------------------

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_shooting) return;
            StartButton.IsEnabled = false;
            _capturedPaths.Clear();
            _sequenceAborted = false;
            ResetDots();

            Log.Information("Photo sequence started");

            for (int i = 1; i <= 3; i++)
            {
                StatusText.Text = $"Photo {i} of 3 — get ready!";
                await RunCountdown(3);

                _shooting = true;
                StatusText.Text = "Please wait…";

                string path;
                try
                {
                    Log.Information("Taking photo {N}/3", i);
                    path = await _camera.TakePictureAsync();
                    _capturedPaths.Add(path);
                }
                catch (OperationCanceledException)
                {
                    _shooting = false;
                    if (_sequenceAborted)
                    {
                        // Disconnect overlay already shown by OnCameraDisconnected
                        Log.Warning("Photo {N} cancelled — camera disconnected mid-shoot", i);
                        return;
                    }
                    // 30-second timeout elapsed without a download completing
                    Log.Error("Photo {N} timed out waiting for download", i);
                    StatusText.Text = "Camera timed out — check the USB connection and try again.";
                    StartButton.IsEnabled = true;
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    // Hard EDSDK error propagated immediately from CameraService
                    _shooting = false;
                    Log.Error("Hard camera error on photo {N}: {Message}", i, ex.Message);
                    StatusText.Text = $"Camera error — {ex.Message}";
                    StartButton.IsEnabled = true;
                    return;
                }

                await HoldFlash();
                await ShowCapturedPreview(path);
                LightUpDot(i);

                if (i < 3)
                {
                    _shooting = false;
                    RequestNextEvfFrame();
                    StatusText.Text = $"Get ready for photo {i + 1}…";
                    await Task.Delay(5000);
                }
            }

            Log.Information("Sequence complete — {Count} photos", _capturedPaths.Count);
            await FadeOutFlash();

            Dispatcher.Invoke(() =>
            {
                var window = Window.GetWindow(this) as MainWindow;
                window?.NavigateTo(new ResultsPage(_capturedPaths));
            });
        }

        // --- Countdown -----------------------------------------------------------

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

        // --- Flash / preview -----------------------------------------------------

        private Task HoldFlash()
        {
            var tcs = new TaskCompletionSource();
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(60));
            anim.Completed += (_, _) => tcs.SetResult();
            FlashOverlay.BeginAnimation(UIElement.OpacityProperty, anim);
            return tcs.Task;
        }

        private Task FadeOutFlash()
        {
            var tcs = new TaskCompletionSource();
            var anim = new DoubleAnimation(FlashOverlay.Opacity, 0, TimeSpan.FromMilliseconds(400));
            anim.Completed += (_, _) => tcs.SetResult();
            FlashOverlay.BeginAnimation(UIElement.OpacityProperty, anim);
            return tcs.Task;
        }

        private async Task ShowCapturedPreview(string path)
        {
            try
            {
                BitmapImage bmp = await Task.Run(() =>
                {
                    var b = new BitmapImage();
                    b.BeginInit();
                    b.UriSource = new Uri(path);
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.EndInit();
                    b.Freeze();
                    return b;
                });

                await Dispatcher.InvokeAsync(() => EvfImage.Source = bmp);
                await FadeOutFlash();
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load captured preview for {Path}", path);
                await FadeOutFlash();
            }
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
            _sequenceAborted = true;
            _evfRunning = false;
            _evfWatchdog?.Dispose();

            Dispatcher.Invoke(() => DisconnectOverlay.Visibility = Visibility.Visible);
        }

        private void OnCameraError(object? sender, string msg)
        {
            Log.Error("Camera error on shoot page: {Message}", msg);
            // Only update status text when not mid-shoot; hard errors during capture are
            // propagated via TakePictureAsync's exception path instead
            if (!_shooting)
                Dispatcher.Invoke(() => StatusText.Text = $"Error: {msg}");
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User navigated back to GreetingPage");
            _evfWatchdog?.Dispose();
            _evfRunning = false;
            var window = Window.GetWindow(this) as MainWindow;
            window?.NavigateTo(new GreetingPage());
        }
    }
}
