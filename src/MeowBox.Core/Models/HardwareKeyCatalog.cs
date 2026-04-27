using MeowBox.Core.Services;
namespace MeowBox.Core.Models;

public static class HardwareKeyCatalog
{
    public static string GetLabel(string? keyId, string? fallbackName = null)
    {
        return keyId switch
        {
            DefaultKeyIds.PerformanceModePress => ResourceStringService.GetString("HardwareKey.PerformanceMode", "Performance mode key"),
            DefaultKeyIds.FnLockToggle => ResourceStringService.GetString("HardwareKey.FnLock", "Fn Lock key"),
            DefaultKeyIds.CapsLockToggle => ResourceStringService.GetString("HardwareKey.CapsLock", "Caps Lock key"),
            DefaultKeyIds.MicrophoneMuteOn => ResourceStringService.GetString("HardwareKey.MicMuted", "Microphone · Muted"),
            DefaultKeyIds.MicrophoneMuteOff => ResourceStringService.GetString("HardwareKey.MicLive", "Microphone · Live"),
            DefaultKeyIds.XiaoAiPress => ResourceStringService.GetString("HardwareKey.XiaoAi", "XiaoAi key"),
            DefaultKeyIds.SettingsPress => ResourceStringService.GetString("HardwareKey.Settings", "Settings key"),
            DefaultKeyIds.ManagerPress => ResourceStringService.GetString("HardwareKey.Manager", "PC Manager key"),
            DefaultKeyIds.BacklightCycle => ResourceStringService.GetString("HardwareKey.Backlight", "Keyboard backlight key"),
            DefaultKeyIds.Projection => ResourceStringService.GetString("HardwareKey.Projection", "Projection key"),
            _ => string.IsNullOrWhiteSpace(fallbackName)
                ? ResourceStringService.GetString("HardwareKey.Unnamed", "Unnamed key")
                : fallbackName.Trim()
        };
    }

    public static string GetDescription(string? keyId)
    {
        return keyId switch
        {
            DefaultKeyIds.PerformanceModePress => ResourceStringService.GetString("HardwareKey.Desc.PerformanceMode", "Press the performance mode key (Fn + K). This entry runs on key press."),
            DefaultKeyIds.FnLockToggle => ResourceStringService.GetString("HardwareKey.Desc.FnLock", "Press Fn + Esc to toggle Fn Lock. This entry runs whenever the firmware reports the new Fn Lock state."),
            DefaultKeyIds.CapsLockToggle => ResourceStringService.GetString("HardwareKey.Desc.CapsLock", "Press Caps Lock to toggle capitalization. This entry runs whenever the firmware reports the new Caps Lock state."),
            DefaultKeyIds.MicrophoneMuteOn => ResourceStringService.GetString("HardwareKey.Desc.MicMutedOn", "Press the F4 microphone key to toggle mute. This entry runs when microphone mute switches to On."),
            DefaultKeyIds.MicrophoneMuteOff => ResourceStringService.GetString("HardwareKey.Desc.MicMutedOff", "Press the F4 microphone key to toggle mute. This entry runs when microphone mute switches to Off."),
            DefaultKeyIds.XiaoAiPress => ResourceStringService.GetString("HardwareKey.Desc.XiaoAi", "Press the XiaoAi key (F7). This entry runs on key press."),
            DefaultKeyIds.SettingsPress => ResourceStringService.GetString("HardwareKey.Desc.Settings", "Press the Settings key (F9). This entry runs on key press."),
            DefaultKeyIds.ManagerPress => ResourceStringService.GetString("HardwareKey.Desc.Manager", "Press the hard top-right PC Manager key. This entry runs on key press."),
            DefaultKeyIds.BacklightCycle => ResourceStringService.GetString("HardwareKey.Desc.Backlight", "Press the keyboard backlight key to cycle lighting modes. This entry runs whenever the firmware reports the new backlight state."),
            DefaultKeyIds.Projection => ResourceStringService.GetString("HardwareKey.Desc.Projection", "Press the projection key (F8). This entry runs on key press."),
            _ => ResourceStringService.GetString("HardwareKey.Desc.Default", "This hardware key is provided by the device firmware. You can change what it does here.")
        };
    }
}
