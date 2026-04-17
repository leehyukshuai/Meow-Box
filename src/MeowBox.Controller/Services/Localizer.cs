using System.Globalization;
using MeowBox.Core.Models;

namespace MeowBox.Controller.Services;

public static class Localizer
{
    public static string GetString(string key)
    {
        return key switch
        {
            "App.Title" => LocalizedText.Pick("Meow Box", "Meow Box"),
            "Navigation.Mappings" => LocalizedText.Pick("Keyboard", "\u952e\u76d8"),
            "Navigation.Touchpad" => LocalizedText.Pick("Touchpad", "\u89e6\u63a7\u677f"),
            "Navigation.Settings" => LocalizedText.Pick("Settings", "\u8bbe\u7f6e"),
            "PageTitle.Mappings" => LocalizedText.Pick("Keyboard", "\u952e\u76d8"),
            "PageTitle.Touchpad" => LocalizedText.Pick("Touchpad", "\u89e6\u63a7\u677f"),
            "PageTitle.Settings" => LocalizedText.Pick("Settings", "\u8bbe\u7f6e"),
            "ServiceStatus.Running" => LocalizedText.Pick("Service running", "\u670d\u52a1\u8fd0\u884c\u4e2d"),
            "ServiceStatus.Stopped" => LocalizedText.Pick("Service stopped", "\u670d\u52a1\u672a\u8fd0\u884c"),
            "QuickService.Start" => LocalizedText.Pick("Start service", "\u542f\u52a8\u670d\u52a1"),
            "QuickService.Stop" => LocalizedText.Pick("Stop service", "\u505c\u6b62\u670d\u52a1"),
            "Dialog.Close" => LocalizedText.Pick("Close", "\u5173\u95ed"),
            "Dialog.Cancel" => LocalizedText.Pick("Cancel", "\u53d6\u6d88"),
            "Dialog.Delete" => LocalizedText.Pick("Delete", "\u5220\u9664"),
            "Dialog.Later" => LocalizedText.Pick("Later", "\u7a0d\u540e"),
            "Dialog.RestoreDefaults" => LocalizedText.Pick("Restore defaults", "\u6062\u590d\u9ed8\u8ba4"),
            "Dialog.RestartNow" => LocalizedText.Pick("Restart now", "\u7acb\u5373\u91cd\u542f"),
            "Dialog.RestartService" => LocalizedText.Pick("Restart service", "\u91cd\u542f\u670d\u52a1"),
            "Settings.Messages.StartServiceFailed.Title" => LocalizedText.Pick("Could not start service", "\u65e0\u6cd5\u542f\u52a8\u670d\u52a1"),
            "Settings.Messages.StartServiceFailed.Body" => LocalizedText.Pick("The background worker could not be started or did not respond.", "\u540e\u53f0 Worker \u65e0\u6cd5\u542f\u52a8\uff0c\u6216\u542f\u52a8\u540e\u6ca1\u6709\u54cd\u5e94\u3002"),
            "Settings.Messages.AutostartFailed.Title" => LocalizedText.Pick("Could not change startup behavior", "\u65e0\u6cd5\u4fee\u6539\u5f00\u673a\u542f\u52a8\u8bbe\u7f6e"),
            "Settings.Messages.PriorityFailed.Title" => LocalizedText.Pick("Could not change startup priority", "\u65e0\u6cd5\u4fee\u6539\u4f18\u5148\u542f\u52a8\u8bbe\u7f6e"),
            "Settings.Messages.RestoreDefaults.Title" => LocalizedText.Pick("Restore defaults?", "\u6062\u590d\u9ed8\u8ba4\uff1f"),
            "Settings.Messages.RestoreDefaults.Body" => LocalizedText.Pick("This will replace the current configuration with the default settings.", "\u8fd9\u4f1a\u7528\u9ed8\u8ba4\u8bbe\u7f6e\u66ff\u6362\u5f53\u524d\u914d\u7f6e\u3002"),
            "Settings.Messages.RestoreDefaultsFailed.Title" => LocalizedText.Pick("Could not restore defaults", "\u65e0\u6cd5\u6062\u590d\u9ed8\u8ba4\u8bbe\u7f6e"),
            "Settings.Messages.LanguageRestart.Title" => LocalizedText.Pick("Restart required", "\u9700\u8981\u91cd\u542f"),
            "Settings.Messages.LanguageRestart.Body" => LocalizedText.Pick("Language changes will apply after restarting Meow Box.", "\u91cd\u542f Meow Box\u540e\uff0c\u8bed\u8a00\u66f4\u6539\u624d\u4f1a\u751f\u6548\u3002"),
            "Mappings.Messages.SaveFailed.Title" => LocalizedText.Pick("Could not save mapping", "\u65e0\u6cd5\u4fdd\u5b58\u6620\u5c04"),
            "Mappings.NoIcon" => LocalizedText.Pick("No icon", "\u65e0\u56fe\u6807"),
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
