# Camera Error Failsafes — Design Spec
_Date: 2026-06-26_

## Problem

Two failure modes currently crash or silently stall the photo sequence:

1. **EVF crash when subject too close** — `StateEvent_InternalError` is logged but the live view can
   terminate. The stall watchdog recovers after 5 s (too slow, message sounds like a hard error).
2. **`EDS_ERR_TAKE_PICTURE_AF_NG` (0x8D01)** — AF failure during capture propagates as an
   `InvalidOperationException` that terminates the entire sequence. The raw hex code is shown to
   the guest, which is meaningless.

Root cause is the same in both cases: the camera cannot focus (subject too close, too far, or
poor contrast). The EDSDK reports this at two different points in the flow.

---

## Error Classification

A static `CameraErrorClassifier` maps EDSDK `TAKE_PICTURE` error codes to three categories.
Card errors are omitted — the camera in this installation does not use a memory card.

| Code | EDSDK name | Kind | Behaviour |
|---|---|---|---|
| `0x8D01` | `AF_NG` | **GuestFixable** | Repeat countdown for slot; show guest message |
| `0x8D03` | `MIRROR_UP_NG` | **Retryable** | Wait 500 ms, silent retry (max 2) |
| `0x8D04` | `SENSOR_CLEANING_NG` | **Retryable** | Wait 2 000 ms, silent retry (max 2) |
| `0x8D05` | `SILENCE_NG` | **Retryable** | Wait 500 ms, silent retry (max 2) |
| `0x0081` | `DEVICE_BUSY` | **Retryable** | Already retried by `CommandProcessor` |
| anything else | — | **Fatal** | Halt sequence; show operator alert |

**Guest messages (GuestFixable):**
- `AF_NG` → "Please step back a little and we'll try again."

**Operator messages (Fatal):**
- All others → "Camera error — please contact staff." (with the hex code in the log only)

---

## Architecture

### `CameraErrorKind` enum  (`Camera/CameraErrorKind.cs`)

```
Retryable   — transparent auto-retry, no guest UI
GuestFixable — guest shown a message, countdown repeats
Fatal        — sequence halts, operator overlay shown
```

### `CameraErrorClassifier` static class  (`Camera/CameraErrorClassifier.cs`)

```
Classify(uint edsCode) → (Kind, RetryDelayMs, GuestMessage?)
```

Single method, no dependencies. Fully static — easy to test, easy to extend.

### `CaptureResult` record  (`Camera/CaptureResult.cs`)

`TakePictureAsync` returns a `CaptureResult` instead of throwing on known EDSDK errors:

```
record CaptureResult(
    bool Success,
    string? Path,
    CameraErrorKind Kind,
    uint EdsCode,
    string? GuestMessage
)
```

Unexpected exceptions (e.g. timeout, `OperationCanceledException`) still propagate normally —
only classified EDSDK errors become results.

### `CameraService.TakePictureAsync` change

The `ERROR` event path currently calls `_pendingDownload.TrySetException(...)`.
After this change it calls `_pendingDownload.TrySetResult(CaptureResult.Failure(...))`.
`TakePictureAsync` returns `Task<CaptureResult>` (was `Task<string>`).

### `ShootPage.RunSequenceAsync` change

Replace the current `try/catch(InvalidOperationException)` with a result dispatch:

```
Retryable   → wait RetryDelayMs, repeat shot (same slot, same countdown), max 2 silent retries
GuestFixable → show GuestMessage in StatusText for 3 s, then repeat the full countdown for
               the slot (counted as a retake so MaxRetakesPerSlot still applies); max 1
GuestFixable retry per slot before treating as Fatal
Fatal        → show FatalOverlay (same style as DisconnectOverlay), halt sequence
```

---

## EVF Stall Recovery

Reduce stall trigger from **5 000 ms → 1 500 ms** in the `EvfPump` watchdog.

When stall fires (and not currently shooting):
- Call `RestartLiveView()` (already implemented)
- Set `StatusText` to **"Camera adjusting…"** (replaces "Camera preview unavailable." which
  sounds like a permanent failure to guests)

The message clears automatically when the next EVF frame arrives (existing behaviour).

---

## What is NOT changing

- `CommandProcessor` retry loop for `DEVICE_BUSY` — already correct, untouched.
- `StateEvent_InternalError` / `StateEvent_CaptureError` handling added earlier — kept as-is.
- `EvfPump.RestartLiveView()` — already implemented, just wired more aggressively.
- The `DisconnectOverlay` for `StateEvent_Shutdown` — separate concern, untouched.

---

## Files Touched

| File | Change |
|---|---|
| `Camera/CameraErrorKind.cs` | New — enum |
| `Camera/CameraErrorClassifier.cs` | New — static classifier |
| `Camera/CaptureResult.cs` | New — result record |
| `Camera/CameraService.cs` | `TakePictureAsync` returns `Task<CaptureResult>`; ERROR path sets result not exception |
| `Camera/EvfPump.cs` | Stall threshold 5 000 → 1 500 ms |
| `Views/ShootPage.xaml` | Add `FatalOverlay` (operator error panel) |
| `Views/ShootPage.xaml.cs` | Replace capture `try/catch` with result dispatch; update stall message |
