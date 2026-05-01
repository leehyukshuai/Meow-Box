using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace MeowBox.Controller.Services;

internal static class SvgAssetTintService
{
    private const string TintMarker = "data-meow-tint=\"cat\"";

    public static async Task<SvgImageSource> CreateTintedImageSourceAsync(string fileName, string fillColor)
    {
        var svg = ApplyFill(await File.ReadAllTextAsync(GetAssetPath(fileName)), fillColor);
        var source = new SvgImageSource();

        using var stream = new InMemoryRandomAccessStream();
        using (var output = stream.GetOutputStreamAt(0))
        {
            using var writer = new DataWriter(output)
            {
                UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8
            };
            writer.WriteString(svg);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        await source.SetSourceAsync(stream);
        return source;
    }

    private static string GetAssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "assets", "ui", fileName);
    }

    private static string ApplyFill(string svg, string fillColor)
    {
        var markerIndex = svg.IndexOf(TintMarker, StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return ApplyFillToTag(svg, markerIndex, fillColor);
        }

        var rootIndex = svg.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        return rootIndex >= 0
            ? ApplyFillToTag(svg, rootIndex, fillColor)
            : svg;
    }

    private static string ApplyFillToTag(string svg, int indexInTag, string fillColor)
    {
        var tagStart = svg.LastIndexOf('<', indexInTag);
        var tagEnd = svg.IndexOf('>', indexInTag);
        if (tagStart < 0 || tagEnd < 0)
        {
            return svg;
        }

        const string fillAttribute = "fill=\"";
        var fillIndex = svg.IndexOf(fillAttribute, tagStart, tagEnd - tagStart, StringComparison.OrdinalIgnoreCase);
        if (fillIndex < 0)
        {
            return svg.Insert(tagEnd, $" fill=\"{fillColor}\"");
        }

        fillIndex += fillAttribute.Length;
        var fillEnd = svg.IndexOf('"', fillIndex);
        if (fillEnd < 0 || fillEnd > tagEnd)
        {
            return svg;
        }

        return svg[..fillIndex] + fillColor + svg[fillEnd..];
    }
}
