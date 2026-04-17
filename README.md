# The Next Generation Photobooth

A Windows kiosk application for a photobooth system, built with WPF (.NET 10) and the Canon EDSDK.

---

## Requirements

- Windows 10/11 (x86 process — required by the Canon EDSDK 32-bit DLLs)
- .NET 10 SDK
- Visual Studio 2022 or later (with the **.NET desktop development** workload)
- A supported Canon EOS camera connected via USB
- Canon EDSDK v13.x

---

## Setup

### 1. Clone the repository

```bash
git clone <repo-url>
cd next-generation-photobooth
```

### 2. Copy the EDSDK DLLs

Copy the **32-bit** DLLs from your Canon EDSDK installation into `Photobooth\`:

```
D:\EDSDKv132010W\Windows\EDSDK\Dll\EDSDK.dll     → Photobooth\EDSDK.dll
D:\EDSDKv132010W\Windows\EDSDK\Dll\EdsImage.dll  → Photobooth\EdsImage.dll
```

> These DLLs are not included in the repository (Canon proprietary).

### 3. Open and build

Open `NextGenerationPhotobooth.sln` in Visual Studio, then **Build → Build Solution**.

---

## Project Structure

```
Photobooth/
├── App.xaml / App.xaml.cs          # Application entry point, CameraService lifetime
├── AppLogger.cs                    # Serilog bootstrap
├── MainWindow.xaml                 # Shell window (fullscreen, no chrome)
├── Views/
│   ├── GreetingPage                # Page 1 — start screen + staff settings
│   ├── ShootPage                   # Page 2 — live preview + 3-photo sequence
│   └── ResultsPage                 # Page 3 — results display + auto-return
└── Camera/
    ├── CameraService.cs            # High-level Canon camera API
    ├── CameraModel.cs              # Camera state
    ├── CommandProcessor.cs         # Async command queue
    ├── Observer.cs                 # Observer pattern
    ├── CameraEvent.cs              # Camera event types
    ├── EDSDKLib/EDSDK.cs           # Canon P/Invoke definitions (copied from SDK)
    └── Commands/                   # Individual EDSDK command implementations
```

---

## Pages

| Page | Description |
|---|---|
| **Greeting** | Welcome screen with a START SESSION button. Gear icon (top-right) opens a PIN-protected staff settings panel. |
| **Shoot** | Live EVF preview from the camera. Takes 3 photos with a 3-second countdown between each. Flash animation on capture. |
| **Results** | Displays the 3 captured photos side by side. Auto-returns to the greeting screen after 5 seconds, or immediately via the START AGAIN button. |

---

## Configuration

| Setting | Location | Default |
|---|---|---|
| Staff PIN | `Views/GreetingPage.xaml.cs` line 9 | `1234` |
| Photos per session | `Views/ShootPage.xaml.cs` | `3` |
| Results auto-return timeout | `Views/ResultsPage.xaml.cs` | `5 seconds` |
| Countdown duration | `Views/ShootPage.xaml.cs` | `3 seconds` |

---

## Logs

Runtime logs are written to:

```
%LocalAppData%\Photobooth\Logs\photobooth-YYYYMMDD.log
```

14-day rolling retention. Log output also appears in the Visual Studio debug output window during development.

---

## Architecture

The camera layer follows the Canon EDSDK sample MVC pattern:

- **`CameraService`** — initialises the SDK, owns the session, exposes `TakePictureAsync()` and EVF frame events
- **`CommandProcessor`** — single background thread processing a `ConcurrentQueue` of commands
- **`CameraModel`** — holds camera state and notifies observers on changes
- Pages subscribe to `CameraService` events and are fully decoupled from EDSDK internals

---

## Post-MVP Roadmap

- [ ] Photostrip composer (layout engine with template support)
- [ ] AI enhancement client (HTTP POST to Python FastAPI server)
- [ ] Print service (Windows print spooler)
- [ ] Nayax payment terminal integration
- [ ] Event manager + SQLite local database
- [ ] Synology NAS sync service
- [ ] Full settings UI
- [ ] Multi-language support
