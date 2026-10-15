# OscarWatch developer documentation

Guides for extending hardware support in the desktop app.

| Document | Description |
|----------|-------------|
| [Building rotator drivers](building-rotator-drivers.md) | Implement `IRotatorDriver` for GS-232, EasyComm, or new controllers |
| [Building radio drivers](building-radio-drivers.md) | Implement `IRigDriver` for CI-V radios or other CAT protocols |
| [Doppler CAT lead tuning](doppler-cat-lead.md) | Phase-gated lead constants, field symptoms, what to tweak if still ahead/behind |
| [Testing the Terminal built-in modem](terminal-builtin-modem-testing.md) | Unit tests, WA8LMF and Direwolf interop corpora, and manual hardware smoke tests |

All drivers live in the **OscarWatch** app project (`OscarWatch/`). Shared models and orbit logic are in **OscarWatch.Core**.

## Solution layout (hardware-related)

```
OscarWatch/
  Rig/           IRigDriver, RigController, IcomCivDriverBase, factories
  Rotator/       IRotatorDriver, RotatorController, Gs232Rotator, factories
OscarWatch.Core/
  Models/        RigSettings, RotatorSettings, RigType, RotatorType
  Services/      IRigController, IRotatorController
  Radio/         IcomCivCodec, doppler helpers (rig logic, not serial I/O)
OscarWatch.Tests/
  RecordingRigDriver.cs, RecordingRotatorDriver.cs
```

Controllers run serial I/O on **background threads**; the UI only enqueues updates. New drivers should be thread-safe (see existing `SemaphoreSlim` usage on rotators).
