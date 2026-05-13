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

        private int? _sessionId;
        private string _sessionDir = string.Empty;

        public ShootPage()
        {
            InitializeComponent();
            InitialiseSession();
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void InitialiseSession()
        {
            var eventId = App.Settings.ActiveEventId;
            if (!eventId.HasValue)
            {
                Log.Warning("ShootPage opened with no active event — session will not be tracked");
                _sessionDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Photobooth", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(_sessionDir);
                _camera.SessionDirectory = _sessionDir;
                return;
            }

            var ev = App.Events.GetById(eventId.Value);
            if (ev is null)
            {
                Log.Warning("Active event {Id} not found — session will not be tracked", eventId.Value);
                _sessionDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Photobooth", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(_sessionDir);
                _camera.SessionDirectory = _sessionDir;
                return;
            }

            try
            {
                var session = App.Events.StartSession(ev.Id);
                _sessionId = session.Id;

                _sessionDir = App.FileStorage.GetPhotosPath(ev.Slug);
                Directory.CreateDirectory(_sessionDir);
                _camera.SessionDirectory = _sessionDir;

                Log.Information("ShootPage — session {SessionId}, dir: {Dir}", _sessionId, _sessionDir);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create DB session — photos will not be tracked");
                _sessionDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Photobooth", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(_sessionDir);
                _camera.SessionDirectory = _sessionDir;
            }
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
                return;
            }

            StartEvf();
            _ = AutoStartAsync();
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

            // 50 ms safety-net: if a DownloadEvfCommand exhausted all its NOT_READY retries
            // (rare) and returned without a frame, this ensures the loop restarts quickly
            // rather than waiting for the 500 ms CommandProcessor retry path.
            // Also provides the 5-second stall detection.
            _evfWatchdog = new System.Threading.Timer(_ =>
            {
                if (!_evfRunning || _shooting) return;

                _evfFramePending = false;
                RequestNextEvfFrame();

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
            }, null, 50, 50);
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
            // Render priority keeps EVF ahead of lower-priority UI work (layout, animations)
            // and prevents decoded frames from queuing up behind other dispatcher callbacks.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                EvfImage.Source = frame;
                if (StatusText.Text == "Camera preview unavailable.")
                    StatusText.Text = string.Empty;
            });
            RequestNextEvfFrame();
        }

        // --- Photo sequence ------------------------------------------------------

        private async Task AutoStartAsync()
        {
            // Brief pause so the EVF feed is visible before the countdown starts
            await Task.Delay(1500);
            if (!_evfRunning || _shooting) return;
            await RunSequenceAsync();
        }

        private async Task RunSequenceAsync()
        {
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
                    var rawPath = await _camera.TakePictureAsync();

                    var date    = DateTime.Today.ToString("yyyyMMdd");
                    var sid     = _sessionId.HasValue ? _sessionId.Value.ToString() : "0";
                    var stem    = Path.GetFileNameWithoutExtension(rawPath);
                    var ext     = Path.GetExtension(rawPath);
                    var dest    = Path.Combine(_sessionDir, $"{stem}_{date}_s{sid}_p{i}{ext}");
                    File.Move(rawPath, dest, overwrite: true);
                    path = dest;

                    _capturedPaths.Add(path);

                    if (_sessionId.HasValue)
                    {
                        try { App.Events.RecordPhoto(_sessionId.Value, i, path); }
                        catch (Exception dbEx) { Log.Error(dbEx, "Failed to record photo {N} in DB", i); }
                    }
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
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    // Hard EDSDK error propagated immediately from CameraService
                    _shooting = false;
                    Log.Error("Hard camera error on photo {N}: {Message}", i, ex.Message);
                    StatusText.Text = $"Camera error — {ex.Message}";
                    return;
                }

                await HoldFlash();
                await ShowCapturedPreview(path);
                LightUpDot(i);

                if (i < 3)
                {
                    _shooting = false;
                    RequestNextEvfFrame();
                }
            }

            Log.Information("Sequence complete — {Count} photos", _capturedPaths.Count);
            await FadeOutFlash();

            Dispatcher.Invoke(() =>
            {
                var window = Window.GetWindow(this) as MainWindow;
                window?.NavigateTo(new ResultsPage(_capturedPaths, _sessionId ?? 0));
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

            if (_sessionId.HasValue && _capturedPaths.Count == 0)
            {
                try { App.Events.AbandonSession(_sessionId.Value); _sessionId = null; }
                catch (Exception ex) { Log.Error(ex, "Failed to abandon session on disconnect"); }
            }

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

            if (_sessionId.HasValue && _capturedPaths.Count == 0)
            {
                try { App.Events.AbandonSession(_sessionId.Value); }
                catch (Exception ex) { Log.Error(ex, "Failed to abandon session on back"); }
            }

            var window = Window.GetWindow(this) as MainWindow;
            window?.NavigateTo(new GreetingPage());
        }
    }
}
