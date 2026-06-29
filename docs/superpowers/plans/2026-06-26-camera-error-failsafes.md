# Camera Error Failsafes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hard-crash behaviour on Canon EDSDK capture errors with classified retry/recover/halt logic so guests see plain-language prompts instead of raw hex codes.

**Architecture:** A static `CameraErrorClassifier` maps EDSDK codes to three kinds: `Retryable` (silent auto-retry), `GuestFixable` (guest message + repeat countdown), `Fatal` (operator alert, sequence halts). `CameraService.TakePictureAsync` returns a `CaptureResult` record instead of throwing on known errors. `ShootPage` dispatches on the result kind. EVF stall recovery is also tightened from 5 s to 1.5 s with a friendlier status message.

**Tech Stack:** C# 12 / .NET 8, WPF, Canon EDSDK (EDSDKLib wrapper in `Photobooth/Camera/EDSDKLib/EDSDK.cs`).

## Global Constraints

- Target framework: `net8.0-windows` (WPF).
- No new NuGet packages.
- No card-related error codes (`0x8D06`–`0x8D08`) — camera has no memory card.
- `CameraErrorKind`, `CaptureResult`, and `CameraErrorClassifier` all live in `namespace Photobooth.Camera`.
- All EDSDK constant references go through `EDSDKLib.EDSDK.*` (not raw hex literals).
- Build command: `dotnet build Photobooth/Photobooth.csproj` — must produce `Build succeeded.` with 0 errors.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `Photobooth/Camera/CameraErrorKind.cs` | Create | Enum: `Retryable`, `GuestFixable`, `Fatal` |
| `Photobooth/Camera/CaptureResult.cs` | Create | Record returned by `TakePictureAsync` |
| `Photobooth/Camera/CameraErrorClassifier.cs` | Create | Maps EDSDK codes → `(Kind, RetryDelayMs, GuestMessage?)` |
| `Photobooth/Camera/CameraService.cs` | Modify | Change `_pendingDownload` type; update `TakePictureAsync`, `OnPhotoSaved`, `Update`, `HandleStateEvent` |
| `Photobooth/Camera/EvfPump.cs` | Modify | Stall threshold `> 5` → `> 1.5` |
| `Photobooth/Views/ShootPage.xaml` | Modify | Add `FatalOverlay` element |
| `Photobooth/Views/ShootPage.xaml.cs` | Modify | Result dispatch in `RunSequenceAsync`; stall message; `ShowFatalOverlay` |

---

### Task 1: Error types — `CameraErrorKind`, `CaptureResult`, `CameraErrorClassifier`

**Files:**
- Create: `Photobooth/Camera/CameraErrorKind.cs`
- Create: `Photobooth/Camera/CaptureResult.cs`
- Create: `Photobooth/Camera/CameraErrorClassifier.cs`

**Interfaces:**
- Produces:
  - `CameraErrorKind` enum with values `Retryable`, `GuestFixable`, `Fatal`
  - `CaptureResult` record with `(bool Success, string? Path, CameraErrorKind Kind, uint EdsCode, string? GuestMessage)` and factory methods `Ok(string)`, `Failure(CameraErrorKind, uint, string?)`
  - `CameraErrorClassifier.Classify(uint code)` returning `(CameraErrorKind Kind, int RetryDelayMs, string? GuestMessage)`

- [ ] **Step 1: Create `CameraErrorKind.cs`**

```csharp
namespace Photobooth.Camera;

public enum CameraErrorKind
{
    Retryable,
    GuestFixable,
    Fatal
}
```

- [ ] **Step 2: Create `CaptureResult.cs`**

```csharp
namespace Photobooth.Camera;

public record CaptureResult(
    bool Success,
    string? Path,
    CameraErrorKind Kind,
    uint EdsCode,
    string? GuestMessage)
{
    public static CaptureResult Ok(string path) =>
        new(true, path, CameraErrorKind.Retryable, 0, null);

    public static CaptureResult Failure(CameraErrorKind kind, uint code, string? message = null) =>
        new(false, null, kind, code, message);
}
```

- [ ] **Step 3: Create `CameraErrorClassifier.cs`**

