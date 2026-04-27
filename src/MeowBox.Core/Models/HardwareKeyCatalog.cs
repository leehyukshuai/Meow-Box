namespace MeowBox.Core.Models;

public static class HardwareKeyCatalog
{
    public static string GetLabel(string? keyId, string? fallbackName = null)
    {
        return keyId switch
        {
            DefaultKeyIds.PerformanceModePress => LocalizedText.Pick("Performance mode key", "性能模式键"),
            DefaultKeyIds.FnLockToggle => LocalizedText.Pick("Fn Lock key", "Fn Lock 键"),
            DefaultKeyIds.CapsLockToggle => LocalizedText.Pick("Caps Lock key", "Caps Lock 键"),
            DefaultKeyIds.MicrophoneMuteOn => LocalizedText.Pick("Microphone · Muted", "麦克风 · 静音"),
            DefaultKeyIds.MicrophoneMuteOff => LocalizedText.Pick("Microphone · Live", "麦克风 · 开启"),
            DefaultKeyIds.XiaoAiPress => LocalizedText.Pick("XiaoAi key", "小爱键"),
            DefaultKeyIds.SettingsPress => LocalizedText.Pick("Settings key", "设置键"),
            DefaultKeyIds.ManagerPress => LocalizedText.Pick("PC Manager key", "电脑管家键"),
            DefaultKeyIds.BacklightCycle => LocalizedText.Pick("Keyboard backlight key", "键盘背光键"),
            DefaultKeyIds.Projection => LocalizedText.Pick("Projection key", "投影键"),
            _ => string.IsNullOrWhiteSpace(fallbackName)
                ? LocalizedText.Pick("Unnamed key", "未命名按键")
                : fallbackName.Trim()
        };
    }

    public static string GetDescription(string? keyId)
    {
        return keyId switch
        {
            DefaultKeyIds.PerformanceModePress => LocalizedText.Pick("Press the performance mode key (Fn + K). This entry runs on key press.", "按下性能模式键（Fn+K）。这个条目会在按下时触发。"),
            DefaultKeyIds.FnLockToggle => LocalizedText.Pick("Press Fn + Esc to toggle Fn Lock. This entry runs whenever the firmware reports the new Fn Lock state.", "同时按下 Fn + Esc 可切换 Fn Lock。这个条目会在固件上报新的 Fn Lock 状态时触发。"),
            DefaultKeyIds.CapsLockToggle => LocalizedText.Pick("Press Caps Lock to toggle capitalization. This entry runs whenever the firmware reports the new Caps Lock state.", "按下 Caps Lock 可切换大写锁定。这个条目会在固件上报新的 Caps Lock 状态时触发。"),
            DefaultKeyIds.MicrophoneMuteOn => LocalizedText.Pick("Press the F4 microphone key to toggle mute. This entry runs when microphone mute switches to On.", "按下 F4 的麦克风功能键可切换静音。这个条目对应麦克风静音切换到开启时。"),
            DefaultKeyIds.MicrophoneMuteOff => LocalizedText.Pick("Press the F4 microphone key to toggle mute. This entry runs when microphone mute switches to Off.", "按下 F4 的麦克风功能键可切换静音。这个条目对应麦克风静音切换到关闭时。"),
            DefaultKeyIds.XiaoAiPress => LocalizedText.Pick("Press the XiaoAi key (F7). This entry runs on key press.", "按下小爱键（F7）。这个条目会在按下时触发。"),
            DefaultKeyIds.SettingsPress => LocalizedText.Pick("Press the Settings key (F9). This entry runs on key press.", "按下设置键（F9）。这个条目会在按下时触发。"),
            DefaultKeyIds.ManagerPress => LocalizedText.Pick("Press the hard top-right PC Manager key. This entry runs on key press.", "按下右上角的硬质电脑管家键。这个条目会在按下时触发。"),
            DefaultKeyIds.BacklightCycle => LocalizedText.Pick("Press the keyboard backlight key to cycle lighting modes. This entry runs whenever the firmware reports the new backlight state.", "按下键盘背光键可轮流切换模式。这个条目会在固件上报新的背光状态时触发。"),
            DefaultKeyIds.Projection => LocalizedText.Pick("Press the projection key (F8). This entry runs on key press.", "按下投影键（F8）。这个条目会在按下时触发。"),
            _ => LocalizedText.Pick("This hardware key is provided by the device firmware. You can change what it does here.", "这个硬件按键由设备固件提供，你可以在这里修改它触发后的动作。")
        };
    }
}
