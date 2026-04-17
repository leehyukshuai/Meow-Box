# AGENTS

## What this repo is

Windows OEM vendor-key remapping tool.

It has two runtime processes:
- `Controller`: WinUI 3 desktop app for editing config and controlling the service
- `Worker`: background process that listens for OEM events and executes actions

The UI edits config only.
The Worker performs runtime behavior.

---

## Repo layout

- `src/MeowBox.Controller/`
  WinUI 3 control panel
- `src/MeowBox.Worker/`
  background worker, tray, OSD, IPC host
- `src/MeowBox.Core/`
  shared models, IPC contracts, config, native/system services
- `src/MeowBox.Setup/`
  WiX MSI installer project
- `assets/`
  packaged assets and default config templates
- `build/`
  all intermediate outputs and package staging
- `artifacts/`
  final distributables (`MeowBox/`, optional `.zip`, `.msi`)

Do not introduce new top-level output folders unless truly necessary.

---

## Current product shape

Controller pages:
1. `Keys`
2. `Mappings`
3. `Settings`

The app should feel like a compact desktop utility, not an admin console.

---

## Runtime model

### Controller

Main coordinator:
- `src/MeowBox.Controller/Services/MeowBoxController.cs`

Worker process startup:
- `src/MeowBox.Controller/Services/WorkerProcessService.cs`

IPC client:
- `src/MeowBox.Controller/Services/WorkerPipeClient.cs`

### Worker

Main host:
- `src/MeowBox.Worker/WorkerHost.cs`

Support services:
- `src/MeowBox.Worker/Services/WorkerPipeServer.cs`
- `src/MeowBox.Worker/Services/TrayIconService.cs`
- `src/MeowBox.Worker/Services/WorkerOsdService.cs`

Worker execution flow:
1. receive `InputEvent`
2. match against configured `Keys`
3. resolve enabled `Mappings`
4. execute the selected `Action`

---

## Config model

Runtime config path:
- `%LocalAppData%\MeowBox\config.json`

Primary model:
- `src/MeowBox.Core/Models/AppConfiguration.cs`

Config service:
- `src/MeowBox.Core/Services/AppConfigService.cs`

Current schema centers on:
- `Theme`
- `Preferences`
- `Keys`
- `Mappings`

Do not reintroduce legacy config compatibility unless explicitly requested.
This project currently prefers a single clean schema.

---

## Assets

Application icon:
- `assets/app/app.ico`

OSD icons:
- `assets/osd/`

Packaged config templates:
- `assets/config/`

Rule:
- internal OSD icons must come from `assets/osd`
- application/tray icons must come from `assets/app`
- do not scatter built-in assets across random folders

---

## IPC / identity

Named pipe:
- `MeowBox.WorkerPipe`

Autostart entry:
- `MeowBoxWorker`

Single-instance mutex:
- `MeowBox.Worker.SingleInstance`

---

## Chinese text safety

- Never write Chinese text through PowerShell here-strings or any toolchain path with uncertain encoding.
- Prefer UTF-8-safe tools when editing localized strings.
- When scripting file writes, prefer Python with explicit `encoding="utf-8"`.
- After changing localization or any user-visible Chinese copy, scan `.cs`, `.xaml`, `.resw`, `.json`, and docs for broken placeholders like `??`, `???`, or replacement characters.
- If any Chinese text shows up as `?`, treat it as a real bug and fix it before finishing the task.

---

## Build and packaging

Primary command:

```powershell
.\build.ps1
```

Expected outputs:
- build staging under `build/`
- final distributables under `artifacts/`

Keep project files simple.
Do not reintroduce `Directory.Build.props` just to relocate transient `obj/` folders; the packaging script already cleans those after a build.

Current packaging targets:
- portable folder
- optional portable zip
- optional MSI installer

Packaging rules:
- user-visible entry point is `MeowBox.Controller.exe`
- `Worker` is internal runtime payload and may be staged under a subdirectory
- package from MSBuild `Publish` outputs
- default `.\build.ps1` should emit `artifacts/MeowBox/` for fast local debugging
- default portable and installer payloads should prefer smaller framework-dependent `win-x64` publish output
- use self-contained publish only when explicitly needed for a copy-and-run distribution
- the Controller publish step must preserve loose WinUI `.pri` / `.xbf` resources in the publish directory, otherwise the unpackaged app can crash at startup

If you change packaging paths, update:
- `build.ps1`
- `README.md`
- this file

---

## Important services

- `src/MeowBox.Core/Services/WmiEventMonitor.cs`
- `src/MeowBox.Core/Services/NativeActionService.cs`
- `src/MeowBox.Core/Services/AudioEndpointController.cs`
- `src/MeowBox.Core/Services/InstalledAppService.cs`
- `src/MeowBox.Core/Services/AutostartService.cs`

---

## UI constraints

### Core principles

- keep the app simple
- prioritize direct editing workflows
- avoid decorative UI without function
- avoid exposing implementation details to normal users
- prefer native/system-feeling interaction over flashy custom styling
- when a requested change naturally suggests a small adjacent polish or fix that is low-risk and clearly improves usability or consistency, do it directly
- do not stop to ask for confirmation on obvious follow-up polish unless the user explicitly asks for strict scope only or the change has non-obvious tradeoffs

### Layout

- resizing must remain stable
- `Keys` and `Mappings` lists must stay accessible in smaller windows
- main work areas should stretch and use available space
- avoid wasted empty regions

### Runtime information

Surface only user-meaningful state, such as:
- service running/stopped
- startup enabled
- tray icon enabled

Do not expose:
- raw watcher diagnostics
- internal event names unless needed for editing/debugging
- executable paths in normal UI
- multi-process architecture details in user-facing copy

### Editing model

- keep mapping creation fast
- focus the new mapping immediately after creation
- infer names from key + action whenever possible
- only show advanced fields when the action type needs them

### Action selection

- action picking should be scan-friendly
- use grouping/search/icon cues when helpful
- do not fall back to giant flat walls of options

### Visual tone

- calm
- native
- compact
- restrained

Prefer OEM/system utility feel over “product marketing UI”.