```csharp
namespace Photobooth.Camera;

public static class CameraErrorClassifier
{
    public static (CameraErrorKind Kind, int RetryDelayMs, string? GuestMessage) Classify(uint code)
        => code switch
        {
            EDSDKLib.EDSDK.EDS_ERR_TAKE_PICTURE_AF_NG =>
                (CameraErrorKind.GuestFixable, 0, "Please step back a little and we'll try again."),
            EDSDKLib.EDSDK.EDS_ERR_TAKE_PICTURE_MIRROR_UP_NG =>
                (CameraErrorKind.Retryable, 500, null),
            EDSDKLib.EDSDK.EDS_ERR_TAKE_PICTURE_SENSOR_CLEANING_NG =>
                (CameraErrorKind.Retryable, 2000, null),
            EDSDKLib.EDSDK.EDS_ERR_TAKE_PICTURE_SILENCE_NG =>
                (CameraErrorKind.Retryable, 500, null),
            EDSDKLib.EDSDK.EDS_ERR_DEVICE_BUSY =>
                (CameraErrorKind.Retryable, 500, null),
            _ =>
                (CameraErrorKind.Fatal, 0, null)
        };
}
```

- [ ] **Step 4: Build to verify the three files compile**

```
dotnet build Photobooth/Photobooth.csproj
```

Expected: `Build succeeded.` — the rest of the project still compiles because no existing types were changed yet.

- [ ] **Step 5: Commit**

```
git add Photobooth/Camera/CameraErrorKind.cs Photobooth/Camera/CaptureResult.cs Photobooth/Camera/CameraErrorClassifier.cs
git commit -m "feat: add CameraErrorKind, CaptureResult, CameraErrorClassifier"
```

---

### Task 2: Update `CameraService` to return `CaptureResult`

**Files:**
- Modify: `Photobooth/Camera/CameraService.cs`

**Interfaces:**
- Consumes: `CaptureResult.Ok(string)`, `CaptureResult.Failure(CameraErrorKind, uint, string?)`, `CameraErrorClassifier.Classify(uint)` from Task 1
- Produces: `TakePictureAsync(CancellationToken) → Task<CaptureResult>` (was `Task<string>`)

Four changes, all in `CameraService.cs`:

**Change 1** — field type (line 36):

Old:
```csharp
internal TaskCompletionSource<string>? _pendingDownload;
```

New:
```csharp
internal TaskCompletionSource<CaptureResult>? _pendingDownload;
```

**Change 2** — `TakePictureAsync` signature and internal TCS (lines 201–224):

Replace the entire method:
```csharp
public async Task<CaptureResult> TakePictureAsync(CancellationToken ct = default)
{
    if (_model == null || _processor == null)
        throw new InvalidOperationException("Camera not initialized");

    Directory.CreateDirectory(SessionDirectory);

    _pendingDownload = new TaskCompletionSource<CaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);

    _model.IsCapturing = true;
    Log.Information("Triggering shutter — waiting for download");
    _processor.PostCommand(new TakePictureCommand(ref _model));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
    using (linked.Token.Register(() =>
    {
        if (_model != null) _model.IsCapturing = false;
        _pendingDownload.TrySetCanceled();
    }))
    {
        return await _pendingDownload.Task;
    }
}
```

**Change 3** — `OnPhotoSaved` (lines 297–301):

Replace the method body:
```csharp
internal void OnPhotoSaved(string path)
{
    Log.Information("Photo download complete: {Path}", path);
    if (_model != null) _model.IsCapturing = false;
    _pendingDownload?.TrySetResult(CaptureResult.Ok(path));
}
```

**Change 4** — `Update()` ERROR case and `HandleStateEvent` CaptureError case:

In `Update()`, replace the `case CameraEvent.Type.ERROR:` block (lines 286–292):
```csharp
case CameraEvent.Type.ERROR:
    var code = (uint)e.Arg;
    Log.Error("Camera error 0x{Error:X8}", code);
    var (kind, _, guestMsg) = CameraErrorClassifier.Classify(code);
    _pendingDownload?.TrySetResult(CaptureResult.Failure(kind, code, guestMsg));
    Error?.Invoke(this, $"EDSDK error 0x{code:X8}");
    break;
```

In `HandleStateEvent`, replace the `case EDSDKLib.EDSDK.StateEvent_CaptureError:` block (lines 353–358):
```csharp
case EDSDKLib.EDSDK.StateEvent_CaptureError:
    Log.Warning("Camera capture error 0x{Param:X8}", inParameter);
    if (_model != null) _model.IsCapturing = false;
    var (captureKind, _, captureMsg) = CameraErrorClassifier.Classify(inParameter);
    _pendingDownload?.TrySetResult(CaptureResult.Failure(captureKind, inParameter, captureMsg));
    break;
```

- [ ] **Step 1: Apply Change 1 — field type**

Edit line 36 of `Photobooth/Camera/CameraService.cs`:

