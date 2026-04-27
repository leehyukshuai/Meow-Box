using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MeowBox.Controller.Services;

public static class XamlStringLocalizer
{
    private static readonly object SyncRoot = new();
    private static IReadOnlyDictionary<string, string>? _englishResources;
    private static IReadOnlyDictionary<string, string>? _chineseResources;

    public static void Apply(object root)
    {
        if (root is null)
        {
            return;
        }

        var resources = GetResourcesForCurrentLanguage();
        if (resources.Count == 0)
        {
            return;
        }

        foreach (var instance in EnumerateObjects(root))
        {
            ApplyToInstance(instance, resources);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetResourcesForCurrentLanguage()
    {
        var resources = GetFlatResourcesForCurrentLanguage();
        var grouped = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var pair in resources)
        {
            var separatorIndex = pair.Key.IndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= pair.Key.Length - 1)
            {
                continue;
            }

            var uid = pair.Key[..separatorIndex];
            var propertyName = pair.Key[(separatorIndex + 1)..];
            if (!grouped.TryGetValue(uid, out var properties))
            {
                properties = new Dictionary<string, string>(StringComparer.Ordinal);
                grouped[uid] = properties;
            }

            properties[propertyName] = pair.Value;
        }

        return grouped.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)pair.Value,
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> GetFlatResourcesForCurrentLanguage()
    {
        var useChinese = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        lock (SyncRoot)
        {
            if (useChinese)
            {
                _chineseResources ??= LoadFlatResources(AppLanguageService.ChineseTag);
                return _chineseResources;
            }

            _englishResources ??= LoadFlatResources(AppLanguageService.EnglishTag);
            return _englishResources;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadFlatResources(string languageTag)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Strings", languageTag, "Resources.resw");
        if (!File.Exists(path))
        {
            if (!string.Equals(languageTag, AppLanguageService.EnglishTag, StringComparison.OrdinalIgnoreCase))
            {
                return LoadFlatResources(AppLanguageService.EnglishTag);
            }

            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        var document = XDocument.Load(path);
        foreach (var data in document.Root?.Elements("data") ?? [])
        {
            var name = data.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            resources[name] = data.Element("value")?.Value ?? string.Empty;
        }

        return resources;
    }

    private static IEnumerable<object> EnumerateObjects(object root)
    {
        var queue = new Queue<object>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            if (current is DependencyObject dependencyObject)
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(dependencyObject); index++)
                {
                    Enqueue(VisualTreeHelper.GetChild(dependencyObject, index));
                }
            }

            switch (current)
            {
                case ContentDialog dialog when dialog.Content is not null:
                    Enqueue(dialog.Content);
                    break;
                case ContentControl contentControl when contentControl.Content is not null:
                    Enqueue(contentControl.Content);
                    break;
                case ItemsControl itemsControl:
                    foreach (var item in itemsControl.Items)
                    {
                        if (item is not null)
                        {
                            Enqueue(item);
                        }
                    }
                    break;
            }
        }

        void Enqueue(object? candidate)
        {
            if (candidate is null || !seen.Add(candidate))
            {
                return;
            }

            queue.Enqueue(candidate);
        }
    }

    private static void ApplyToInstance(
        object instance,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources)
    {
        var uid = instance.GetType().GetProperty("Uid", BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance) as string;
        if (string.IsNullOrEmpty(uid) || !resources.TryGetValue(uid, out var uidProperties))
        {
            return;
        }

        foreach (var property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            if (property.PropertyType != typeof(string) && property.PropertyType != typeof(object))
            {
                continue;
            }

            if (uidProperties.TryGetValue(property.Name, out var value))
            {
                property.SetValue(instance, value);
            }
        }
    }
}
