using Microsoft.UI.Xaml;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class ActionDefinitionViewModel : ObservableObject
{
    private string _type;
    private string _target;
    private string _arguments;
    private string _osdTitle;
    private string _osdMessage;
    private double _durationMs;
    private string _osdIconMode;
    private string _osdBuiltInAsset;
    private string _osdIconPath;

    public ActionDefinitionViewModel(ActionDefinitionConfiguration? model = null)
    {
        model ??= new ActionDefinitionConfiguration();
        var osdIcon = model.OsdIcon ?? new IconConfiguration();

        _type = model.Type ?? HotkeyActionType.None;
        _target = model.Target ?? string.Empty;
        _arguments = model.Arguments ?? string.Empty;
        _osdTitle = model.OsdTitle ?? string.Empty;
        _osdMessage = model.OsdMessage ?? string.Empty;
        _durationMs = model.DurationMs ?? RuntimeDefaults.DefaultOsdDurationMs;
        _osdIconMode = osdIcon.Mode ?? IconSourceMode.None;
        _osdBuiltInAsset = osdIcon.BuiltInAsset ?? BuiltInOsdAsset.FnLock;
        _osdIconPath = osdIcon.Path ?? string.Empty;
    }

    public IReadOnlyList<ChoiceOption> IconModes => IconAssetCatalog.IconModes;

    public IReadOnlyList<IconAssetOption> OsdBuiltInAssets => IconAssetCatalog.OsdAssets;

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
        set => SetProperty(ref _target, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    public string OsdTitle
    {
        get => _osdTitle;
        set => SetProperty(ref _osdTitle, value);
    }

    public string OsdMessage
    {
        get => _osdMessage;
        set => SetProperty(ref _osdMessage, value);
    }

    public double DurationMs
    {
        get => _durationMs;
        set => SetProperty(ref _durationMs, Math.Max(500, value));
    }

    public string OsdIconMode
    {
        get => _osdIconMode;
        set
        {
            if (SetProperty(ref _osdIconMode, value))
            {
                OnPropertyChanged(nameof(OsdBuiltInVisibility));
                OnPropertyChanged(nameof(OsdCustomVisibility));
            }
        }
    }

    public string OsdBuiltInAsset
    {
        get => _osdBuiltInAsset;
        set => SetProperty(ref _osdBuiltInAsset, value);
    }

    public string OsdIconPath
    {
        get => _osdIconPath;
        set => SetProperty(ref _osdIconPath, value);
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

    public Visibility OsdBuiltInVisibility => OsdIconMode == IconSourceMode.BuiltIn ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OsdCustomVisibility => OsdIconMode == IconSourceMode.CustomFile ? Visibility.Visible : Visibility.Collapsed;

    public ActionDefinitionConfiguration ToConfiguration()
    {
        return new ActionDefinitionConfiguration
        {
            Type = Type,
            Target = Type == HotkeyActionType.OpenApplication && !string.IsNullOrWhiteSpace(Target) ? Target.Trim() : null,
            Arguments = Type == HotkeyActionType.OpenApplication && !string.IsNullOrWhiteSpace(Arguments) ? Arguments.Trim() : null,
            OsdTitle = Type == HotkeyActionType.ShowOsd && !string.IsNullOrWhiteSpace(OsdTitle) ? OsdTitle.Trim() : null,
            OsdMessage = Type == HotkeyActionType.ShowOsd && !string.IsNullOrWhiteSpace(OsdMessage) ? OsdMessage.Trim() : null,
            DurationMs = Type == HotkeyActionType.ShowOsd ? (int)Math.Round(Math.Max(500, DurationMs)) : null,
            OsdIcon = new IconConfiguration
            {
                Mode = Type == HotkeyActionType.ShowOsd ? OsdIconMode : IconSourceMode.None,
                BuiltInAsset = Type == HotkeyActionType.ShowOsd && OsdIconMode == IconSourceMode.BuiltIn ? OsdBuiltInAsset : null,
                Path = Type == HotkeyActionType.ShowOsd && OsdIconMode == IconSourceMode.CustomFile && !string.IsNullOrWhiteSpace(OsdIconPath) ? OsdIconPath.Trim() : null
            },
            TrayIcon = new IconConfiguration()
        };
    }

    public void ClearAssignment()
    {
        Type = HotkeyActionType.None;
        Target = string.Empty;
        Arguments = string.Empty;
        OsdTitle = string.Empty;
        OsdMessage = string.Empty;
        OsdIconMode = IconSourceMode.None;
        OsdBuiltInAsset = BuiltInOsdAsset.FnLock;
        OsdIconPath = string.Empty;
    }
}
