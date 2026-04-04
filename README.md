# Fn Mapping Tool

`Fn Mapping Tool` 是一个前后端分离的 Windows 厂商私有按键重映射工具。

项目结构：

- `src/FnMappingTool.Controller`
  WinUI 3 控制面板
- `src/FnMappingTool.Worker`
  后台 Worker，负责 WMI 监听、动作执行、OSD 和 tray icon
- `src/FnMappingTool.Core`
  共享配置模型、WMI 事件、系统动作和 IPC 协议
- `assets/`
  内置图标与默认配置
- `installer/FnMappingTool.Setup/`
  WiX MSI 安装包工程
- `build/`
  构建中间产物
- `portable/FnMappingTool/`
  最终 portable 目录
- `artifacts/`
  最终打包产物（zip / msi）

控制面板分为 3 个页面：

1. `Keys`
2. `Mappings`
3. `Settings`

运行时配置文件：

- `%LocalAppData%\FnMappingTool\config.json`

默认配置模板：

- `assets\config\xiaomi-default.json`

内置资源目录约定：

- `assets\osd\`：OSD 内置图标
- `assets\app\`：程序图标
- `assets\config\`：默认配置模板

## 构建与打包

```powershell
.\build.ps1
```

可选参数：

```powershell
.\build.ps1 -Version 0.1.0
.\build.ps1 -SkipMsi
.\build.ps1 -SkipZip
```

脚本会完成：

1. 构建 `FnMappingTool.Controller` 的 x64 Release 产物
2. 构建 `FnMappingTool.Worker` 的 x64 Release 产物
3. 生成 `portable\FnMappingTool\`
   - 用户可见入口只有 `FnMappingTool.Controller.exe`
   - worker 被放入内部目录 `runtime\worker\`
4. 生成 portable zip：
   - `artifacts\FnMappingTool-portable-v<version>.zip`
5. 生成 MSI 安装包：
   - `artifacts\FnMappingTool-setup-v<version>.msi`

最终主要文件：

- `portable\FnMappingTool\FnMappingTool.Controller.exe`
- `portable\FnMappingTool\runtime\worker\FnMappingTool.Worker.exe`
- `artifacts\FnMappingTool-portable-v<version>.zip`
- `artifacts\FnMappingTool-setup-v<version>.msi`

说明：

- 对用户可见的主入口只有 controller；worker 作为内部运行时随包分发
- Worker 不使用单文件发布，避免首次启动时自解压带来的冷启动延迟
- Controller 仍基于 `Release Build` 产物分发，而不是 `Publish`，以规避当前机器上的 WinUI 3 publish 启动问题

详细架构说明见：

- [AGENTS.md](/C:/Users/hyuk/Desktop/OSD/AGENTS.md)
