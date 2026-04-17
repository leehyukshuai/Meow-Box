# Meow Box

[中文说明](#中文说明) | [English](#english)

![screenshot 1](screenshots/image1.png)

![screenshot 2](screenshots/image2.png)

## 中文说明

### 项目介绍

Meow Box 是一个面向 **Xiaomi Book Pro 14 2026** 的 Windows OEM 特殊按键与触控板动作自定义工具。

它的目标很直接：把原本由厂商预设的硬件行为，变成更容易调整、更好用、更好看的个人工作流。

当前版本主要提供：

- 更方便的触控板自定义动作
  - 支持 **全局重按**
  - 支持 **左上角重按 / 长按**
  - 支持 **右上角重按 / 长按**
- 更灵活的厂商按键自定义动作
  - 可自定义 **小爱键、管家键、设置键、投屏键** 等 OEM 按键
  - 支持发送单键，也支持发送 **组合键**
- 更美观的 OSD 显示
  - 支持 **Caps Lock / Backlight / Microphone / Fn Lock** 的 OSD
- 更贴合这台机器的默认配置与界面流程

### 更新状况

- 当前版本：**v1.0.0**
- 这个项目可以理解为从 **Fn Mapping Tool** 独立演化出来的 **Meow Box** 新代码库
- 当前发布文件：
  - `MeowBox-portable-v1.0.0.zip`
  - `MeowBox-setup-v1.0.0.msi`
- Release 中应包含 `artifacts/` 下构建出的发布产物

### 项目说明

#### 当前支持范围

当前版本仅面向：

- `Xiaomi Book Pro 14 2026`

#### 运行结构

- **Controller**：WinUI 3 桌面界面，用来编辑配置和控制后台服务
- **Worker**：后台进程，负责监听 OEM / 触控板事件并执行动作

普通用户实际需要打开的是 **MeowBox.Controller.exe**。

#### 主要能力

- 监听 Xiaomi Book Pro 14 2026 的 OEM / WMI 特殊按键事件
- 自定义厂商按键动作
- 自定义触控板 5 个压感动作
- 发送标准按键与组合键
- 执行系统动作、媒体动作、亮度 / 音量动作、应用启动动作
- 显示 OSD
- 后台常驻运行

#### 运行时要求

默认构建产物是 **framework-dependent**，体积更小。

目标电脑如果没装运行时，需要先安装：

- **.NET 8 Desktop Runtime x64**
- **Windows App Runtime x64（建议 1.7 或更新）**

#### 构建

默认构建：

```powershell
.\build.ps1
```

生成全部发布产物：

```powershell
.\build.ps1 -Version 1.0.0 -PackageAll
```

默认输出：

- `artifacts/MeowBox/`
- `artifacts/MeowBox-portable-v<version>.zip`
- `artifacts/MeowBox-setup-v<version>.msi`

#### 项目结构

- `src/MeowBox.Controller/` — WinUI 3 控制器
- `src/MeowBox.Worker/` — 后台 Worker
- `src/MeowBox.Core/` — 共享模型、服务、IPC
- `src/MeowBox.Setup/` — WiX 安装包工程
- `assets/` — 应用资源
- `build/` — 中间产物
- `artifacts/` — 最终发布产物

### 版权相关

- 协议：**GPL-3.0**（见 `LICENSE`）
- 应用 icon 来源：
  - https://www.flaticon.com
- **OSD 图标均为作者本人绘制**

---

## English

### Project introduction

Meow Box is a Windows customization utility for **Xiaomi Book Pro 14 2026**.

Its goal is simple: turn the laptop's OEM key behavior and pressure-based touchpad actions into something easier to configure, more useful in daily use, and visually cleaner.

The current version mainly provides:

- easier touchpad custom actions
  - **global deep press**
  - **left-top deep press / long press**
  - **right-top deep press / long press**
- more flexible OEM-key customization
  - configurable **XiaoAi key, Manager key, Settings key, Projection key**, and similar vendor keys
  - supports both single-key output and **key chords**
- better-looking OSD
  - OSD for **Caps Lock / Backlight / Microphone / Fn Lock**
- a UI and default configuration flow tailored to this device

### Release status

- Current version: **v1.0.0**
- This project can be understood as a standalone **Meow Box** codebase evolved from **Fn Mapping Tool**
- Current release files:
  - `MeowBox-portable-v1.0.0.zip`
  - `MeowBox-setup-v1.0.0.msi`
- The GitHub release should publish the artifacts built under `artifacts/`

### Project details

#### Supported target

The current version is built only for:

- `Xiaomi Book Pro 14 2026`

#### Runtime structure

- **Controller**: WinUI 3 desktop app for editing config and controlling the background service
- **Worker**: background process that listens for OEM / touchpad events and executes actions

For normal users, the main entry point is **MeowBox.Controller.exe**.

#### Main capabilities

- listens to Xiaomi Book Pro 14 2026 OEM / WMI special-key events
- customizes vendor-key behavior
- customizes 5 touchpad pressure actions
- sends standard keys and key chords
- runs system, media, brightness / volume, and application-launch actions
- shows OSD
- keeps a background worker running

#### Runtime requirements

The default package is **framework-dependent**, which keeps it smaller.

If the target PC does not already have the required runtime installed, add these first:

- **.NET 8 Desktop Runtime x64**
- **Windows App Runtime x64 (1.7 or newer recommended)**

#### Build

Default build:

```powershell
.\build.ps1
```

Build all release outputs:

```powershell
.\build.ps1 -Version 1.0.0 -PackageAll
```

Default outputs:

- `artifacts/MeowBox/`
- `artifacts/MeowBox-portable-v<version>.zip`
- `artifacts/MeowBox-setup-v<version>.msi`

#### Project layout

- `src/MeowBox.Controller/` — WinUI 3 controller app
- `src/MeowBox.Worker/` — background worker
- `src/MeowBox.Core/` — shared models, services, IPC
- `src/MeowBox.Setup/` — WiX installer project
- `assets/` — app assets
- `build/` — intermediate outputs
- `artifacts/` — final distributables

### Copyright

- License: **GPL-3.0** (see `LICENSE`)
- Application icon attribution:
  - https://www.flaticon.com
- **All OSD icons are drawn by the author**