```csharp
internal TaskCompletionSource<CaptureResult>? _pendingDownload;
```

- [ ] **Step 2: Apply Change 2 — `TakePictureAsync` signature**

Replace `public async Task<string> TakePictureAsync` … through the closing `}` (lines 201–224) with:

```csharp
public async Task<CaptureResult> TakePictureAsync(CancellationToken ct = default)
{
    if (_model == null || _processor == null)
        throw new InvalidOperationException("Camera not initialized");

    Directory.CreateDirectory(SessionDirectory);

    _pendingDownload = new TaskCompletionSource<CaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);

    _model.IsCapturing = true;
    Log.Information("Triggering shutter — waiting for download");
    _processor.PostCommand(new TakePictureCommand(ref _model));

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
    using (linked.Token.Register(() =>
    {
        if (_model != null) _model.IsCapturing = false;
        _pendingDownload.TrySetCanceled();
    }))
    {
        return await _pendingDownload.Task;
    }
}
```

- [ ] **Step 3: Apply Change 3 — `OnPhotoSaved`**

Replace the body of `OnPhotoSaved` so it reads:
```csharp
internal void OnPhotoSaved(string path)
{
    Log.Information("Photo download complete: {Path}", path);
    if (_model != null) _model.IsCapturing = false;
    _pendingDownload?.TrySetResult(CaptureResult.Ok(path));
}
```

- [ ] **Step 4: Apply Change 4a — ERROR case in `Update()`**

Replace the `case CameraEvent.Type.ERROR:` block:
```csharp
case CameraEvent.Type.ERROR:
    var code = (uint)e.Arg;
    Log.Error("Camera error 0x{Error:X8}", code);
    var (kind, _, guestMsg) = CameraErrorClassifier.Classify(code);
    _pendingDownload?.TrySetResult(CaptureResult.Failure(kind, code, guestMsg));
    Error?.Invoke(this, $"EDSDK error 0x{code:X8}");
    break;
```

- [ ] **Step 5: Apply Change 4b — CaptureError case in `HandleStateEvent()`**

Replace the `case EDSDKLib.EDSDK.StateEvent_CaptureError:` block:
```csharp
case EDSDKLib.EDSDK.StateEvent_CaptureError:
    Log.Warning("Camera capture error 0x{Param:X8}", inParameter);
    if (_model != null) _model.IsCapturing = false;
    var (captureKind, _, captureMsg) = CameraErrorClassifier.Classify(inParameter);
    _pendingDownload?.TrySetResult(CaptureResult.Failure(captureKind, inParameter, captureMsg));
    break;
```

- [ ] **Step 6: Build — expect one error in ShootPage, not in CameraService**

```
dotnet build Photobooth/Photobooth.csproj
```

Expected: The build error will be in `ShootPage.xaml.cs` — it still assigns `await _camera.TakePictureAsync(ct)` to a `string`. That's fine — it will be fixed in Task 3. Verify `CameraService.cs` itself has **0 errors**.

- [ ] **Step 7: Commit**

```
git add Photobooth/Camera/CameraService.cs
git commit -m "feat: TakePictureAsync returns CaptureResult; classify errors at source"
```

---

### Task 3: ShootPage — FatalOverlay XAML + result dispatch in `RunSequenceAsync`

**Files:**
- Modify: `Photobooth/Views/ShootPage.xaml`
- Modify: `Photobooth/Views/ShootPage.xaml.cs`

**Interfaces:**
- Consumes: `Task<CaptureResult> TakePictureAsync(CancellationToken)`, `CaptureResult.Success`, `CaptureResult.Kind`, `CaptureResult.EdsCode`, `CaptureResult.GuestMessage`, `CameraErrorKind.Retryable/GuestFixable/Fatal`, `CameraErrorClassifier.Classify(uint)` (for `RetryDelayMs`)

- [ ] **Step 1: Add `FatalOverlay` to `ShootPage.xaml`**

In `Photobooth/Views/ShootPage.xaml`, insert the following block **after** the closing `</Grid>` of the `DisconnectOverlay` and **before** the final `</Grid>` that closes the page (after line 205):

