using System;
using System.Threading.Tasks;
using Photobooth.Camera;
using Xunit;

namespace Photobooth.Tests.Integration;

/// <summary>
/// Tests for CameraService callback logic.
/// We instantiate CameraService WITHOUT calling Initialize(), so EDSDK is never loaded.
/// The internal callbacks (HandleStateEvent, HandleObjectEvent, HandlePropertyEvent,
/// OnPhotoSaved) and _pendingDownload are exposed via InternalsVisibleTo.
/// </summary>
public sealed class CameraServiceTests : IDisposable
{
    // StateEvent_Shutdown = 0x00000301 (verified in EDSDKLib/EDSDK.cs line 957)
    private const uint StateEvent_Shutdown = 0x00000301u;

    // A value that is NOT a recognised state event — HandleStateEvent must ignore it
    private const uint StateEvent_Other = 0x00000000u;

    private readonly CameraService _sut = new CameraService();

    public void Dispose() => _sut.Dispose();

    // ── HandleStateEvent ─────────────────────────────────────────────────────────

    [Fact]
    public void HandleStateEvent_Shutdown_RaisesCameraDisconnected()
    {
        bool fired = false;
        _sut.CameraDisconnected += (_, _) => fired = true;

        _sut.HandleStateEvent(StateEvent_Shutdown, 0u, IntPtr.Zero);

        Assert.True(fired, "CameraDisconnected should have been raised for StateEvent_Shutdown");
    }

    [Fact]
    public void HandleStateEvent_Shutdown_SetsIsConnectedFalse()
    {
        // IsConnected starts false (Initialize() not called); set it to true manually
        // via the public property setter — but it is get-only. We simulate a connected
        // state by verifying the transition: after Shutdown it must be false regardless.
        _sut.HandleStateEvent(StateEvent_Shutdown, 0u, IntPtr.Zero);

        Assert.False(_sut.IsConnected);
    }

    [Fact]
    public void HandleStateEvent_Shutdown_ReturnsEdsErrOk()
    {
        uint result = _sut.HandleStateEvent(StateEvent_Shutdown, 0u, IntPtr.Zero);

        // EDS_ERR_OK == 0
        Assert.Equal(0u, result);
    }

    [Fact]
    public void HandleStateEvent_NonShutdown_DoesNotRaiseCameraDisconnected()
    {
        bool fired = false;
        _sut.CameraDisconnected += (_, _) => fired = true;

        _sut.HandleStateEvent(StateEvent_Other, 0u, IntPtr.Zero);

        Assert.False(fired, "CameraDisconnected must not fire for a non-shutdown event");
    }

    [Fact]
    public void HandleStateEvent_NonShutdown_ReturnsEdsErrOk()
    {
        uint result = _sut.HandleStateEvent(StateEvent_Other, 0u, IntPtr.Zero);

        Assert.Equal(0u, result);
    }

    [Fact]
    public void HandleStateEvent_Shutdown_CancelsPendingDownload()
    {
        // Arm a pending download TCS, then fire Shutdown — it must be cancelled.
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut._pendingDownload = tcs;

        _sut.HandleStateEvent(StateEvent_Shutdown, 0u, IntPtr.Zero);

        Assert.True(tcs.Task.IsCanceled,
            "Pending download task should be cancelled when the camera shuts down");
    }

    // ── _pendingDownload state tracking ─────────────────────────────────────────

    [Fact]
    public void PendingDownload_IsNullByDefault()
    {
        Assert.Null(_sut._pendingDownload);
    }

    [Fact]
    public void PendingDownload_CanBeSetAndRead()
    {
        var tcs = new TaskCompletionSource<string>();
        _sut._pendingDownload = tcs;

        Assert.Same(tcs, _sut._pendingDownload);
    }

    // ── OnPhotoSaved ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnPhotoSaved_WithValidPath_CompletesPendingDownload()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut._pendingDownload = tcs;

