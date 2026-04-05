# Fn Mapping Tool

## 中文说明

![banner](screenshots\banner.jpeg)

### Screenshots

### 这是什么

Fn Mapping Tool 是一个给 Windows 笔记本用的 OEM / 厂商特殊按键映射工具。

这个项目最初是因为 **小米电脑自带的电脑管家不好用**：

- 自定义程度低
- 很多功能鸡肋
- 卸载后，一些特殊按键会直接失效

我在自己的机器上遇到的典型问题就是：

- 麦克风静音键
- 电脑管家键
- 小爱键

在卸载电脑管家后无法正常使用。

所以我自己用 vibe coding 写了这个工具。除了把这些键救回来，也顺手把能力做得更通用：

- 可以捕获 OEM / WMI 按键事件
- 可以把事件保存成 Key
- 可以把 Key 映射到不同 Action
- 可以显示 OSD
- 可以让别的机器也做自己的适配和 preset

### 当前测试范围

> **重要：这个工具目前没有在其他机器上充分测试。**
>
> 目前只在 **Xiaomi Book Pro 14 2026 + Windows 11** 上实际测试过。

如果你在别的机器上尝试成功，欢迎提交适配后的 preset。

### 主要功能

- 捕获厂商特殊按键事件
- 保存按键定义（Keys）
- 自定义按键映射（Mappings）
- 支持打开设置、投屏、音量、媒体控制、亮度、启动应用等动作
- 支持自定义 OSD
- 后台 Worker 常驻处理按键事件

### 运行结构

- **Controller**
  - WinUI 3 桌面界面
  - 用来编辑配置、控制后台服务
- **Worker**
  - 后台进程
  - 监听 OEM 事件并执行映射动作

对普通用户来说，真正要打开的是 **Controller**。

### 预设（Preset）说明

仓库内置的预设放在：

- `assets/config/`

例如：

- `assets/config/xiaomibookpro14 2026.json`

应用启动时，这些预设会同步到用户目录：

- `%LocalAppData%\FnMappingTool\presets`

你可以在应用的 **Settings > Files** 里：

- 打开 preset 文件夹
- 复制自己的 preset 进去
- 刷新列表
- 直接加载 preset

### 给别人做适配 / 提交 preset

欢迎把这个工具适配到你自己的电脑，并把 preset 提交到这个仓库。

为了方便别人使用，请尽量：

1. 在 preset 文件名里写清楚机器型号
   - 例如：`brand-model-year.json`
2. 在 README / PR 描述里写清楚：
   - 品牌
   - 具体型号
   - 年份或代次
   - 操作系统版本
3. 尽量说明哪些键已经适配
   - 例如：麦克风静音、助手键、性能键、投影键等

这样别人才能更容易判断这个 preset 能不能直接拿来用。

### 默认构建产物需要什么运行时

默认生成的是 **framework-dependent** 包，体积更小。

这意味着目标电脑如果没有相关运行时，需要自己安装。最短说明如下：

- **.NET 8 Desktop Runtime x64**
- **Windows App Runtime x64（建议 1.7 或更新）**

如果你想生成“尽量拷过去就能直接运行”的大包，可以使用：

```powershell
.\build.ps1 -SelfContained
```

### 构建产物

默认构建：

```powershell
.\build.ps1
```

可选参数：

```powershell
.\build.ps1 -Version 0.1.0
.\build.ps1 -SkipZip
.\build.ps1 -SkipMsi
.\build.ps1 -SelfContained
```

生成结果：

- `artifacts/FnMappingTool-portable-v<version>.zip`
- `artifacts/FnMappingTool-setup-v<version>.msi`

### 项目目录

- `src/FnMappingTool.Controller/` — WinUI 3 控制器
- `src/FnMappingTool.Worker/` — 后台 Worker
- `src/FnMappingTool.Core/` — 共享模型、服务、IPC
- `src/FnMappingTool.Setup/` — WiX 安装包工程
- `assets/` — 应用资源和预设
- `build/` — 中间产物
- `artifacts/` — 最终发布产物

### 图标来源

项目中使用的部分免费图标来源于：

- https://www.flaticon.com