```xml
<!-- Camera fatal error modal -->
<Grid x:Name="FatalOverlay"
      Grid.RowSpan="3"
      Visibility="Collapsed">
    <Rectangle Fill="#CC000000"/>
    <Border Background="{DynamicResource SurfaceBrush}"
            CornerRadius="12" Width="440" Padding="40"
            VerticalAlignment="Center" HorizontalAlignment="Center">
        <StackPanel>
            <TextBlock Text="Camera Problem"
                       Foreground="{StaticResource TextPrimaryBrush}"
                       FontSize="26" FontWeight="Bold"
                       Margin="0,0,0,12"/>
            <TextBlock x:Name="FatalOverlayText"
                       Foreground="{StaticResource TextSecondaryBrush}"
                       FontSize="15" TextWrapping="Wrap"
                       Margin="0,0,0,28"/>
            <Button Content="← GO BACK"
                    Style="{StaticResource KioskButton}"
                    HorizontalAlignment="Left"
                    Padding="28,12" FontSize="16"
                    Click="Back_Click"/>
        </StackPanel>
    </Border>
</Grid>
```

- [ ] **Step 2: Add `ShowFatalOverlay` method to `ShootPage.xaml.cs`**

Add this method in the `// --- Error / disconnect ---` region, after `OnCameraError`:

```csharp
private void ShowFatalOverlay(string message)
{
    FatalOverlayText.Text = message;
    FatalOverlay.Visibility = Visibility.Visible;
}
```

- [ ] **Step 3: Replace `RunSequenceAsync` in `ShootPage.xaml.cs`**

Replace the entire `RunSequenceAsync` method (lines 176–302) with the following:

```csharp
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

        int  retakeCount   = 0;
        int  silentRetries = 0;
        int  guestRetries  = 0;
        bool retakeRequested;

        do
        {
            retakeRequested = false;
            bool cameraRetry = false;

            StatusText.Text = $"Photo {i} of 3 — get ready!";
            await RunCountdown(_settings.CountdownSeconds, ct);
            if (ct.IsCancellationRequested) return;

            _shooting = true;
            StatusText.Text = "Please wait…";
            CaptureSpinner.Visibility = Visibility.Visible;

            string path = string.Empty;
            CaptureResult capture;
            try
            {
                Log.Information("Taking photo {N}/3 (attempt {Attempt})", i, retakeCount + 1);
                capture = await _camera.TakePictureAsync(ct);
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

            if (!capture.Success)
            {
                _shooting = false;
                CaptureSpinner.Visibility = Visibility.Collapsed;
                var (_, retryDelay, _) = CameraErrorClassifier.Classify(capture.EdsCode);

                switch (capture.Kind)
                {
                    case CameraErrorKind.Retryable when silentRetries < 2:
                        silentRetries++;
                        Log.Warning("Retryable error 0x{Code:X8} — silent retry {N}", capture.EdsCode, silentRetries);
                        if (retryDelay > 0)
                            try { await Task.Delay(retryDelay, ct); }
                            catch (OperationCanceledException) { return; }
                        cameraRetry = true;
                        retakeRequested = true;
                        break;

                    case CameraErrorKind.GuestFixable when guestRetries < 1:
                        guestRetries++;
                        Log.Warning("GuestFixable error 0x{Code:X8}: {Msg}", capture.EdsCode, capture.GuestMessage);
                        StatusText.Text = capture.GuestMessage ?? "Please try again.";
                        try { await Task.Delay(3000, ct); }
                        catch (OperationCanceledException) { return; }
                        cameraRetry = true;
                        retakeRequested = true;
                        break;

                    default:
                        Log.Error("Fatal camera error 0x{Code:X8}", capture.EdsCode);
                        ShowFatalOverlay("Camera error — please contact staff.");
                        return;
                }
            }

            if (!cameraRetry)
            {
                path = capture.Path!;

                var date = DateTime.Today.ToString("yyyyMMdd");
                var sid  = _sessionId.HasValue ? _sessionId.Value.ToString() : "0";
                var stem = Path.GetFileNameWithoutExtension(path);
                var ext  = Path.GetExtension(path);
                var dest = Path.Combine(_sessionDir, $"{stem}_{date}_s{sid}_p{i}{ext}");
                File.Move(path, dest, overwrite: true);
                path = dest;

                _ = Task.Run(() => BitmapHelper.GenerateThumbnail(dest));

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
```

- [ ] **Step 4: Build — expect clean**

