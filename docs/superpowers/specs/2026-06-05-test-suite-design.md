# Test Suite Design — Unit + Mocked Integration

**Date:** 2026-06-05
**Scope:** Initial test suite covering unit and mocked-integration tests, plus CI pipeline integration. Visual regression, DB integration, HTTP contract, and hardware smoke tests are deferred to the backlog.

---

## 1. Project Structure

One new project added to the solution alongside the existing `Photobooth/` app:

```
NextGenerationPhotobooth.sln
├── Photobooth/
└── Photobooth.Tests/
    ├── Photobooth.Tests.csproj
    ├── Unit/
    │   ├── FlowControllerTests.cs
    │   ├── EventServiceTests.cs
    │   ├── PhotostripComposerTests.cs
    │   └── SettingsManagerTests.cs
    ├── Integration/
    │   ├── CameraServiceTests.cs
    │   ├── PrintServiceTests.cs
    │   └── AIEnhancementClientTests.cs
    └── Fixtures/
        ├── sample1.jpg
        ├── sample2.jpg
        └── sample3.jpg
```

**Framework:** xUnit + Moq
**Target:** `x86` (matches main app — required for EDSDK compatibility)
**Fixtures:** Small committed JPEGs used by PhotostripComposer tests. No dependency on real session photos.

---

## 2. Coverage Scope

### Unit Tests

**FlowControllerTests**
- All 7 valid state transitions fire `StateChanged` with correct new state
- Invalid triggers are silently ignored (no exception, no state change)
- Context properties (`SessionPhotos`, `SessionId`, `AIFlowActive`, `AIStyleId`, `AIStyleName`) are set correctly per transition
- Chained transitions work in sequence (e.g. Idle → StylePick → Shooting → Preview)

**EventServiceTests**
- Slug generation: correct format `"{name}-{year}"`, year suffix, special-character sanitisation
- Print limit — event level: at-limit passes, over-limit throws
- Print limit — session level: at-limit passes, over-limit throws
- Session lifecycle: create → record photos → finalize / abandon

**PhotostripComposerTests**
- `LetterboxRect` preserves aspect ratio for landscape and portrait inputs
- `FillRect` crops correctly (output fills target dimensions)
- `RotateBitmap` produces correct output dimensions for 90°, 180°, 270°
- `Compose()` output bitmap is exactly 1240×1844 px with three valid photos
- `Compose()` with a missing photo path → 1px placeholder inserted, no exception thrown

**SettingsManagerTests**
- Save → load roundtrip: all values survive serialisation
- Fresh (missing) file returns correct defaults
- PIN hashing uses SHA-256 and is deterministic
- Corrupt JSON file falls back to defaults without throwing

### Integration Tests (mocked hardware — no physical devices required)

**CameraServiceTests**
- `HandlePropertyEvent` callback queues the correct SDK property query
- `HandleObjectEventHandler` (photo ready) causes `TakePictureAsync` to complete with a file path
- `HandleStateEvent` (camera disconnect) cancels a pending `TakePictureAsync` and raises the disconnect event
- `TakePictureAsync` respects a `CancellationToken`
- `TakePictureAsync` 30 s timeout fires if no download event arrives

**PrintServiceTests**
- DNP printer name pattern (`DS620`, `RX1`) is detected correctly from a mock printer list
- Paper size resolves to 4×6 portrait
- Print job is submitted to the selected printer name via `IPrintAdapter`

**AIEnhancementClientTests**
- `GET /styles` response deserialises to the correct style list
- `POST /augment` sends the correct multipart body: base64 image field, style ID, API key header
- Non-2xx response throws a meaningful exception with the status code

---

## 3. Mocking Strategy

| Dependency | Strategy |
|---|---|
| **EDSDK (CameraService)** | Native DLL never loaded — `Initialize()` not called in tests. Event handler methods marked `internal`, exposed via `[assembly: InternalsVisibleTo("Photobooth.Tests")]`. Tests invoke them directly to simulate SDK callbacks. |
| **HttpClient (AIEnhancementClient)** | Custom `HttpMessageHandler` stub injected at construction. Returns pre-canned responses; lets tests assert on the outgoing request shape. No extra packages needed. |
| **PrintDocument / printer list (PrintService)** | Thin `IPrintAdapter` interface wraps printer enumeration and job submission. Tests use `Mock<IPrintAdapter>`. |
| **IEventRepository (EventService)** | `Mock<IEventRepository>` via Moq. No changes needed — EventService already depends on the interface. |
| **File system (SettingsManager)** | Tests pass a `Path.GetTempFileName()` path into the constructor. Each test cleans up in `Dispose`. |
| **Navigation (FlowController)** | `INavigator` interface (`void NavigateTo(Page p)`) injected into FlowController. Tests use `Mock<INavigator>`. |

### Code changes required in the main project

1. **Add `INavigator` interface** — extracted from the direct `MainWindow.NavigateTo()` call in `FlowController`. `MainWindow` implements it; tests inject a mock.
2. **Add `IPrintAdapter` interface** — wraps `PrinterSettings` enumeration and `PrintDocument` submission in `PrintService`.
3. **Add `[assembly: InternalsVisibleTo("Photobooth.Tests")]`** to `Properties/AssemblyInfo.cs` (create the file if it doesn't exist) — exposes `CameraService` internal event handler methods to the test project.

---

## 4. CI Pipeline

`dotnet test` is inserted between the existing build and publish steps. The `--no-build` flag reuses binaries from the build step. A test failure exits the job before publish runs — a broken build never produces a release artifact.

```yaml
- name: Restore
  run: dotnet restore -r win-x86

- name: Build
  run: dotnet build -c Release --no-restore

- name: Test
  run: dotnet test -c Release --no-build --no-restore --verbosity normal

- name: Publish
  run: dotnet publish -c Release -r win-x86 --self-contained --no-build -o ./publish
```

Runner: `windows-latest` (existing, unchanged). No matrix or separate jobs needed.

---

## 5. Deferred to Backlog

The following test types are logged in `Dev/projects/Photobooth/Backlog.md` and are out of scope for this implementation:

- Visual regression tests (PhotostripComposer reference image snapshots)
- Database integration tests (real SQLite file, EventRepository queries)
- HTTP contract tests (AIEnhancementClient vs FastAPI schema)
- Smoke test category (`[Trait("Category","Smoke")]`)
- Hardware smoke tests (kiosk-local only, not CI-able)
