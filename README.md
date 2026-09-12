# 🎮 Wolverine V3 Pro 8K — Zero-Lag Remapper Suite

A Windows (.NET / WPF) input remapper for the **Razer Wolverine V3 Pro 8K**. It
intercepts the keys Synapse sends from the M1–M6 buttons at the low-level
keyboard-hook layer, **swallows them before Windows or the game can see them**,
and drives a **virtual Xbox 360 controller** (via ViGEmBus) instead — so games
like *Throne and Liberty* stay locked in controller UI with zero stutter.

## Features

- **Per-button configuration** with three independent dimensions:
  - **Trigger** — Hold / Toggle (latch on-off) / Double-tap
  - **Repeat** — Once / N times / While-held (turbo)
  - **Action** — Chord (buttons at once), Hold + Press, or a full Macro sequence
- **Macro editor** — ordered Press / Release / Tap / Wait / Push-stick / Center-stick
  steps, with joystick positions recorded live from the controller (great for
  radial "ring menu" gestures).
- **Passthrough mode** — mirrors the physical pad (buttons, sticks, triggers)
  into the virtual pad, so games that only read one controller get everything.
- **Live controller diagram**, **analog trigger bars**, and a
  **stick / deadzone analyzer** with adjustable inner/outer deadzone.
- **Named profiles** per game, stored in `%AppData%\WolverineRemapper\`.

## Requirements

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download)
- [ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases)
- Razer Synapse mapping M1–M6 to spare keys (Synapse only exposes F1–F12, so the
  defaults are ScrollLock / Pause / Insert / Home / PageUp / PageDown)

## Build & run

```bash
dotnet run --project src/WolverineRemapper/WolverineRemapper.csproj
```

To produce a single-file executable in `dist/`:

```bash
dotnet publish src/WolverineRemapper/WolverineRemapper.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

## Quick start

1. In Synapse, map M1→ScrollLock, M2→Pause, M3→Insert, M4→Home, M5→PageUp, M6→PageDown.
2. Launch the app, press **START ENGINE**.
3. Configure each M-button on the **Visual Remapper** tab.
4. Launch your game and play.

## Profiles / save data

Your profiles live in `%AppData%\WolverineRemapper\`. A backup snapshot is kept
in [`profiles-backup/`](profiles-backup/) — see its README to restore on another PC.

## Project layout

- `src/WolverineRemapper/` — the WPF app
  - `Services/` — keyboard hook, virtual controller, XInput, remapper engine, profiles
  - `Models/` — button config, macro/trigger/repeat models
  - `ViewModels/` — `MainViewModel`
  - `Assets/` — app icon
- `tools/WolverineDiagnostic/` — standalone console prototype
- `profiles-backup/` — mirrored save data