如需二次分发或继续扩展，请同时注意对应图标作者与站点的署名 / 使用要求。

### 作者与协议

- 作者：https://github.com/leehyukshuai
- 仓库：https://github.com/leehyukshuai/Fn-Mapping-Tool
- 协议：**GPL-3.0**（见仓库中的 `LICENSE`）

---

## English

### Screenshots

`screenshots/choose action.png` shows the action picker UI.

### What this project is

Fn Mapping Tool is a Windows utility for remapping OEM / vendor-specific keys.

This project exists because the stock **Xiaomi PC Manager is not good enough** for this use case:

- it is not very customizable
- some features are not very useful
- after uninstalling it, several special keys stop working

On my machine, the most obvious broken keys after removing PC Manager were:

- microphone mute
- the PC Manager key
- the XiaoAi key

So I wrote this tool with vibe coding to bring those keys back, and then expanded it into something more general so other laptops can also be adapted.

It can:

- capture OEM / WMI key events
- save them as Keys
- map Keys to Actions
- show OSD
- allow other machines to provide their own presets

### Current test coverage

> **Important: this tool has not been broadly tested on other machines.**
>
> It has only been tested on **Xiaomi Book Pro 14 2026 + Windows 11**.

If you make it work on another machine, contributions are very welcome.

### Main features

- capture vendor-specific key events
- define named Keys
- create custom Mappings
- map keys to actions such as Settings, projection, volume, media, brightness, launching apps, and custom OSD
- run a background Worker to handle events

### Runtime structure

- **Controller**
  - WinUI 3 desktop app
  - edits config and controls the background service
- **Worker**
  - background process
  - listens for OEM events and executes mapped actions

For end users, the visible entry point is the **Controller**.

### Presets

Built-in repository presets live in:

- `assets/config/`

Example:

- `assets/config/xiaomibookpro14 2026.json`

When the app starts, these presets are synced into the user preset directory:

- `%LocalAppData%\FnMappingTool\presets`

In **Settings > Files**, users can:

- open the preset folder
- drop in their own preset JSON files
- refresh the list
- load a preset directly

### Contributing presets for other machines

If you adapt this tool to your own laptop, please feel free to contribute your preset back to this repository.

To make presets actually useful for other people, please include:

1. a clear machine model in the preset file name
   - for example: `brand-model-year.json`
2. the exact machine details in the README or PR description
   - brand
   - exact model
   - year / generation
   - OS version
3. a short note about which special keys are already adapted
   - for example: microphone mute, assistant key, performance key, projection key, etc.

That makes it much easier for other users to know whether a preset may work for them.

### Runtime requirements for the default package

The default build is **framework-dependent** to keep the package smaller.

If the target PC does not already have the needed runtimes, install these first:

- **.NET 8 Desktop Runtime x64**
- **Windows App Runtime x64 (1.7 or newer recommended)**

If you want a larger package that is closer to copy-and-run, use:

```powershell
.\build.ps1 -SelfContained
```

### Build outputs

Default build:

```powershell
.\build.ps1
```

Optional arguments:

```powershell
.\build.ps1 -Version 0.1.0
.\build.ps1 -SkipZip
.\build.ps1 -SkipMsi
.\build.ps1 -SelfContained
```

Final artifacts:

- `artifacts/FnMappingTool-portable-v<version>.zip`
- `artifacts/FnMappingTool-setup-v<version>.msi`

### Project layout

- `src/FnMappingTool.Controller/` — WinUI 3 controller app
- `src/FnMappingTool.Worker/` — background worker
- `src/FnMappingTool.Core/` — shared models, services, IPC
- `src/FnMappingTool.Setup/` — WiX installer project
- `assets/` — packaged assets and presets
- `build/` — intermediate outputs
- `artifacts/` — final distributables

### Icon attribution

Some free icons used in this project come from:

- https://www.flaticon.com

If you redistribute or extend the project, please also follow the attribution / usage requirements of the corresponding icon authors and the site.

### Author and license

- Author: https://github.com/leehyukshuai
- Repository: https://github.com/leehyukshuai/Fn-Mapping-Tool
- License: **GPL-3.0** (see `LICENSE`)