```
dotnet build Photobooth/Photobooth.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 5: Smoke test**

Run the app. Go to the shoot page. Verify:
- Normal flow (no errors) works end-to-end — all 3 photos captured and sequence completes.
- FatalOverlay renders correctly if you trigger a fatal error (e.g. temporarily disconnect camera mid-shoot).
- Pressing "← GO BACK" on the FatalOverlay returns to the greeting page.

- [ ] **Step 6: Commit**

```
git add Photobooth/Views/ShootPage.xaml Photobooth/Views/ShootPage.xaml.cs
git commit -m "feat: dispatch capture result kinds in ShootPage; add FatalOverlay"
```

---

### Task 4: EVF stall tuning — 1.5 s threshold + friendly status message

**Files:**
- Modify: `Photobooth/Camera/EvfPump.cs` (line 63)
- Modify: `Photobooth/Views/ShootPage.xaml.cs` (two string literals in `OnLoaded`)

**Interfaces:**
- No new types — purely configuration changes.

- [ ] **Step 1: Reduce stall threshold in `EvfPump.cs`**

In `Photobooth/Camera/EvfPump.cs`, line 63, change:

```csharp
if (_onStall != null && (DateTime.UtcNow - _lastFrameTime).TotalSeconds > 5)
```

to:

```csharp
if (_onStall != null && (DateTime.UtcNow - _lastFrameTime).TotalSeconds > 1.5)
```

- [ ] **Step 2: Update stall message in `ShootPage.xaml.cs`**

In `OnLoaded`, the `EvfPump` constructor has two string literals that need to change.

Change the frame-received clear check (inside the `renderFrame` lambda):

```csharp
// Old
if (StatusText.Text == "Camera preview unavailable.")
    StatusText.Text = string.Empty;

// New
if (StatusText.Text == "Camera adjusting…")
    StatusText.Text = string.Empty;
```

Change the stall message (inside the `onStall` lambda):

```csharp
// Old
StatusText.Text = "Camera preview unavailable.";

// New
StatusText.Text = "Camera adjusting…";
```

The full `EvfPump` constructor call in `OnLoaded` after both edits:

```csharp
_evfPump = new EvfPump(
    _camera,
    Dispatcher,
    frame =>
    {
        EvfImage.Source = frame;
        if (StatusText.Text == "Camera adjusting…")
            StatusText.Text = string.Empty;
    },
    onStall: () =>
    {
        if (!_shooting)
        {
            Log.Warning("EVF stall — restarting live view");
            StatusText.Text = "Camera adjusting…";
            _evfPump?.RestartLiveView();
        }
    },
    pauseGuard: () => _shooting,
    watchdogMs: 50);
```

- [ ] **Step 3: Build**

```
dotnet build Photobooth/Photobooth.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Manual stall test**

Run the app, go to shoot page. Hold something within ~5 cm of the lens. Verify:
- Status bar shows "Camera adjusting…" within ~2 s (not 5 s as before).
- Live view recovers automatically when the object is moved away.
- Message clears as soon as the next frame arrives.

- [ ] **Step 5: Commit**

```
git add Photobooth/Camera/EvfPump.cs Photobooth/Views/ShootPage.xaml.cs
git commit -m "fix: tighten EVF stall recovery to 1.5 s; friendlier stall message"
```

---

## Self-Review

**Spec coverage:**
- ✅ `CameraErrorKind` enum — Task 1
- ✅ `CameraErrorClassifier.Classify` — Task 1
- ✅ `CaptureResult` record + factory methods — Task 1
- ✅ `TakePictureAsync` returns `Task<CaptureResult>` — Task 2
- ✅ ERROR path sets result not exception — Task 2 (Changes 4a/4b)
- ✅ `StateEvent_CaptureError` path sets result not exception — Task 2 (Change 4b)
- ✅ `ShootPage` Retryable: silent retry, max 2, per-slot counter — Task 3
- ✅ `ShootPage` GuestFixable: guest message 3 s, max 1, repeat countdown — Task 3
- ✅ `ShootPage` Fatal: `ShowFatalOverlay`, sequence halts — Task 3
- ✅ `FatalOverlay` XAML (matches `DisconnectOverlay` style) — Task 3
- ✅ EVF stall threshold 5 000 → 1 500 ms — Task 4
- ✅ Stall message "Camera adjusting…" — Task 4
- ✅ Card errors omitted throughout — confirmed, none present

**Placeholder scan:** None found.

**Type consistency:**
- `CaptureResult.Ok` / `CaptureResult.Failure` — defined Task 1, used Task 2. ✅
- `CameraErrorClassifier.Classify` returns `(Kind, RetryDelayMs, GuestMessage?)` — defined Task 1, destructured in Task 2 and Task 3. ✅
- `CameraErrorKind.Retryable/GuestFixable/Fatal` — defined Task 1, used in Task 3 switch. ✅
- `FatalOverlayText` / `FatalOverlay` — named in XAML (Task 3 Step 1), used in `ShowFatalOverlay` (Task 3 Step 2). ✅
- `silentRetries`, `guestRetries` — declared Task 3 Step 3, used in same method. ✅
