namespace MeowBox.Core.Models;

public readonly record struct BuiltInOsdDefinition(string Title, string? AssetKey);

public static class BuiltInOsdCatalog
{
    public static bool SupportsToggle(string? keyId, string? actionType)
    {
        return SupportsKey(keyId);
    }

    public static bool SupportsKey(string? keyId)
    {
        return keyId switch
        {
            DefaultKeyIds.FnLockToggle => true,
            DefaultKeyIds.CapsLockToggle => true,
            DefaultKeyIds.MicrophoneMuteOn => true,
            DefaultKeyIds.MicrophoneMuteOff => true,
            DefaultKeyIds.BacklightCycle => true,
            _ => false
        };
    }

    public static BuiltInOsdDefinition? ResolveForKey(string? keyId, string? reportHex = null)
    {
        return keyId switch
        {
            DefaultKeyIds.FnLockToggle => ResolveFnLock(reportHex),
            DefaultKeyIds.CapsLockToggle => ResolveCapsLock(reportHex),
            DefaultKeyIds.MicrophoneMuteOn => new BuiltInOsdDefinition(LocalizedText.Pick("Microphone off", "麦克风已关闭"), BuiltInOsdAsset.MicrophoneMute),
            DefaultKeyIds.MicrophoneMuteOff => new BuiltInOsdDefinition(LocalizedText.Pick("Microphone on", "麦克风已开启"), BuiltInOsdAsset.MicrophoneOn),
            DefaultKeyIds.BacklightCycle => ResolveBacklight(reportHex),
            _ => null
        };
    }

    private static BuiltInOsdDefinition ResolveFnLock(string? reportHex)
    {
        return NormalizeHex(reportHex) switch
        {
            var value when value.StartsWith("010701", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(LocalizedText.Pick("Fn lock on", "Fn 锁定已开启"), BuiltInOsdAsset.FnLock),
            var value when value.StartsWith("010700", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(LocalizedText.Pick("Fn lock off", "Fn 锁定已关闭"), BuiltInOsdAsset.FnUnlock),
            _ => new BuiltInOsdDefinition(LocalizedText.Pick("Fn lock", "Fn 锁定"), BuiltInOsdAsset.FnLock)
        };
    }

    private static BuiltInOsdDefinition ResolveCapsLock(string? reportHex)
    {
        return NormalizeHex(reportHex) switch
        {
            var value when value.StartsWith("010901", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(LocalizedText.Pick("Caps lock on", "大写锁定已开启"), BuiltInOsdAsset.CapsLock),
            var value when value.StartsWith("010900", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(LocalizedText.Pick("Caps lock off", "大写锁定已关闭"), BuiltInOsdAsset.CapsUnlock),
            _ => new BuiltInOsdDefinition(LocalizedText.Pick("Caps lock", "大写锁定"), BuiltInOsdAsset.CapsLock)
        };
    }

    private static BuiltInOsdDefinition? ResolveBacklight(string? reportHex)
    {
        return NormalizeHex(reportHex) switch
        {
            var value when value.StartsWith("010500", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(LocalizedText.Pick("Backlight off", "键盘背光已关闭"), BuiltInOsdAsset.BacklightOff),
            var value when value.StartsWith("010505", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(LocalizedText.Pick("Backlight low", "键盘背光低档"), BuiltInOsdAsset.BacklightLow),
            var value when value.StartsWith("01050A", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(LocalizedText.Pick("Backlight high", "键盘背光高档"), BuiltInOsdAsset.BacklightHigh),
            var value when value.StartsWith("010580", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(LocalizedText.Pick("Backlight auto", "键盘背光自动"), BuiltInOsdAsset.BacklightAuto),
            _ => new BuiltInOsdDefinition(LocalizedText.Pick("Keyboard backlight", "键盘背光"), BuiltInOsdAsset.BacklightAuto)
        };
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var index = 0;
        foreach (var character in value.ToUpperInvariant())
        {
            if ((character >= '0' && character <= '9') ||
                (character >= 'A' && character <= 'F'))
            {
                buffer[index++] = character;
            }
        }

        return new string(buffer, 0, index);
    }
}
