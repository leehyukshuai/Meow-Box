# AGENTS

## Goal

This repo is a Windows OEM/vendor-key remapping tool.

Source layout:

- `src/FnMappingTool.Controller`
  WinUI 3 control panel
- `src/FnMappingTool.Worker`
  background worker process
- `src/FnMappingTool.Core`
  shared models, services, IPC contracts
- `assets/`
  built-in assets and presets
- `build/`
  intermediate outputs
- `portable/FnMappingTool/`
  final runnable package

## Product shape

The control panel has 4 pages:

1. `Keys`
2. `Actions`
3. `Mappings`
4. `Settings`

The UI edits config only.
The Worker listens to OEM events and executes actions.

## Runtime model

### Controller

Main coordinator:

- `src/FnMappingTool.Controller/Services/FnMappingToolController.cs`

Worker startup / IPC:

- `src/FnMappingTool.Controller/Services/WorkerProcessService.cs`
- `src/FnMappingTool.Controller/Services/WorkerPipeClient.cs`

### Worker

Main host:

- `src/FnMappingTool.Worker/WorkerHost.cs`

Support:

- `src/FnMappingTool.Worker/Services/WorkerPipeServer.cs`
- `src/FnMappingTool.Worker/Services/TrayIconService.cs`
- `src/FnMappingTool.Worker/Services/WorkerOsdService.cs`

Worker flow:

1. receive `InputEvent`
2. match against `Keys`
3. resolve enabled `Mappings`
4. resolve `Actions`
5. execute actions

## Config

Config path:

- `%LocalAppData%\FnMappingTool\config.json`

Shared model:

- `src/FnMappingTool.Core/Models/AppConfiguration.cs`

Config service:

- `src/FnMappingTool.Core/Services/AppConfigService.cs`

Schema:

- `Theme`
- `Preferences`
- `Keys`
- `Actions`
- `Mappings`

## IPC and identity

Named pipe:

- `FnMappingTool.WorkerPipe`

Autostart entry:

- `FnMappingToolWorker`

Single-instance mutex:

- `FnMappingTool.Worker.SingleInstance`

## Important services

- `src/FnMappingTool.Core/Services/WmiEventMonitor.cs`
- `src/FnMappingTool.Core/Services/NativeActionService.cs`
- `src/FnMappingTool.Core/Services/AudioEndpointController.cs`
- `src/FnMappingTool.Core/Services/InstalledAppService.cs`
- `src/FnMappingTool.Core/Services/AutostartService.cs`

## Defaults

Built-in preset:

- `assets/presets/xiaomi-default.json`

Built-in assets:

- `assets/osd/`
- `assets/app/`

## Build and package

Use:

```powershell
.\build.ps1
```

Current packaging strategy:

- Controller uses `Release Build` output copied into `portable/FnMappingTool/`
- Worker uses `Release Build` output copied into the same directory
- `.pdb` files are removed from the portable folder

Important:

- WinUI 3 `Publish` output was reproduced to crash on this machine
- `Release Build` output for the controller was verified to open successfully

Final runnable files:

- `portable/FnMappingTool/FnMappingTool.Controller.exe`
- `portable/FnMappingTool/FnMappingTool.Worker.exe`


## Additional UI design requirements

The Controller UI must behave like a robust desktop application, not like a static mockup.

### Resizing and window behavior

The app must remain usable across normal desktop window sizes and common Windows scaling factors.

Requirements:
- resizing must feel stable and intentional
- the layout must adapt when the window becomes larger or smaller
- the UI must not become visually tiny after resize or scale changes
- maximize must remain fully usable
- content should expand to use available space instead of leaving large unused regions
- important working areas should stretch vertically when space is available

Do not treat the initial design size as the only supported size.

### Minimum usable layout

Core editing workflows must remain usable even in relatively small windows.

Requirements:
- essential lists must remain visible
- the user must always be able to reach the main working collection for the current page
- do not hide the primary list or replace it with an unusable collapsed state
- if space becomes constrained, simplify secondary panels first, not the core workflow
- list visibility is more important than preserving decorative spacing or oversized panels

In particular, pages like `Keys` and `Mappings` must always preserve access to their main lists.

### Space usage

Use available vertical space efficiently.

Requirements:
- editing panes should fill the available height in a balanced way
- avoid layouts where obvious empty lower space remains while important panels are cramped
- list regions and detail regions should visually occupy the space they are given
- avoid artificial height limits that make the app feel unfinished or wasteful

### Feature exposure and runtime information

Do not expose internal runtime or process details unless they are strictly necessary for the user.

The normal user should not need to understand:
- low-level event watcher state
- raw runtime diagnostics
- specific internal event names
- worker executable paths
- multi-process architecture details

The UI should surface only user-meaningful runtime status, such as:
- whether the service is running
- whether startup is enabled
- whether tray icon is enabled

Keep runtime status lightweight and clear.
Prefer simple status indicators over verbose technical text.

### Simplicity of the editing model

Prefer the simplest mental model that matches how users think.

If a concept does not provide clear value to normal users as a separate top-level object, do not force the UI to revolve around it.

The UI should prioritize the user workflow over internal schema purity.

For creation flows:
- reduce unnecessary naming steps
- infer labels when possible
- generate sensible default names automatically
- ask the user only for information that is actually needed to complete the task

### Mapping creation flow

The mapping creation experience should be fast and direct.

Preferred behavior:
- creating a new mapping should immediately focus that new mapping in the editor
- new entries should become editable without extra navigation
- mapping names should be generated automatically from meaningful user selections
- the user should primarily choose the input key and the target action
- additional fields should appear only when the chosen action type requires more configuration

Do not make users fill empty metadata first before they can perform the real task.

### Action selection

Action selection must be easy to scan and pleasant to use.

Requirements:
- actions should be grouped in a way users can understand quickly
- grouping may use multiple tags when helpful
- actions should be discoverable by category and by search
- actions should not be presented as one long flat list
- action choices should have strong visual cues
- icons should support recognition, not just decoration

Prefer action picking patterns that reduce reading fatigue.

### Icon usage

Use more icon support across the app where it improves recognition and scan speed.

Requirements:
- action choices should usually include icons
- navigation and important commands should use icons where appropriate
- icons must remain consistent in style and weight
- icons should support comprehension, not create clutter

Prefer native/system icon language whenever possible.

### Progressive complexity

Only show complexity when the selected feature genuinely needs it.

Requirements:
- simple actions should remain simple
- advanced fields should appear conditionally
- actions such as launching an app, showing OSD, or changing tray/taskbar icon may reveal more settings
- do not front-load advanced configuration for every action

### Visual calmness

The app should feel calm and native even when feature-rich.

Requirements:
- do not overcrowd the interface
- do not over-explain everything in the main layout
- avoid verbose labels in prominent areas such as the title bar
- use concise visual status where possible
- preserve clarity and comfort over information density for its own sake

The user should feel that the app is guiding them through a small number of clear choices, not presenting an engineering console.
