using Microsoft.UI.Xaml;
using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Controller.ViewModels;

public sealed class MappingOsdViewModel : ObservableObject
{
    private bool _enabled;
    private string _title;
    private string _iconPath;

    public MappingOsdViewModel(MappingOsdConfiguration? model = null)
    {
        model ??= new MappingOsdConfiguration();
        _enabled = model.Enabled;
        _title = model.Title ?? string.Empty;
        _iconPath = ResolveInitialIconPath(model.Icon);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                OnPropertyChanged(nameof(EditorVisibility));
            }
        }
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    public string IconPath
    {
        get => _iconPath;
        set => SetProperty(ref _iconPath, value ?? string.Empty);
    }

    public Visibility EditorVisibility => Enabled ? Visibility.Visible : Visibility.Collapsed;

    public MappingOsdConfiguration ToConfiguration()
    {
        var iconPath = !string.IsNullOrWhiteSpace(IconPath)
            ? IconPath.Trim()
            : null;

        return new MappingOsdConfiguration
        {
            Enabled = Enabled,
            Title = !string.IsNullOrWhiteSpace(Title) ? Title.Trim() : null,
            Icon = new IconConfiguration
            {
                Mode = string.IsNullOrWhiteSpace(iconPath) ? IconSourceMode.None : IconSourceMode.CustomFile,
                Path = iconPath
            }
        };
    }

    public void EnsureDefaultTitle(string fallbackTitle)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Title = fallbackTitle;
        }
    }

    private static string ResolveInitialIconPath(IconConfiguration icon)
    {
        return !string.IsNullOrWhiteSpace(icon.Path) &&
               string.Equals(Path.GetExtension(icon.Path), ".png", StringComparison.OrdinalIgnoreCase)
            ? icon.Path
            : string.Empty;
    }
}
