using System.Globalization;

namespace FnMappingTool.Core.Models;

public static class LocalizedText
{
    public static bool IsChinese =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

    public static string Pick(string english, string chinese)
    {
        return IsChinese ? chinese : english;
    }
}
