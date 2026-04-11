using System.Globalization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.Services;

public static class Localizer
{
    public static string GetString(string key)
    {
        return key switch
        {
            "App.Title" => LocalizedText.Pick("Fn Mapping Tool", "Fn \u6620\u5c04\u5de5\u5177"),
            "Navigation.Keys" => LocalizedText.Pick("Keys", "\u6309\u952e"),
            "Navigation.Mappings" => LocalizedText.Pick("Mappings", "\u6620\u5c04"),
            "Navigation.Settings" => LocalizedText.Pick("Settings", "\u8bbe\u7f6e"),
            "PageTitle.Keys" => LocalizedText.Pick("Keys", "\u6309\u952e"),
            "PageTitle.Mappings" => LocalizedText.Pick("Mappings", "\u6620\u5c04"),
            "PageTitle.Settings" => LocalizedText.Pick("Settings", "\u8bbe\u7f6e"),
            "ServiceStatus.Running" => LocalizedText.Pick("Service running", "\u670d\u52a1\u8fd0\u884c\u4e2d"),
            "ServiceStatus.Stopped" => LocalizedText.Pick("Service stopped", "\u670d\u52a1\u672a\u8fd0\u884c"),
            "QuickService.Start" => LocalizedText.Pick("Start service", "\u542f\u52a8\u670d\u52a1"),
            "QuickService.Stop" => LocalizedText.Pick("Stop service", "\u505c\u6b62\u670d\u52a1"),
            "Dialog.Close" => LocalizedText.Pick("Close", "\u5173\u95ed"),
            "Dialog.Cancel" => LocalizedText.Pick("Cancel", "\u53d6\u6d88"),
            "Dialog.Delete" => LocalizedText.Pick("Delete", "\u5220\u9664"),
            "Dialog.Later" => LocalizedText.Pick("Later", "\u7a0d\u540e"),
            "Dialog.RestartNow" => LocalizedText.Pick("Restart now", "\u7acb\u5373\u91cd\u542f"),
            "Dialog.RestartService" => LocalizedText.Pick("Restart service", "\u91cd\u542f\u670d\u52a1"),
            "Settings.Messages.StartServiceFailed.Title" => LocalizedText.Pick("Could not start service", "\u65e0\u6cd5\u542f\u52a8\u670d\u52a1"),
            "Settings.Messages.StartServiceFailed.Body" => LocalizedText.Pick("The background worker could not be started or did not respond.", "\u540e\u53f0 Worker \u65e0\u6cd5\u542f\u52a8\uff0c\u6216\u542f\u52a8\u540e\u6ca1\u6709\u54cd\u5e94\u3002"),
            "Settings.Messages.AutostartFailed.Title" => LocalizedText.Pick("Could not change startup behavior", "\u65e0\u6cd5\u4fee\u6539\u5f00\u673a\u542f\u52a8\u8bbe\u7f6e"),
            "Settings.Messages.PriorityFailed.Title" => LocalizedText.Pick("Could not change startup priority", "\u65e0\u6cd5\u4fee\u6539\u4f18\u5148\u542f\u52a8\u8bbe\u7f6e"),
            "Settings.Messages.ImportFailed.Title" => LocalizedText.Pick("Could not import configuration", "\u65e0\u6cd5\u5bfc\u5165\u914d\u7f6e"),
            "Settings.Messages.ImportApplied.Title" => LocalizedText.Pick("Configuration imported", "\u914d\u7f6e\u5df2\u5bfc\u5165"),
            "Settings.Messages.ImportApplied.BodyServiceRunning" => LocalizedText.Pick("The file has been imported. Restart the service to apply it.", "\u6587\u4ef6\u5df2\u5bfc\u5165\u3002\u8bf7\u91cd\u542f\u670d\u52a1\u4ee5\u5e94\u7528\u8be5\u914d\u7f6e\u3002"),
            "Settings.Messages.ImportApplied.BodyServiceStopped" => LocalizedText.Pick("The file has been imported. Start the service when you are ready to apply it.", "\u6587\u4ef6\u5df2\u5bfc\u5165\u3002\u51c6\u5907\u597d\u540e\u542f\u52a8\u670d\u52a1\u5373\u53ef\u5e94\u7528\u8be5\u914d\u7f6e\u3002"),
            "Settings.Messages.RestartServiceFailed.Title" => LocalizedText.Pick("Could not restart service", "\u65e0\u6cd5\u91cd\u542f\u670d\u52a1"),
            "Settings.Messages.RestartServiceFailed.Body" => LocalizedText.Pick("The background worker could not be restarted.", "\u540e\u53f0 Worker \u65e0\u6cd5\u91cd\u542f\u3002"),
            "Settings.Messages.LanguageRestart.Title" => LocalizedText.Pick("Restart required", "\u9700\u8981\u91cd\u542f"),
            "Settings.Messages.LanguageRestart.Body" => LocalizedText.Pick("Language changes will apply after restarting Fn Mapping Tool.", "\u91cd\u542f Fn \u6620\u5c04\u5de5\u5177\u540e\uff0c\u8bed\u8a00\u66f4\u6539\u624d\u4f1a\u751f\u6548\u3002"),
            "Keys.Messages.ServiceStopped.Title" => LocalizedText.Pick("Background service is stopped", "\u540e\u53f0\u670d\u52a1\u672a\u8fd0\u884c"),
            "Keys.Messages.ServiceStopped.Body" => LocalizedText.Pick("Start the background service in Settings before capturing a new key.", "\u8bf7\u5148\u5728\u201c\u8bbe\u7f6e\u201d\u4e2d\u542f\u52a8\u540e\u53f0\u670d\u52a1\uff0c\u518d\u6355\u83b7\u65b0\u7684\u6309\u952e\u3002"),
            "Keys.Messages.AddFailed.Title" => LocalizedText.Pick("Could not add key", "\u65e0\u6cd5\u6dfb\u52a0\u6309\u952e"),
            "Keys.Messages.SaveFailed.Title" => LocalizedText.Pick("Could not save key", "\u65e0\u6cd5\u4fdd\u5b58\u6309\u952e"),
            "Keys.Messages.Delete.Title" => LocalizedText.Pick("Delete key", "\u5220\u9664\u6309\u952e"),
            "Keys.Messages.Delete.Body" => LocalizedText.Pick("This will also delete mappings that use the selected key.", "\u8fd9\u4e5f\u4f1a\u5220\u9664\u6240\u6709\u4f7f\u7528\u8be5\u6309\u952e\u7684\u6620\u5c04\u3002"),
            "Mappings.Messages.AddFailed.Title" => LocalizedText.Pick("Could not add mapping", "\u65e0\u6cd5\u6dfb\u52a0\u6620\u5c04"),
            "Mappings.Messages.Delete.Title" => LocalizedText.Pick("Delete mapping", "\u5220\u9664\u6620\u5c04"),
            "Mappings.Messages.Delete.Body" => LocalizedText.Pick("Delete the selected mapping?", "\u8981\u5220\u9664\u5f53\u524d\u9009\u4e2d\u7684\u6620\u5c04\u5417\uff1f"),
            "Mappings.Messages.SaveFailed.Title" => LocalizedText.Pick("Could not save mapping", "\u65e0\u6cd5\u4fdd\u5b58\u6620\u5c04"),
            "Mappings.NoIcon" => LocalizedText.Pick("No icon", "\u65e0\u56fe\u6807"),
            "AddKey.CaptureReady" => LocalizedText.Pick("Ready to capture a hardware key.", "\u5df2\u51c6\u5907\u597d\u6355\u83b7\u786c\u4ef6\u6309\u952e\u3002"),
            "AddKey.CaptureWaiting" => LocalizedText.Pick("Waiting for the next hardware key...", "\u6b63\u5728\u7b49\u5f85\u4e0b\u4e00\u4e2a\u786c\u4ef6\u6309\u952e\u2026\u2026"),
            "CaptureWaitingInlineText" => LocalizedText.Pick("Waiting for the next hardware key...", "\u6b63\u5728\u7b49\u5f85\u4e0b\u4e00\u4e2a\u786c\u4ef6\u6309\u952e\u2026\u2026"),
            "AddKey.CaptureCancelled" => LocalizedText.Pick("Capture was cancelled or timed out.", "\u6355\u83b7\u5df2\u53d6\u6d88\u6216\u8d85\u65f6\u3002"),
            "AddKey.CaptureSuccess" => LocalizedText.Pick("Key captured.", "\u6309\u952e\u5df2\u6355\u83b7\u3002"),
            "AddKey.NameExists" => LocalizedText.Pick("This key name already exists.", "\u8fd9\u4e2a\u6309\u952e\u540d\u79f0\u5df2\u5b58\u5728\u3002"),
            "AppPicker.NoInstalledApps" => LocalizedText.Pick("No installed apps were returned by Get-StartApps.", "Get-StartApps \u6ca1\u6709\u8fd4\u56de\u4efb\u4f55\u5df2\u5b89\u88c5\u5e94\u7528\u3002"),
            "AppPicker.FoundInstalledApps" => LocalizedText.Pick("Found {0} installed apps.", "\u627e\u5230 {0} \u4e2a\u5df2\u5b89\u88c5\u5e94\u7528\u3002"),
            _ => key
        };
    }

    public static string Format(string key, params object[] arguments)
    {
        return string.Format(CultureInfo.CurrentCulture, GetString(key), arguments);
    }
}
