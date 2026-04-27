using System.Globalization;
using MeowBox.Core.Models;

namespace MeowBox.Core.Services;

public static class AppLanguageService
{
    public const string EnglishTag = "en-US";
    public const string ChineseTag = "zh-CN";

    public static string ResolveStoredPreference(string? value)
    {
        return value switch
        {
            AppLanguagePreference.English => AppLanguagePreference.English,
            AppLanguagePreference.Chinese => AppLanguagePreference.Chinese,
            _ => AppLanguagePreference.System
        };
    }

    public static string ResolveEffectiveLanguageTag(string? value)
    {
        return ResolveStoredPreference(value) switch
        {
            AppLanguagePreference.Chinese => ChineseTag,
            AppLanguagePreference.English => EnglishTag,
            _ => ResolveSystemLanguageTag()
        };
    }

    public static void Apply(string? value)
    {
        var languageTag = ResolveEffectiveLanguageTag(value);
        var culture = new CultureInfo(languageTag);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static string ResolveSystemLanguageTag()
    {
        var systemLanguage = CultureInfo.InstalledUICulture.Name;
        if (systemLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseTag;
        }

        if (systemLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return EnglishTag;
        }

        return EnglishTag;
    }
}
