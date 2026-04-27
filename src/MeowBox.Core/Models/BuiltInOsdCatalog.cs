using MeowBox.Core.Services;

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
            DefaultKeyIds.MicrophoneMuteOn => new BuiltInOsdDefinition(GetString("Osd.Title.MicrophoneOff", "Microphone off"), BuiltInOsdAsset.MicrophoneMute),
            DefaultKeyIds.MicrophoneMuteOff => new BuiltInOsdDefinition(GetString("Osd.Title.MicrophoneOn", "Microphone on"), BuiltInOsdAsset.MicrophoneOn),
            DefaultKeyIds.BacklightCycle => ResolveBacklight(reportHex),
            _ => null
        };
    }

    private static BuiltInOsdDefinition ResolveFnLock(string? reportHex)
    {
        return NormalizeHex(reportHex) switch
        {
            var value when value.StartsWith("010701", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(GetString("Osd.Title.FnLockOn", "Fn lock on"), BuiltInOsdAsset.FnLock),
            var value when value.StartsWith("010700", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(GetString("Osd.Title.FnLockOff", "Fn lock off"), BuiltInOsdAsset.FnUnlock),
            _ => new BuiltInOsdDefinition(GetString("Osd.Title.FnLock", "Fn lock"), BuiltInOsdAsset.FnLock)
        };
    }

    private static BuiltInOsdDefinition ResolveCapsLock(string? reportHex)
    {
        return NormalizeHex(reportHex) switch
        {
            var value when value.StartsWith("010901", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(GetString("Osd.Title.CapsLockOn", "Caps lock on"), BuiltInOsdAsset.CapsLock),
            var value when value.StartsWith("010900", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(GetString("Osd.Title.CapsLockOff", "Caps lock off"), BuiltInOsdAsset.CapsUnlock),
            _ => new BuiltInOsdDefinition(GetString("Osd.Title.CapsLock", "Caps lock"), BuiltInOsdAsset.CapsLock)
        };
    }

    private static BuiltInOsdDefinition? ResolveBacklight(string? reportHex)
    {
        return NormalizeHex(reportHex) switch
        {
            var value when value.StartsWith("010500", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(GetString("Osd.Title.BacklightOff", "Backlight off"), BuiltInOsdAsset.BacklightOff),
            var value when value.StartsWith("010505", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(GetString("Osd.Title.BacklightLow", "Backlight low"), BuiltInOsdAsset.BacklightLow),
            var value when value.StartsWith("01050A", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(GetString("Osd.Title.BacklightHigh", "Backlight high"), BuiltInOsdAsset.BacklightHigh),
            var value when value.StartsWith("010580", StringComparison.OrdinalIgnoreCase)
                => new BuiltInOsdDefinition(GetString("Osd.Title.BacklightAuto", "Backlight auto"), BuiltInOsdAsset.BacklightAuto),
            _ => new BuiltInOsdDefinition(GetString("Osd.Title.KeyboardBacklight", "Keyboard backlight"), BuiltInOsdAsset.BacklightAuto)
        };
    }

    private static string GetString(string key, string fallback)
    {
        return ResourceStringService.GetString(key, fallback);
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