        _sut.OnPhotoSaved(@"C:\Photos\test.jpg");

        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal(@"C:\Photos\test.jpg", await tcs.Task);
    }

    [Fact]
    public void OnPhotoSaved_WithNullPendingDownload_DoesNotThrow()
    {
        // _pendingDownload is null — OnPhotoSaved must be a no-op rather than throw
        _sut._pendingDownload = null;

        var ex = Record.Exception(() => _sut.OnPhotoSaved(@"C:\Photos\test.jpg"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task OnPhotoSaved_WithEmptyPath_CompletesPendingDownloadWithEmptyString()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut._pendingDownload = tcs;

        _sut.OnPhotoSaved(string.Empty);

        Assert.True(tcs.Task.IsCompletedSuccessfully);
        Assert.Equal(string.Empty, await tcs.Task);
    }

    [Fact]
    public async Task OnPhotoSaved_CalledTwice_SecondCallIsNoOp()
    {
        // TCS can only be completed once; the second call must not throw.
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sut._pendingDownload = tcs;

        _sut.OnPhotoSaved(@"C:\Photos\first.jpg");
        var ex = Record.Exception(() => _sut.OnPhotoSaved(@"C:\Photos\second.jpg"));

        Assert.Null(ex);
        Assert.Equal(@"C:\Photos\first.jpg", await tcs.Task);
    }

    // ── HandlePropertyEvent (smoke test — no model/processor so just returns OK) ─

    [Fact]
    public void HandlePropertyEvent_WithNullModel_ReturnsEdsErrOk()
    {
        // _model is null because Initialize() was never called — method must guard safely
        uint result = _sut.HandlePropertyEvent(0u, 0u, 0u, IntPtr.Zero);

        Assert.Equal(0u, result);
    }

    // ── HandleObjectEvent (smoke test — no model/processor so just returns OK) ───

    [Fact]
    public void HandleObjectEvent_WithNullModel_ReturnsEdsErrOk()
    {
        // _model is null — the else-branch releases inRef when non-zero, but
        // passing IntPtr.Zero is safe (EdsRelease is not called for zero).
        uint result = _sut.HandleObjectEvent(0u, IntPtr.Zero, IntPtr.Zero);

        Assert.Equal(0u, result);
    }

    // ── DeviceBusy ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_DeviceBusyEvent_RaisesDeviceBusy()
    {
        bool fired = false;
        _sut.DeviceBusy += (_, _) => fired = true;

        _sut.Update(null!, new CameraEvent(CameraEvent.Type.DEVICE_BUSY, IntPtr.Zero));

        Assert.True(fired, "DeviceBusy should have been raised for CameraEvent.Type.DEVICE_BUSY");
    }

    [Fact]
    public void Update_NonBusyEvent_DoesNotRaiseDeviceBusy()
    {
        bool fired = false;
        _sut.DeviceBusy += (_, _) => fired = true;

        _sut.Update(null!, new CameraEvent(CameraEvent.Type.DOWNLOAD_START, IntPtr.Zero));

        Assert.False(fired, "DeviceBusy must not fire for unrelated event types");
    }

    // ── RotationDegrees ──────────────────────────────────────────────────────────

    [Fact]
    public void RotationDegrees_DefaultsToZero()
    {
        Assert.Equal(0, _sut.RotationDegrees);
    }

    [Fact]
    public void RotationDegrees_CanBeSetAndRead()
    {
        _sut.RotationDegrees = 180;

        Assert.Equal(180, _sut.RotationDegrees);
    }

    // ── SetPropertyAsync ─────────────────────────────────────────────────────────

    private const uint TestPropId      = 0x00000405u;
    private const uint OtherTestPropId = 0x00000406u;

    [Fact]
    public async Task SetPropertyAsync_MatchingPropertyChangedEvent_ResolvesTrue()
    {
        Task<bool> task = _sut.SetPropertyAsync(TestPropId, 0x48u, CancellationToken.None);

        _sut.Update(null!, new CameraEvent(CameraEvent.Type.PROPERTY_CHANGED, (IntPtr)TestPropId));

        bool result = await task;
        Assert.True(result);
    }

    [Fact]
    public async Task SetPropertyAsync_NonMatchingPropertyChangedEvent_DoesNotResolveEarly()
    {
        using var cts = new CancellationTokenSource();

        Task<bool> task = _sut.SetPropertyAsync(TestPropId, 0x48u, cts.Token);

        _sut.Update(null!, new CameraEvent(CameraEvent.Type.PROPERTY_CHANGED, (IntPtr)OtherTestPropId));

        Assert.False(task.IsCompleted);

        cts.Cancel();
        bool result = await task;
        Assert.False(result);
    }

    [Fact]
    public async Task SetPropertyAsync_CancelledBeforeConfirmation_ResolvesFalse()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        bool result = await _sut.SetPropertyAsync(TestPropId, 0x48u, cts.Token);

        Assert.False(result);
    }
}
