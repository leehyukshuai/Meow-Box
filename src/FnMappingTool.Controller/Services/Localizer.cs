using System.Globalization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.Services;

public static class Localizer
{
    public static string GetString(string key)
    {
        return key switch
        {
            "App.Title" => LocalizedText.Pick("Fn Mapping Tool", "Fn 映射工具"),
            "Navigation.Keys" => LocalizedText.Pick("Keys", "按键"),
            "Navigation.Mappings" => LocalizedText.Pick("Mappings", "映射"),
            "Navigation.Settings" => LocalizedText.Pick("Settings", "设置"),
            "PageTitle.Keys" => LocalizedText.Pick("Keys", "按键"),
            "PageTitle.Mappings" => LocalizedText.Pick("Mappings", "映射"),
            "PageTitle.Settings" => LocalizedText.Pick("Settings", "设置"),
            "ServiceStatus.Running" => LocalizedText.Pick("Service running", "服务运行中"),
            "ServiceStatus.Stopped" => LocalizedText.Pick("Service stopped", "服务未运行"),
            "QuickService.Start" => LocalizedText.Pick("Start service", "启动服务"),
            "QuickService.Stop" => LocalizedText.Pick("Stop service", "停止服务"),
            "Dialog.Close" => LocalizedText.Pick("Close", "关闭"),
            "Dialog.Cancel" => LocalizedText.Pick("Cancel", "取消"),
            "Dialog.Delete" => LocalizedText.Pick("Delete", "删除"),
            "Dialog.Later" => LocalizedText.Pick("Later", "稍后"),
            "Dialog.RestartNow" => LocalizedText.Pick("Restart now", "立即重启"),
            "Dialog.RestartService" => LocalizedText.Pick("Restart service", "重启服务"),
            "Settings.Messages.StartServiceFailed.Title" => LocalizedText.Pick("Could not start service", "无法启动服务"),
            "Settings.Messages.StartServiceFailed.Body" => LocalizedText.Pick("The background worker could not be started or did not respond.", "后台 Worker 无法启动，或启动后没有响应。"),
            "Settings.Messages.AutostartFailed.Title" => LocalizedText.Pick("Could not change startup behavior", "无法修改开机启动设置"),
            "Settings.Messages.PriorityFailed.Title" => LocalizedText.Pick("Could not change startup priority", "无法修改优先启动设置"),
            "Settings.Messages.ImportFailed.Title" => LocalizedText.Pick("Could not import configuration", "无法导入配置"),
            "Settings.Messages.ImportApplied.Title" => LocalizedText.Pick("Configuration imported", "配置已导入"),
            "Settings.Messages.ImportApplied.BodyServiceRunning" => LocalizedText.Pick("The file has been imported. Restart the service to apply it.", "文件已导入。请重启服务以应用该配置。"),
            "Settings.Messages.ImportApplied.BodyServiceStopped" => LocalizedText.Pick("The file has been imported. Start the service when you are ready to apply it.", "文件已导入。准备好后启动服务即可应用该配置。"),
            "Settings.Messages.RestartServiceFailed.Title" => LocalizedText.Pick("Could not restart service", "无法重启服务"),
            "Settings.Messages.RestartServiceFailed.Body" => LocalizedText.Pick("The background worker could not be restarted.", "后台 Worker 无法重启。"),
            "Settings.Messages.LanguageRestart.Title" => LocalizedText.Pick("Restart required", "需要重启"),
            "Settings.Messages.LanguageRestart.Body" => LocalizedText.Pick("Language changes will apply after restarting Fn Mapping Tool.", "重启 Fn 映射工具后，语言更改才会生效。"),
            "Keys.Messages.ServiceStopped.Title" => LocalizedText.Pick("Background service is stopped", "后台服务未运行"),
            "Keys.Messages.ServiceStopped.Body" => LocalizedText.Pick("Start the background service in Settings before capturing a new key.", "请先在“设置”中启动后台服务，再捕获新的按键。"),
            "Keys.Messages.AddFailed.Title" => LocalizedText.Pick("Could not add key", "无法添加按键"),
            "Keys.Messages.SaveFailed.Title" => LocalizedText.Pick("Could not save key", "无法保存按键"),
            "Keys.Messages.Delete.Title" => LocalizedText.Pick("Delete key", "删除按键"),
            "Keys.Messages.Delete.Body" => LocalizedText.Pick("This will also delete mappings that use the selected key.", "这也会删除所有使用该按键的映射。"),
            "Mappings.Messages.AddFailed.Title" => LocalizedText.Pick("Could not add mapping", "无法添加映射"),
            "Mappings.Messages.Delete.Title" => LocalizedText.Pick("Delete mapping", "删除映射"),
            "Mappings.Messages.Delete.Body" => LocalizedText.Pick("Delete the selected mapping?", "要删除当前选中的映射吗？"),
            "Mappings.Messages.SaveFailed.Title" => LocalizedText.Pick("Could not save mapping", "无法保存映射"),
            "Mappings.NoIcon" => LocalizedText.Pick("No icon", "无图标"),
            "AddKey.CaptureWaiting" => LocalizedText.Pick("Waiting for the next OEM event...", "正在等待下一个 OEM 事件……"),
            "AddKey.CaptureCancelled" => LocalizedText.Pick("Capture was cancelled or timed out.", "捕获已取消或超时。"),
            "AddKey.CaptureSuccess" => LocalizedText.Pick("OEM event captured. Enter a unique key name and add it.", "已捕获 OEM 事件。请输入唯一的按键名称并添加。"),
            "AddKey.NameExists" => LocalizedText.Pick("This key name already exists.", "这个按键名称已存在。"),
            "AppPicker.NoInstalledApps" => LocalizedText.Pick("No installed apps were returned by Get-StartApps.", "Get-StartApps 没有返回任何已安装应用。"),
            "AppPicker.FoundInstalledApps" => LocalizedText.Pick("Found {0} installed apps.", "找到 {0} 个已安装应用。"),
            _ => key
        };
    }

    public static string Format(string key, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, GetString(key), arguments);
    }
}