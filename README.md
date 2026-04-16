# Fn Mapping Tool

[中文说明](#中文说明) | [English](#english)

![screenshot 1](screenshots/image1.png)

![screenshot 2](screenshots/image2.png)

## 中文说明

### ✨ 功能介绍

Fn Mapping Tool 是一个面向 Windows 笔记本的 OEM / 厂商特殊按键映射工具，重点是把原本只能由厂商管家控制的特殊按键，重新变成可自定义、可观察、可移植的功能。

它目前支持：

- 监听 Xiaomi Book Pro 14 2026 的 OEM / WMI 特殊按键事件
- 把厂商自定义按键映射为标准键盘按键
- 执行设置、投屏、媒体控制、音量、亮度、启动应用等动作
- 为 Caps Lock、麦克风静音、Fn Lock 等状态显示 OSD
- 使用 Controller + Worker 的方式长期稳定运行

它的实现方式也比较直接：后台 Worker 监听 OEM 通过 WMI 抛出的事件，再把这些事件映射到内置的固定按键表，然后执行每个按键对应的 Mapping。当前版本已精简为只支持 Xiaomi Book Pro 14 2026。

### 📦 最新版本

- 当前版本：**v0.2.0**
- Release：https://github.com/leehyukshuai/Fn-Mapping-Tool/releases/tag/v0.2.0
- 下载：
  - Portable：`FnMappingTool-portable-v0.2.0.zip`
  - MSI：`FnMappingTool-setup-v0.2.0.msi`

### 🆕 v0.2.0 更新

- 新增中英文界面支持，默认跟随系统语言
- 优化 Controller 界面与交互体验
- 支持 Caps Lock / 麦克风静音状态的 OSD 显示
- 修复 OSD 偶发被其他窗口遮挡的问题
- 重绘并更新 OSD 图标
- 新增计划任务优先启动
- 支持将厂商自定义按键映射为标准按键

### 🚀 现在能做什么

- 监听内置支持的 OEM / WMI 特殊按键事件
- 给固定的一对一按键映射配置动作（Mappings）
- 执行设置、投屏、音量、媒体控制、亮度、启动应用等动作
- 显示 OSD
- 由后台 Worker 常驻处理按键事件

### 🧪 测试范围

> 目前只在 **Xiaomi Book Pro 14 2026 + Windows 11** 上实际测试过。

### 🧩 运行结构

- **Controller**：WinUI 3 桌面界面，用来编辑配置和控制后台服务
- **Worker**：后台进程，负责监听 OEM 事件并执行动作

普通用户实际需要打开的是 **Controller**。

### 📦 当前支持机型

当前内置并固定支持：

- `Xiaomi Book Pro 14 2026`

应用会直接生成这台机器对应的默认配置，不再提供 preset 导入或切换。

### 🛠️ 运行时要求

默认构建产物是 **framework-dependent**，体积更小。

目标电脑如果没装运行时，需要先安装：

- **.NET 8 Desktop Runtime x64**
- **Windows App Runtime x64（建议 1.7 或更新）**

如果你想打一个更接近“拷过去就能跑”的包：

```powershell
.\build.ps1 -SelfContained
```

### 🔨 构建

默认构建：

```powershell
.\build.ps1
```

默认只会生成一个便于直接调试的便携目录：

- `artifacts/FnMappingTool/`

可选参数：

```powershell
.\build.ps1 -Version 0.2.0
.\build.ps1 -Zip
.\build.ps1 -Msi
.\build.ps1 -PackageAll
.\build.ps1 -SelfContained
```

输出文件：

- 默认：`artifacts/FnMappingTool/`
- `-Zip`：`artifacts/FnMappingTool-portable-v<version>.zip`
- `-Msi`：`artifacts/FnMappingTool-setup-v<version>.msi`
- `-PackageAll`：同时生成 zip 和 msi

### 🗂️ 项目结构

- `src/FnMappingTool.Controller/` — WinUI 3 控制器
- `src/FnMappingTool.Worker/` — 后台 Worker
- `src/FnMappingTool.Core/` — 共享模型、服务、IPC
- `src/FnMappingTool.Setup/` — WiX 安装包工程
- `assets/` — 应用资源
- `build/` — 中间产物
- `artifacts/` — 最终发布产物

### 🎨 图标来源

应用的 icon 图来自：

- https://www.flaticon.com

### 📄 作者与协议

- 作者：https://github.com/leehyukshuai
- 仓库：https://github.com/leehyukshuai/Fn-Mapping-Tool
- 协议：**GPL-3.0**（见 `LICENSE`）

---

## English

### ✨ Feature overview

Fn Mapping Tool is a Windows utility for turning OEM / vendor-specific keys into configurable features instead of one-off buttons locked behind vendor software.

It currently supports:

- listening to the OEM / WMI special-key events exposed by Xiaomi Book Pro 14 2026
- mapping vendor-specific keys to standard keyboard keys
- running actions such as Settings, projection, media controls, volume, brightness, and app launch
- showing OSD for states like Caps Lock, microphone mute, and Fn Lock
- keeping a Controller + Worker workflow running reliably in the background

The implementation is fairly direct: the Worker listens for OEM events exposed through WMI, resolves them against a built-in fixed key list, and then executes the mapped behavior. The current build is intentionally simplified to support Xiaomi Book Pro 14 2026 only.

### 📦 Latest release

- Current version: **v0.2.0**
- Release: https://github.com/leehyukshuai/Fn-Mapping-Tool/releases/tag/v0.2.0
- Downloads:
  - Portable: `FnMappingTool-portable-v0.2.0.zip`
  - MSI: `FnMappingTool-setup-v0.2.0.msi`

### 🆕 What's new in v0.2.0

- added Chinese and English UI support with system-language default
- improved the Controller UI and editing flow
- added Caps Lock / microphone mute OSD support
- fixed the OSD occasionally being covered by other windows
- refreshed the OSD icons with more vivid artwork
- added priority startup through Task Scheduler
- added support for mapping vendor-specific keys to standard keys

### 🚀 What it does

- listens for the built-in OEM / WMI key events
- lets you edit the fixed one-to-one key mappings
- runs actions such as Settings, projection, volume, media, brightness, app launch, and more
- shows OSD
- keeps a background Worker running to handle events

### 🧪 Test coverage

> Right now, it has only been tested on **Xiaomi Book Pro 14 2026 + Windows 11**.

### 🧩 Runtime structure

- **Controller**: WinUI 3 desktop app for editing config and controlling the background service
- **Worker**: background process that listens for OEM events and executes actions

For normal users, the entry point is **Controller**.

### 📦 Supported device

The current build has one built-in target:

- `Xiaomi Book Pro 14 2026`

The app now creates the matching default configuration directly and no longer exposes preset import or model switching.

### 🛠️ Runtime requirements

The default package is **framework-dependent**, which keeps it smaller.

If the target PC does not already have the required runtime installed, add these first:

- **.NET 8 Desktop Runtime x64**
- **Windows App Runtime x64 (1.7 or newer recommended)**

If you want a larger package that is closer to copy-and-run:

```powershell
.\build.ps1 -SelfContained
```

### 🔨 Build

Default build:

```powershell
.\build.ps1
```

By default this now creates only a portable folder for quick local testing:

- `artifacts/FnMappingTool/`

Optional arguments:

```powershell
.\build.ps1 -Version 0.2.0
.\build.ps1 -Zip
.\build.ps1 -Msi
.\build.ps1 -PackageAll
.\build.ps1 -SelfContained
```

Outputs:

- Default: `artifacts/FnMappingTool/`
- `-Zip`: `artifacts/FnMappingTool-portable-v<version>.zip`
- `-Msi`: `artifacts/FnMappingTool-setup-v<version>.msi`
- `-PackageAll`: builds both zip and msi

### 🗂️ Project layout

- `src/FnMappingTool.Controller/` — WinUI 3 controller app
- `src/FnMappingTool.Worker/` — background worker
- `src/FnMappingTool.Core/` — shared models, services, IPC
- `src/FnMappingTool.Setup/` — WiX installer project
- `assets/` — app assets
- `build/` — intermediate outputs
- `artifacts/` — final distributables

### 🎨 Icon attribution

The application icon comes from:

- https://www.flaticon.com

### 📄 Author and license

- Author: https://github.com/leehyukshuai
- Repository: https://github.com/leehyukshuai/Fn-Mapping-Tool
- License: **GPL-3.0** (see `LICENSE`)
