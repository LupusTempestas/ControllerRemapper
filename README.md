# 🎮 Wolverine — Zero-Lag Remapper Suite

A Windows (.NET / WPF) input remapper for the **Razer Wolverine V3 Pro 8K** and
the **Wolverine V2 family**. It catches each M-button press before the game can
see it and drives a **virtual Xbox 360 controller** (via ViGEmBus) instead — so
games like *Throne and Liberty* stay locked in controller UI with zero stutter.

Pick your controller in the dropdown at the top left:

- **Wolverine V3 Pro 8K (PC)** — Synapse maps M1–M6 to keyboard keys. The app
  intercepts those keys at the low-level keyboard-hook layer and **swallows them
  before Windows or the game can see them**.
- **Wolverine V2 / V2 Chroma / V2 Pro** — Razer's software can only map the
  paddles to normal pad buttons, never to keys. So each M-button **sacrifices**
  one real button (View, Menu, stick clicks, …): map the paddle to that button in
  Razer Controller Setup for Xbox, pick the same button in the app, and the app
  watches the physical pad for it, strips it from passthrough and fires your
  combo instead. See *Wolverine V2 setup* below.

## Download

Two flavours on the [latest release](https://github.com/LupusTempestas/ControllerRemapper/releases/latest):

- **`WolverineRemapper-Setup-x.y.z.exe`** — the installer. Offers a desktop
  shortcut, *Start with Windows*, and installs the **ViGEmBus** driver for you
  (plus **HidHide** if you tick it, for Wolverine V2). Adds an uninstaller.
- **`WolverineRemapper-x.y.z-portable.exe`** — a single self-contained file, no
  install. You must install [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases)
  yourself once.

Neither needs a .NET runtime. The **Settings** tab inside the app has the same
switches (start with Windows, start the engine on launch, close to tray, update
check) plus a driver status check.

> **Windows SmartScreen** will show "Windows protected your PC" the first time,
> because the exe is not code-signed. Click *More info → Run anyway*. Some
> antivirus tools also flag it, since it installs a keyboard hook and creates a
> virtual controller — that is exactly what a remapper has to do.

## Features

- **Per-button configuration** with three independent dimensions:
  - **Trigger** — Hold / Toggle (latch on-off) / Double-tap
  - **Repeat** — Once / Multiple times / While-held (turbo)
  - **Action** — Chord (buttons at once), Hold + Press, or a full Macro sequence
- **Macro editor** — ordered Press / Release / Tap / Wait / Push-stick / Center-stick
  steps, with joystick positions recorded live from the controller (great for
  radial "ring menu" gestures).
- **Passthrough mode** — mirrors the physical pad (buttons, sticks, triggers)
  into the virtual pad, so games that only read one controller get everything.
- **Live controller diagram**, **analog trigger bars**, and a
  **stick / deadzone analyzer** with adjustable inner/outer deadzone.
- **Named profiles** per game, stored in `%AppData%\WolverineRemapper\`.
- **Runs in the tray** — closing the window keeps the engine running. The tray
  icon's menu opens the window, starts/stops the engine, switches profiles,
  restarts or quits the app.
- **Five languages** — English, French, German, Spanish, Dutch. Auto-detected
  from Windows, switchable live from the 🌐 dropdown in the header. Catalogues
  are plain JSON in `src/WolverineRemapper/Localization/`, so adding a language
  is one file.

## Requirements

- Windows 10/11 (x64)
- [ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases) — creates the virtual Xbox pad
- **V3 Pro 8K:** Razer Synapse mapping M1–M6 to spare keys (Synapse only exposes
  F1–F12, so the defaults are ScrollLock / Pause / Insert / Home / PageUp / PageDown)
- **V2 family:** Razer Controller Setup for Xbox, plus
  [HidHide](https://github.com/nefarius/HidHide/releases) — see *Wolverine V2 setup*
- The release exe is self-contained. Building from source needs the
  [.NET 10 SDK](https://dotnet.microsoft.com/download).

Games must not run as administrator unless the remapper does too, or the
keyboard hook cannot intercept the trigger keys.

## Build & run

```bash
dotnet run --project src/WolverineRemapper/WolverineRemapper.csproj
```

To produce the self-contained single-file executable in `dist/` (what the
releases ship):

```bash
dotnet publish src/WolverineRemapper/WolverineRemapper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Launch with `--autostart` to start the engine immediately, and `--minimized`
to stay in the tray (this is what the *Start with Windows* entry uses).

### Installer and releases

`installer/WolverineRemapper.iss` is the Inno Setup script; `installer/build.ps1`
publishes, downloads the driver installers and compiles it locally (needs
`winget install JRSoftware.InnoSetup`). Pushing a tag like `v1.2.0` runs
[`.github/workflows/release.yml`](.github/workflows/release.yml), which builds
both the installer and the portable exe on GitHub and attaches them to a release.

## Quick start (V3 Pro 8K)

1. In Synapse, map M1→ScrollLock, M2→Pause, M3→Insert, M4→Home, M5→PageUp, M6→PageDown.
2. Launch the app, press **START ENGINE**.
3. Configure each M-button on the **Visual Remapper** tab.
4. Launch your game and play.

## Wolverine V2 setup

The V2 family cannot send keyboard keys, so the trigger is a real pad button
that the paddle mirrors. That button is no longer usable in-game.

1. Select **WOLVERINE V2 / V2 CHROMA / V2 PRO** in the top-left dropdown. Each
   M-button gets a default sacrifice (View, Menu, LS click, RS click, D-Left,
   D-Right); change it with the **SACRIFICED BUTTON** picker in the config header.
2. In **Razer Controller Setup for Xbox**, map each paddle to the button you chose.
3. Turn **Passthrough** on. The app mirrors the physical pad into the virtual pad
   with the sacrificed buttons removed, so the game only ever sees the combo.
4. Install [HidHide](https://github.com/nefarius/HidHide/releases), hide the
   physical Wolverine, and whitelist `WolverineRemapper.exe`. Without it the game
   still receives the raw paddle press from the physical pad.
5. Press **START ENGINE** and play. The virtual pad is the only controller the
   game sees, carrying your sticks, buttons and combos.

## Profiles / save data

Your profiles live in `%AppData%\WolverineRemapper\Profiles\` as plain JSON.
Use **DUPLICATE** to branch a profile, and copy the files to move them to
another PC. An example *Throne and Liberty* profile is in
[`profiles-backup/`](profiles-backup/).

## Project layout

- `src/WolverineRemapper/` — the WPF app
  - `Services/` — keyboard hook, virtual controller, XInput, remapper engine, profiles
  - `Models/` — button config, macro/trigger/repeat models
  - `ViewModels/` — `MainViewModel`
  - `Assets/` — app icon
- `tools/WolverineDiagnostic/` — standalone console prototype
- `profiles-backup/` — example profile

## License

[MIT](LICENSE). Not affiliated with Razer. Xbox is a trademark of Microsoft.
