# Fn Mapping Tool

Fn Mapping Tool is a Windows utility for remapping OEM / vendor-specific keys.

It targets laptops that expose special keys through WMI or other vendor event channels. The app lets you capture those events, map them to useful actions, and optionally show a clean OEM-style OSD.

## What it does

- capture vendor key events
- define named keys from those events
- map keys to actions such as:
  - Windows Settings
  - projection switcher
  - volume / media controls
  - brightness controls
  - launch application
  - custom OSD
- run the remapping service in the background
- optionally show tray icon and OSD

## App structure

The project has two runtime parts:

- **Controller**
  - WinUI 3 desktop app
  - edits configuration
  - starts / stops / controls the background service
- **Worker**
  - background process
  - listens for OEM input events
  - executes mapped actions
  - owns tray and OSD behavior

For end users, the visible entry point is the **Controller**.
The Worker is packaged as internal runtime payload.

## Project layout

- `src/FnMappingTool.Controller/` — WinUI 3 controller app
- `src/FnMappingTool.Worker/` — background worker
- `src/FnMappingTool.Core/` — shared models, services, IPC
- `src/FnMappingTool.Setup/` — WiX MSI installer project
- `assets/` — packaged icons and default config templates
- `build/` — intermediate outputs and package staging
- `artifacts/` — final distributables

## Configuration

Runtime config file:

- `%LocalAppData%\FnMappingTool\config.json`

Packaged config templates:

- `assets/config/`

OSD and application assets:

- `assets/osd/`
- `assets/app/app.ico`

## Build and package

Run:

```powershell
.\build.ps1
```

Optional arguments:

```powershell
.\build.ps1 -Version 0.1.0
.\build.ps1 -SkipZip
.\build.ps1 -SkipMsi
```

## Build outputs

Main staged outputs:

- `build/bin/`
- `build/package/FnMappingTool/`

Final outputs:

- `artifacts/FnMappingTool-portable-v<version>.zip`
- `artifacts/FnMappingTool-setup-v<version>.msi`

The package staging folder contains:

- `FnMappingTool.Controller.exe` — user-facing launcher
- `runtime/worker/FnMappingTool.Worker.exe` — internal worker payload

`build.ps1` also cleans transient project `obj/` folders after packaging so the repo stays tidy without needing a shared `Directory.Build.props`.

## Installer behavior

The MSI installer currently provides:

- install wizard UI
- install directory selection
- start menu shortcut for the app
- start menu shortcut for uninstall

## Notes

- Worker is intentionally **not** shipped as a separate user-facing app entry
- The current packaging flow uses verified **Release build outputs**, not WinUI `Publish`, because `Publish` was previously unstable on this machine
- The project currently favors a **single clean config schema** over backward-compat migration layers

## Development guidance

If you are changing runtime architecture, packaging, or config schema, also update:

- `AGENTS.md`
- `build.ps1`
- this README
