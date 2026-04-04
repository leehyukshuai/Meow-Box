using Microsoft.UI.Xaml;
using FnMappingTool.Core.Models;
using FnMappingTool.Core.Services;

namespace FnMappingTool.Controller.ViewModels;

public sealed class ActionDefinitionViewModel : ObservableObject
{
    private string _type;
    private string _target;
    private string _arguments;
    private string _osdTitle;
    private string _osdIconPath;

    public ActionDefinitionViewModel(ActionDefinitionConfiguration? model = null)
    {
        model ??= new ActionDefinitionConfiguration();
        var osdIcon = model.OsdIcon ?? new IconConfiguration();

        _type = model.Type ?? HotkeyActionType.None;
        _target = model.Target ?? string.Empty;
        _arguments = model.Arguments ?? string.Empty;
        _osdTitle = model.OsdTitle ?? string.Empty;
        _osdIconPath = ResolveInitialIconPath(osdIcon);
    }

    public string Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(ActionLabel));
                OnPropertyChanged(nameof(ActionDescription));
                OnPropertyChanged(nameof(ActionIconGlyph));
                OnPropertyChanged(nameof(ActionTagsText));
                OnPropertyChanged(nameof(TargetVisibility));
                OnPropertyChanged(nameof(ArgumentsVisibility));
                OnPropertyChanged(nameof(InstalledAppPickerVisibility));
                OnPropertyChanged(nameof(OsdEditorVisibility));
            }
        }
    }

    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value ?? string.Empty);
    }

    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value ?? string.Empty);
    }

    public string OsdTitle
    {
        get => _osdTitle;
        set => SetProperty(ref _osdTitle, value ?? string.Empty);
    }

    public string OsdIconPath
    {
        get => _osdIconPath;
        set => SetProperty(ref _osdIconPath, value ?? string.Empty);
    }

    public string ActionLabel => ActionCatalog.GetLabel(Type);

    public string ActionDescription => ActionCatalog.GetDescription(Type);

    public string ActionIconGlyph => ActionCatalog.GetIconGlyph(Type);

    public string ActionTagsText => ActionCatalog.GetTagsText(Type);

    public bool HasAssignedAction => !string.IsNullOrWhiteSpace(Type);

    public Visibility TargetVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ArgumentsVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InstalledAppPickerVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OsdEditorVisibility => Type == HotkeyActionType.ShowOsd ? Visibility.Visible : Visibility.Collapsed;

    public ActionDefinitionConfiguration ToConfiguration()
    {
        var iconPath = Type == HotkeyActionType.ShowOsd && !string.IsNullOrWhiteSpace(OsdIconPath)
            ? OsdIconPath.Trim()
            : null;

        return new ActionDefinitionConfiguration
        {
            Type = Type,
            Target = Type == HotkeyActionType.OpenApplication && !string.IsNullOrWhiteSpace(Target) ? Target.Trim() : null,
            Arguments = Type == HotkeyActionType.OpenApplication && !string.IsNullOrWhiteSpace(Arguments) ? Arguments.Trim() : null,
            OsdTitle = Type == HotkeyActionType.ShowOsd && !string.IsNullOrWhiteSpace(OsdTitle) ? OsdTitle.Trim() : null,
            OsdIcon = new IconConfiguration
            {
                Mode = string.IsNullOrWhiteSpace(iconPath) ? IconSourceMode.None : IconSourceMode.CustomFile,
                Path = iconPath
            }
        };
    }

    public void ClearAssignment()
    {
        Type = HotkeyActionType.None;
        Target = string.Empty;
        Arguments = string.Empty;
        OsdTitle = string.Empty;
        OsdIconPath = string.Empty;
    }

    private static string ResolveInitialIconPath(IconConfiguration icon)
    {
        return ResolvePreferredPngPath(icon.Path);
    }

    private static string ResolvePreferredPngPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase)
            ? path
            : string.Empty;
    }
}
