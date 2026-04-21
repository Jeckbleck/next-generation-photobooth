# Instructions
New Repository Setup Instructions
---

## Context Handoff Prompt

```
We are starting a new C# WPF photobooth application called The Next Generation Photobooth, for the time being.

Context from prior work:
- We previously worked in the Canon EDSDK C# sample at D:\EDSDKv132010W\Windows\sample\CSharp\CameraControl\
- The EDSDK DLL issue was resolved: copy 32-bit DLLs from EDSDK\Dll\ (EDSDK.dll + EdsImage.dll) to bin\Debug\
- Architecture: C# WPF kiosk app + Python FastAPI AI server (separate repo)
- Target: photobooth kiosk, shoot 2–3 photos per session, compose photostrip, print, optional AI enhancement

Please help me:
1. Set up a new WPF (.NET 6 or later) solution with the project structure matching our C4 Level 3 component design
2. Port the Canon EDSDK camera module from the old sample into the new solution as a clean wrapper class
3. Set up WebView2 for the live preview host
4. Set up SQLite with EF Core for local data storage
5. Set up the folder structure for events/sessions in local storage

The C4 architecture we designed has these top-level components:
FlowController, SessionManager, CameraModule, EVFModule, LiveViewHost (WebView2),
EventManager, PhotostripComposer, PaymentService, PrintService, AIEnhancementClient,
SyncService (post-MVP stub), SettingsManager, LocalDB, FileStorageService.
```

---

## MVP Scope (what to build first)

- [ ] WPF app shell + navigation
- [ ] EDSDK CameraModule wrapper (ported from old sample)
- [ ] EVFModule — live view frame pump
- [ ] WebView2 LiveViewHost + MediaPipe JS adapter
- [ ] SessionManager — manage 2–3 photo batch
- [ ] FileStorageService — `/events/{id}/{session_id}/raw|enhanced|strips`
- [ ] PhotostripComposer — layout engine with template support
- [ ] AIEnhancementClient — HTTP POST to FastAPI
- [ ] PrintService — Windows print spooler
- [ ] FlowController — full state machine (Idle → Payment → Shoot → Preview → Print)
- [ ] SettingsManager + admin PIN
- [ ] EventManager + SQLite schema
- [ ] Basic settings UI

## Post-MVP

- [ ] Nayax PaymentService
- [ ] SyncService (Synology NAS upload)
- [ ] Full photostrip designer UI
- [ ] Multi-language support

---

## Key Technical Decisions

| Decision | Choice | Reason |
|---|---|---|
| UI framework | WPF (.NET 8) | Best for kiosk, XAML flexibility, WebView2 support |
| Camera control | Canon EDSDK (C#) | Official SDK, reliable on Windows |
| Live preview effects | JS/MediaPipe via WebView2 | Reuse existing JS work; EVF frames injected via PostMessage |
| AI processing | Python FastAPI (LAN) | Best ML ecosystem; decoupled from kiosk |
| Local DB | SQLite + EF Core | Lightweight, embedded, no server needed |
| Payment | Nayax terminal | Client requirement |
| Cloud backup | Synology NAS (SMB/SFTP) | Optional, background, post-MVP |

---

## EDSDK DLL Notes

- SDK location: `D:\EDSDKv132010W\Windows\`
- 32-bit: `EDSDK\Dll\EDSDK.dll` + `EdsImage.dll`
- 64-bit: `EDSDK_64\Dll\EDSDK.dll` + `EdsImage.dll`
- Build target: AnyCPU → copy 32-bit DLLs to `bin\Debug\` alongside the exe
- Old sample solution: `D:\EDSDKv132010W\Windows\sample\CSharp\CameraControl\CameraControl.sln`

