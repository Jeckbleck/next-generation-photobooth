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
        private bool _paywallActive;
        private CancellationTokenSource? _retakeCts;

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
                _paywallActive = false;
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
                _paywallActive = false;
                _sessionDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "Photobooth", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(_sessionDir);
                _camera.SessionDirectory = _sessionDir;
                return;
            }

            _paywallActive = ev.PaywallEnabled;

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
                _paywallActive = false;
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

                int  retakeCount     = 0;
                bool retakeRequested;

                do
                {
                    retakeRequested = false;

                    string? path;
                    try
                    {
                        path = await TakePhotoWithRetryAsync(i, maxAttempts: 3, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        _shooting = false;
                        CaptureSpinner.Visibility = Visibility.Collapsed;
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

                    if (path is null)
                    {
                        foreach (var p in _capturedPaths)
                            BitmapHelper.DeleteWithThumbnail(p);
                        if (_sessionId.HasValue)
                        {
                            try { _events.AbandonSession(_sessionId.Value); _sessionId = null; }
                            catch (Exception ex) { Log.Error(ex, "Failed to abandon session after exhausted retries"); }
                        }
                        StatusText.Text = "Camera unavailable — please ask for assistance.";
                        _flow.Trigger(FlowTrigger.SessionAborted);
                        return;
                    }

                    _ = Task.Run(() => BitmapHelper.GenerateThumbnail(path));

                    // _shooting stays true: EVF pump stays paused so captured photo stays visible
                    CaptureSpinner.Visibility = Visibility.Collapsed;
                    await HoldFlash();
                    await ShowCapturedPreview(path, ct);

                    bool canRetake = !_paywallActive &&
                                     (_settings.MaxRetakesPerSlot == 0 || retakeCount < _settings.MaxRetakesPerSlot);

                    if (canRetake)
                    {
                        Dispatcher.Invoke(() => RetakeButton.IsEnabled = true);
                        retakeRequested = await ShowRetakeWindowAsync(_settings.RetakeHoldSeconds, ct);
                        Dispatcher.Invoke(() => RetakeButton.IsEnabled = false);
                    }
                    else
                    {
                        try { await Task.Delay(_settings.RetakeHoldSeconds * 1000, ct); }
                        catch (OperationCanceledException) { _shooting = false; return; }
                    }

                    _shooting = false;

                    if (ct.IsCancellationRequested)
                    {
                        BitmapHelper.DeleteWithThumbnail(path);
                        return;
                    }

                    if (retakeRequested)
                    {
                        Log.Information("Retake requested for photo {N} (retake #{Count})", i, retakeCount + 1);
                        BitmapHelper.DeleteWithThumbnail(path);
                        retakeCount++;
                    }
                    else
                    {
                        _capturedPaths.Add(path);

                        if (_sessionId.HasValue)
                        {
                            try { _events.RecordPhoto(_sessionId.Value, i, path); }
                            catch (Exception dbEx) { Log.Error(dbEx, "Failed to record photo {N} in DB", i); }
                        }

                        LightUpDot(i);
                        _ = SetThumbnailAsync(i, path);
                    }

                } while (retakeRequested && !ct.IsCancellationRequested);

                if (ct.IsCancellationRequested) return;
            }

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
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load captured preview for {Path}", path);
                await FadeOutFlash();
            }
        }

        private void ResetDots()
        {
            Thumb1.Source = null;
            Thumb2.Source = null;
            Thumb3.Source = null;
        }

        private void LightUpDot(int n) { }

        private async Task SetThumbnailAsync(int n, string path)
        {
            try
            {
                var src = await Task.Run(() => BitmapHelper.LoadThumbnail(path, fallbackDecodeWidth: 200));
                var img = n switch { 1 => Thumb1, 2 => Thumb2, _ => Thumb3 };
                img.Source = src;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not load thumbnail for slot {N}", n);
            }
        }

        // --- Error / disconnect --------------------------------------------------

        private void OnCameraDisconnected(object? sender, EventArgs e)
        {
            Log.Warning("Camera disconnected during shoot page");
            _sequenceAborted = true;
            _sessionCts.Cancel();
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

        private async Task RecoverCameraAsync(CancellationToken ct)
        {
            _evfPump?.Stop();
            StatusText.Text = "Camera is getting ready — one moment…";
            try { await Task.Delay(3000, ct); }
            catch (OperationCanceledException) { return; }
            _evfPump?.Start();
        }

        private async Task<string?> TakePhotoWithRetryAsync(int slot, int maxAttempts, CancellationToken ct)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                StatusText.Text = $"Photo {slot} of 3 — get ready!";
                await RunCountdown(_settings.CountdownSeconds, ct);
                if (ct.IsCancellationRequested) return null;

                _shooting = true;
                StatusText.Text = "Please wait…";
                CaptureSpinner.Visibility = Visibility.Visible;

                try
                {
                    Log.Information("Taking photo {N}/3 (attempt {Attempt})", slot, attempt);
                    var rawPath = await _camera.TakePictureAsync(ct);

                    var date = DateTime.Today.ToString("yyyyMMdd");
                    var sid  = _sessionId.HasValue ? _sessionId.Value.ToString() : "0";
                    var stem = Path.GetFileNameWithoutExtension(rawPath);
                    var ext  = Path.GetExtension(rawPath);
                    var dest = Path.Combine(_sessionDir, $"{stem}_{date}_s{sid}_p{slot}{ext}");
                    File.Move(rawPath, dest, overwrite: true);

                    CaptureSpinner.Visibility = Visibility.Collapsed;
                    return dest; // _shooting stays true — caller owns the preview hold
                }
                catch (InvalidOperationException ex)
                {
                    _shooting = false;
                    CaptureSpinner.Visibility = Visibility.Collapsed;
                    Log.Error("Hard camera error on photo {Slot} attempt {Attempt}: {Message}", slot, attempt, ex.Message);

                    if (attempt == maxAttempts)
                        return null;

                    await RecoverCameraAsync(ct);
                }
            }

            return null;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User navigated back — aborting session");
            _sessionCts.Cancel();
            _evfPump?.Stop();

            foreach (var p in _capturedPaths)
                BitmapHelper.DeleteWithThumbnail(p);

            if (_sessionId.HasValue)
            {
                try { _events.AbandonSession(_sessionId.Value); _sessionId = null; }
                catch (Exception ex) { Log.Error(ex, "Failed to abandon session on back"); }
            }

            _flow.Trigger(FlowTrigger.SessionAborted);
        }

        private async Task<bool> ShowRetakeWindowAsync(int holdSeconds, CancellationToken sessionCt)
        {
            _retakeCts = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(sessionCt, _retakeCts.Token);
            bool tappedRetake = false;
            try
            {
                await Task.Delay(holdSeconds * 1000, linked.Token);
            }
            catch (OperationCanceledException)
            {
                tappedRetake = _retakeCts.IsCancellationRequested && !sessionCt.IsCancellationRequested;
            }
            finally
            {
                _retakeCts.Dispose();
                _retakeCts = null;
            }
            return tappedRetake;
        }

        private void RetakeButton_Click(object sender, RoutedEventArgs e)
        {
            _retakeCts?.Cancel();
        }
    }
}
