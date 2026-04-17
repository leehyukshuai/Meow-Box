# Meow Box

> Xiaomi Book Pro 14 2026 edition

[中文说明](#中文说明) | [English](#english)

![screenshot 1](screenshots/image1.png)

![screenshot 2](screenshots/image2.png)

## 中文说明

### ✨ 功能介绍

Meow Box 现在已经不再是一个泛用的 OEM / 厂商特殊按键映射工具，而是**特化为仅适用于 Xiaomi Book Pro 14 2026** 的定制版本。

它的目标是把这台机器上的 OEM 特殊按键和触控板压感动作，重新变成可自定义、可观察、可长期运行的功能。

它目前支持：

- 监听 Xiaomi Book Pro 14 2026 的 OEM / WMI 特殊按键事件
- 把厂商自定义按键映射为标准按键或**自定义组合键**
- 自定义触控板的 5 个压感动作：
  - 全局重按
  - 左上角重按
  - 左上角长按
  - 右上角重按
  - 右上角长按
- 执行设置、投屏、媒体控制、音量、亮度、启动应用等动作
- 为 Caps Lock、麦克风静音、Fn Lock 等状态显示 OSD
- 对界面进行了进一步优化，更适合当前这台机器的直接配置流程
- 使用 Controller + Worker 的方式长期稳定运行

它的实现方式也比较直接：后台 Worker 监听 OEM 通过 WMI 抛出的事件，再把这些事件映射到内置的固定按键表，然后执行每个按键对应的 Mapping。当前版本已精简为只支持 Xiaomi Book Pro 14 2026。

### 📦 最新版本

- 当前版本：**v0.2.0**
- Release：https://github.com/leehyukshuai/MeowBox/releases/tag/v0.2.0
- 下载：
  - Portable：`MeowBox-portable-v0.2.0.zip`
  - MSI：`MeowBox-setup-v0.2.0.msi`

### 🆕 当前分支新增内容

- 项目定位改为**仅支持 Xiaomi Book Pro 14 2026**
- 新增触控板自定义动作支持，共 5 个：
  - 全局重按
  - 左上角重按
  - 左上角长按
  - 右上角重按
  - 右上角长按
- 发送标准按键升级为支持**自定义组合键**（Ctrl / Shift / Alt / Win + 主键）
- 设置页与动作编辑页做了进一步布局优化
- 触控板轻按阈值、长按时长、实时压力显示支持自定义与联动

### 🚀 现在能做什么

- 监听内置支持的 OEM / WMI 特殊按键事件
- 给固定的一对一按键映射配置动作（Mappings）
- 给触控板的 5 个压感动作分别配置独立动作
- 执行设置、投屏、音量、媒体控制、亮度、启动应用等动作
- 发送单键或组合键
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

### 🌿 分支说明

当前仓库建议关注两个分支：

- `main`
  - 保留原先较偏泛用的项目形态
  - 主要对应较早的通用 OEM 特殊按键映射版本
  - 不包含当前 Xiaomi Book Pro 14 2026 特化的完整触控板动作与界面收敛方案

- `xiaomibook`
  - 这是当前面向 **Xiaomi Book Pro 14 2026** 的特化分支
  - 内置固定机型配置
  - 支持触控板 5 个压感动作自定义
  - 支持自定义组合键
  - 界面与默认配置都围绕这台机器进行了收敛和优化

如果你现在就是在维护或使用 Xiaomi Book Pro 14 2026 版本，应优先使用 `xiaomibook`。

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

- `artifacts/MeowBox/`

可选参数：

```powershell
.\build.ps1 -Version 0.2.0
.\build.ps1 -Zip
.\build.ps1 -Msi
.\build.ps1 -PackageAll
.\build.ps1 -SelfContained
```

输出文件：

- 默认：`artifacts/MeowBox/`
- `-Zip`：`artifacts/MeowBox-portable-v<version>.zip`
- `-Msi`：`artifacts/MeowBox-setup-v<version>.msi`
- `-PackageAll`：同时生成 zip 和 msi

### 🗂️ 项目结构

- `src/MeowBox.Controller/` — WinUI 3 控制器
- `src/MeowBox.Worker/` — 后台 Worker
- `src/MeowBox.Core/` — 共享模型、服务、IPC
- `src/MeowBox.Setup/` — WiX 安装包工程
- `assets/` — 应用资源
- `build/` — 中间产物
- `artifacts/` — 最终发布产物

### 🎨 图标来源

应用的 icon 图来自：

- https://www.flaticon.com

### 📄 作者与协议

- 作者：https://github.com/leehyukshuai
- 仓库：https://github.com/leehyukshuai/MeowBox
- 协议：**GPL-3.0**（见 `LICENSE`）

---

## English

### ✨ Feature overview

Meow Box is no longer a general-purpose OEM / vendor-key remapping utility. It is now a **device-specific build for Xiaomi Book Pro 14 2026 only**.

The goal is to turn this machine's OEM keys and pressure-based touchpad gestures into configurable actions that can be edited, observed, and kept running reliably in the background.

It currently supports:

- listening to the OEM / WMI special-key events exposed by Xiaomi Book Pro 14 2026
- mapping vendor-specific keys to standard keys or **custom key chords**
- customizing 5 touchpad pressure actions:
  - global deep press
  - left-top deep press
  - left-top long press
  - right-top deep press
  - right-top long press
- running actions such as Settings, projection, media controls, volume, brightness, and app launch
- showing OSD for states like Caps Lock, microphone mute, and Fn Lock
- further refining the UI for this machine-specific workflow
- keeping a Controller + Worker workflow running reliably in the background

The implementation is fairly direct: the Worker listens for OEM events exposed through WMI, resolves them against a built-in fixed key list, and then executes the mapped behavior. The current build is intentionally simplified to support Xiaomi Book Pro 14 2026 only.

### 📦 Latest release

- Current version: **v0.2.0**
- Release: https://github.com/leehyukshuai/MeowBox/releases/tag/v0.2.0
- Downloads:
  - Portable: `MeowBox-portable-v0.2.0.zip`
  - MSI: `MeowBox-setup-v0.2.0.msi`

### 🆕 What's new on the current branch

- narrowed the project scope to **Xiaomi Book Pro 14 2026 only**
- added touchpad custom actions for 5 pressure gestures:
  - global deep press
  - left-top deep press
  - left-top long press
  - right-top deep press
  - right-top long press
- upgraded key sending to support **custom key chords** (Ctrl / Shift / Alt / Win + primary key)
- further refined the Settings and action-editing layouts
- added linked touchpad tuning for light-press threshold, long-press duration, and live pressure display

### 🚀 What it does

- listens for the built-in OEM / WMI key events
- lets you edit the fixed one-to-one key mappings
- lets you configure separate actions for 5 touchpad pressure gestures
- runs actions such as Settings, projection, volume, media, brightness, app launch, and more
- sends single keys or key chords
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

### 🌿 Branches

The repository currently has two branches worth knowing:

- `main`
  - keeps the older, more general-shaped project line
  - mainly reflects the earlier generic OEM special-key remapping direction
  - does not represent the full Xiaomi Book Pro 14 2026-specific touchpad workflow and UI convergence

- `xiaomibook`
  - the specialized branch for **Xiaomi Book Pro 14 2026**
  - uses a fixed built-in device configuration
  - includes the 5 customizable touchpad pressure actions
  - includes custom key-chord support
  - includes further UI and default-config tuning for this specific machine

If you are using or maintaining the Xiaomi Book Pro 14 2026 version, `xiaomibook` is the branch you should use.

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

- `artifacts/MeowBox/`

Optional arguments:

```powershell
.\build.ps1 -Version 0.2.0
.\build.ps1 -Zip
.\build.ps1 -Msi
.\build.ps1 -PackageAll
.\build.ps1 -SelfContained
```

Outputs:

- Default: `artifacts/MeowBox/`
- `-Zip`: `artifacts/MeowBox-portable-v<version>.zip`
- `-Msi`: `artifacts/MeowBox-setup-v<version>.msi`
- `-PackageAll`: builds both zip and msi

### 🗂️ Project layout

- `src/MeowBox.Controller/` — WinUI 3 controller app
- `src/MeowBox.Worker/` — background worker
- `src/MeowBox.Core/` — shared models, services, IPC
- `src/MeowBox.Setup/` — WiX installer project
- `assets/` — app assets
- `build/` — intermediate outputs
- `artifacts/` — final distributables

### 🎨 Icon attribution

The application icon comes from:

- https://www.flaticon.com

### 📄 Author and license

- Author: https://github.com/leehyukshuai
- Repository: https://github.com/leehyukshuai/MeowBox
- License: **GPL-3.0** (see `LICENSE`)
