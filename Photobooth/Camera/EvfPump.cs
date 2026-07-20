// Photobooth/Camera/EvfPump.cs
using System;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Photobooth.Camera;

/// <summary>
/// Self-perpetuating EVF frame-pump. Call Start() to begin; Stop() or Dispose() to halt.
/// </summary>
public sealed class EvfPump : IDisposable
{
    private readonly CameraService _camera;
    private readonly Dispatcher _dispatcher;
    private readonly Action<BitmapSource> _renderFrame;
    private readonly Action? _onStall;
    private readonly Func<bool>? _pauseGuard;
    private readonly int _watchdogMs;

    private bool _running;
    private bool _framePending;
    private DateTime? _pendingSince;
    private DateTime _lastFrameTime;
    private Timer? _watchdog;
    private readonly int _stallThresholdMs;

    /// <param name="camera">Live CameraService instance.</param>
    /// <param name="dispatcher">UI dispatcher of the owning view.</param>
    /// <param name="renderFrame">Called on the UI thread with each decoded frame.</param>
    /// <param name="onStall">Called on the UI thread when no frame arrives for 5 seconds. Optional.</param>
    /// <param name="pauseGuard">If provided and returns true, the next frame request is skipped.
    ///   Use to suppress EVF requests during active capture (ShootPage pattern).</param>
    /// <param name="watchdogMs">Watchdog interval in milliseconds. Default 100.</param>
    public EvfPump(
        CameraService camera,
        Dispatcher dispatcher,
        Action<BitmapSource> renderFrame,
        Action? onStall = null,
        Func<bool>? pauseGuard = null,
        int watchdogMs = 100)
    {
        _camera      = camera;
        _dispatcher  = dispatcher;
        _renderFrame = renderFrame;
        _onStall     = onStall;
        _pauseGuard  = pauseGuard;
        _watchdogMs  = watchdogMs;

        // A genuine stall (no response at all) is treated as "no frame for several
        // watchdog intervals", with a floor so a tight watchdogMs (e.g. 50ms) doesn't
        // mistake ordinary USB round-trip jitter for a stall.
        _stallThresholdMs = Math.Max(watchdogMs * 5, 1000);
    }

    public void Start()
    {
        _lastFrameTime = DateTime.UtcNow;
        _running       = true;
        _camera.EvfFrameReady += OnFrame;
        _camera.StartLiveView();
        RequestNext();

        _watchdog = new Timer(_ =>
        {
            if (!_running) return;

            // Only clear a pending request if it has genuinely stalled (no response
            // for _stallThresholdMs). Resetting _framePending unconditionally on every
            // tick — the previous behavior — defeated the one-frame-in-flight throttle
            // in RequestNext() below, flooding the shared camera command queue with a
            // new frame request every tick regardless of whether the prior one had
            // actually completed.
            if (_framePending && _pendingSince.HasValue &&
                (DateTime.UtcNow - _pendingSince.Value).TotalMilliseconds > _stallThresholdMs)
            {
                _framePending = false;
            }

            RequestNext();
            if (_onStall != null && (DateTime.UtcNow - _lastFrameTime).TotalSeconds > 5)
                _dispatcher.BeginInvoke(_onStall);
        }, null, _watchdogMs, _watchdogMs);
    }

    public void Stop()
    {
        _running = false;
        _watchdog?.Dispose();
        _watchdog = null;
        _camera.EvfFrameReady -= OnFrame;
        _camera.StopLiveView();
    }

    private void RequestNext()
    {
        if (!_running || _framePending || (_pauseGuard?.Invoke() ?? false)) return;
        _framePending = true;
        _pendingSince = DateTime.UtcNow;
        _camera.RequestEvfFrame();
    }

    private void OnFrame(object? sender, BitmapSource frame)
    {
        _framePending  = false;
        _pendingSince  = null;
        _lastFrameTime = DateTime.UtcNow;
        _dispatcher.BeginInvoke(DispatcherPriority.Render, () => _renderFrame(frame));
        RequestNext();
    }

    public void Dispose() => Stop();
}
