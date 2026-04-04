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
  内置图标、OSD 资源、preset
- `build/`
  构建中间产物
- `portable/FnMappingTool/`
  最终可直接运行的扁平分发目录

控制面板分为 4 个页面：

1. `Keys`
2. `Actions`
3. `Mappings`
4. `Settings`

运行时配置文件：

- `%LocalAppData%\FnMappingTool\config.json`

内置预设：

- `assets\presets\xiaomi-default.json`

内置资源目录约定：

- `assets\osd\`：OSD 内置图标
- `assets\app\`：程序与 tray 使用的应用图标
- `assets\presets\`：预设

## 构建

```powershell
.\build.ps1
```

构建脚本会：

1. 构建 `FnMappingTool.Controller` 的 Release 产物
2. 复制控制面板所需运行文件到 `portable/FnMappingTool/`
3. 构建 `FnMappingTool.Worker` 的 Release 产物并复制到同一目录
4. 删除 `.pdb`

最终主要文件：

- `portable\FnMappingTool\FnMappingTool.Controller.exe`
- `portable\FnMappingTool\FnMappingTool.Worker.exe`

说明：

- 当前架构明确分为控制面板和后台 Worker，所以最终最简分发形态是 `2 个 exe`
- Worker 不再使用单文件发布，避免首次启动时的自解压/冷启动明显变慢
- WinUI 3 的 `Publish` 产物在当前环境下会启动崩溃，因此当前分发基于 `Release Build` 产物拷贝，而不是 `Publish`

详细架构说明见：

- [AGENTS.md](/C:/Users/hyuk/Desktop/OSD/AGENTS.md)
