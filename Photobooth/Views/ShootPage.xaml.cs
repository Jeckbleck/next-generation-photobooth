using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Photobooth.Camera;
using Photobooth.Helpers;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views
{
    public partial class ShootPage : Page
    {
        private readonly CameraService       _camera;
        private readonly IEventService       _events;
        private readonly SettingsManager     _settings;
        private readonly IFileStorageService _fileStorage;
        private readonly FlowController      _flow;

        private readonly List<string> _capturedPaths = new();
        private volatile bool _shooting;
        private bool _sequenceAborted;
        private CancellationTokenSource _sessionCts = new();
        private EvfPump? _evfPump;

        private int? _sessionId;
        private string _sessionDir = string.Empty;

        public ShootPage(
            CameraService       camera,
            IEventService       events,
            SettingsManager     settings,
            IFileStorageService fileStorage,
            FlowController      flow)
        {
            _camera      = camera;
            _events      = events;
            _settings    = settings;
            _fileStorage = fileStorage;
            _flow        = flow;
            InitializeComponent();

            InitialiseSession();
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void InitialiseSession()
        {
            var eventId = _settings.ActiveEventId;
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

            var ev = _events.GetById(eventId.Value);
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
                var session = _events.StartSession(ev.Id);
                _sessionId = session.Id;

                _sessionDir = _fileStorage.GetPhotosPath(ev.Slug);
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
            _camera.CameraDisconnected += OnCameraDisconnected;
            _camera.Error              += OnCameraError;

            if (!_camera.IsConnected)
            {
                Log.Warning("ShootPage loaded but camera not connected");
                StatusText.Text = "No camera detected — go back and check the connection.";
                return;
            }

            _evfPump?.Stop();
            _evfPump = new EvfPump(
                _camera,
                Dispatcher,
                frame =>
                {
                    EvfImage.Source = frame;
                    if (StatusText.Text == "Camera preview unavailable.")
                        StatusText.Text = string.Empty;
                },
                onStall: () =>
                {
                    if (!_shooting)
                    {
                        Log.Warning("EVF stall detected — no frame for >5 s");
                        StatusText.Text = "Camera preview unavailable.";
                    }
                },
                pauseGuard: () => _shooting,
                watchdogMs: 50);

            _evfPump.Start();
            _ = AutoStartAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Log.Information("ShootPage unloaded — stopping EVF");
            _evfPump?.Stop();
            _sessionCts.Cancel();

            _camera.CameraDisconnected -= OnCameraDisconnected;
            _camera.Error              -= OnCameraError;
        }

        // --- Photo sequence ------------------------------------------------------

        private async Task AutoStartAsync()
        {
            try { await Task.Delay(1500, _sessionCts.Token); }
            catch (OperationCanceledException) { return; }
            if (_shooting) return;
            await RunSequenceAsync();
        }

        private async Task RunSequenceAsync()
        {
            _capturedPaths.Clear();
            _sequenceAborted = false;
            ResetDots();
            var ct = _sessionCts.Token;

            Log.Information("Photo sequence started");

            for (int i = 1; i <= 3; i++)
            {
                if (ct.IsCancellationRequested) return;

                StatusText.Text = $"Photo {i} of 3 — get ready!";
                await RunCountdown(_settings.CountdownSeconds, ct);

                if (ct.IsCancellationRequested) return;

                _shooting = true;
                StatusText.Text = "Please wait…";

                string path;
                try
                {
                    Log.Information("Taking photo {N}/3", i);
                    var rawPath = await _camera.TakePictureAsync(ct);

                    var date    = DateTime.Today.ToString("yyyyMMdd");
                    var sid     = _sessionId.HasValue ? _sessionId.Value.ToString() : "0";
                    var stem    = Path.GetFileNameWithoutExtension(rawPath);
                    var ext     = Path.GetExtension(rawPath);
                    var dest    = Path.Combine(_sessionDir, $"{stem}_{date}_s{sid}_p{i}{ext}");
                    File.Move(rawPath, dest, overwrite: true);
                    path = dest;

                    _ = Task.Run(() => BitmapHelper.GenerateThumbnail(dest));

                    _capturedPaths.Add(path);

                    if (_sessionId.HasValue)
                    {
                        try { _events.RecordPhoto(_sessionId.Value, i, path); }
                        catch (Exception dbEx) { Log.Error(dbEx, "Failed to record photo {N} in DB", i); }
                    }
                }
                catch (OperationCanceledException)
                {
                    _shooting = false;
                    if (_sequenceAborted)
                    {
                        Log.Warning("Photo {N} cancelled — camera disconnected mid-shoot", i);
                        return;
                    }
                    if (ct.IsCancellationRequested) return;
                    Log.Error("Photo {N} timed out waiting for download", i);
                    StatusText.Text = "Camera timed out — check the USB connection and try again.";
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    _shooting = false;
                    Log.Error("Hard camera error on photo {N}: {Message}", i, ex.Message);
                    StatusText.Text = $"Camera error — {ex.Message}";
                    return;
                }

                await HoldFlash();
                await ShowCapturedPreview(path, ct);
                LightUpDot(i);

                _shooting = false;
            }

            if (ct.IsCancellationRequested) return;

            Log.Information("Sequence complete — {Count} photos", _capturedPaths.Count);
            await FadeOutFlash();

            Dispatcher.Invoke(() =>
                _flow.Trigger(FlowTrigger.ShotsDone,
                    new FlowContext { PhotoPaths = _capturedPaths, SessionId = _sessionId ?? 0 }));
        }

        // --- Countdown -----------------------------------------------------------

        private async Task RunCountdown(int seconds, CancellationToken ct = default)
        {
            CountdownText.Visibility = Visibility.Visible;
            for (int s = seconds; s >= 1; s--)
            {
                CountdownText.Text = s.ToString();
                await Task.Delay(1000, ct);
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

        private async Task ShowCapturedPreview(string path, CancellationToken ct = default)
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
                }, ct);

                await Dispatcher.InvokeAsync(() => EvfImage.Source = bmp);
                await FadeOutFlash();
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException) { }
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
            _evfPump?.Stop();

            if (_sessionId.HasValue && _capturedPaths.Count == 0)
            {
                try { _events.AbandonSession(_sessionId.Value); _sessionId = null; }
                catch (Exception ex) { Log.Error(ex, "Failed to abandon session on disconnect"); }
            }

            Dispatcher.Invoke(() => DisconnectOverlay.Visibility = Visibility.Visible);
        }

        private void OnCameraError(object? sender, string msg)
        {
            Log.Error("Camera error on shoot page: {Message}", msg);
            if (!_shooting)
                Dispatcher.Invoke(() => StatusText.Text = $"Error: {msg}");
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User navigated back — aborting session");
            _sessionCts.Cancel();
            _evfPump?.Stop();

            foreach (var p in _capturedPaths)
            {
                try { File.Delete(p); }
                catch (Exception ex) { Log.Warning(ex, "Failed to delete abandoned photo {Path}", p); }
            }

            if (_sessionId.HasValue)
            {
                try { _events.AbandonSession(_sessionId.Value); _sessionId = null; }
                catch (Exception ex) { Log.Error(ex, "Failed to abandon session on back"); }
            }

            _flow.Trigger(FlowTrigger.SessionAborted);
        }

        private void RetakeButton_Click(object sender, RoutedEventArgs e) { }
    }
}
