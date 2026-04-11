using Microsoft.UI.Xaml;
using FnMappingTool.Core.Models;
using FnMappingTool.Core.Services;

namespace FnMappingTool.Controller.ViewModels;

public sealed class ActionDefinitionViewModel : ObservableObject
{
    private string _type;
    private string _standardKey;
    private string _standardKeyGroup;
    private string _target;
    private string _arguments;

    public ActionDefinitionViewModel(ActionDefinitionConfiguration? model = null)
    {
        model ??= new ActionDefinitionConfiguration();

        _type = model.Type ?? HotkeyActionType.None;
        _standardKey = StandardKeyCatalog.NormalizeKey(model.StandardKey);
        _standardKeyGroup = StandardKeyCatalog.GetPreferredGroupKey(_standardKey);
        _target = model.Target ?? string.Empty;
        _arguments = model.Arguments ?? string.Empty;
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
                OnPropertyChanged(nameof(StandardKeyEditorVisibility));
                OnPropertyChanged(nameof(HasAssignedAction));
            }
        }
    }

    public string StandardKey
    {
        get => _standardKey;
        set
        {
            var normalizedValue = StandardKeyCatalog.NormalizeKey(value);
            if (!SetProperty(ref _standardKey, normalizedValue))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(normalizedValue))
            {
                var preferredGroup = StandardKeyCatalog.GetPreferredGroupKey(normalizedValue);
                if (!string.Equals(_standardKeyGroup, preferredGroup, StringComparison.OrdinalIgnoreCase))
                {
                    _standardKeyGroup = preferredGroup;
                    OnPropertyChanged(nameof(StandardKeyGroup));
                }
            }

            OnPropertyChanged(nameof(StandardKeyLabel));
            OnPropertyChanged(nameof(ActionDescription));
        }
    }

    public string StandardKeyGroup
    {
        get => _standardKeyGroup;
        set
        {
            var normalizedValue = StandardKeyCatalog.NormalizeGroupKey(value);
            if (!SetProperty(ref _standardKeyGroup, normalizedValue))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(StandardKey) &&
                !StandardKeyCatalog.MatchesGroup(StandardKey, normalizedValue))
            {
                StandardKey = string.Empty;
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

    public string ActionLabel => ActionCatalog.GetLabel(Type);

    public string ActionDescription => BuildActionDescription();

    public string ActionIconGlyph => ActionCatalog.GetIconGlyph(Type);

    public string ActionTagsText => ActionCatalog.GetTagsText(Type);

    public string StandardKeyLabel => StandardKeyCatalog.GetLabel(StandardKey);

    public bool HasAssignedAction => !string.IsNullOrWhiteSpace(Type);

    public Visibility TargetVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ArgumentsVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InstalledAppPickerVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StandardKeyEditorVisibility => Type == HotkeyActionType.SendStandardKey ? Visibility.Visible : Visibility.Collapsed;

    public ActionDefinitionConfiguration ToConfiguration()
    {
        return new ActionDefinitionConfiguration
        {
            Type = Type,
            StandardKey = Type == HotkeyActionType.SendStandardKey && !string.IsNullOrWhiteSpace(StandardKey) ? StandardKey : null,
            Target = Type == HotkeyActionType.OpenApplication && !string.IsNullOrWhiteSpace(Target) ? Target.Trim() : null,
            Arguments = Type == HotkeyActionType.OpenApplication && !string.IsNullOrWhiteSpace(Arguments) ? Arguments.Trim() : null,
            OsdIcon = new IconConfiguration()
        };
    }

    public void ClearAssignment()
    {
        Type = HotkeyActionType.None;
        StandardKey = string.Empty;
        StandardKeyGroup = StandardKeyCatalog.GroupOptions[0].Key;
        Target = string.Empty;
        Arguments = string.Empty;
    }

    private string BuildActionDescription()
    {
        if (Type == HotkeyActionType.SendStandardKey)
        {
            return string.IsNullOrWhiteSpace(StandardKey)
                ? LocalizedText.Pick("Sends a standard keyboard or media key that you choose below.", "发送你在下方选择的标准键盘按键或媒体按键。")
                : string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    LocalizedText.Pick("Sends the standard key {0}.", "发送标准按键 {0}。"),
                    StandardKeyLabel);
        }

        return ActionCatalog.GetDescription(Type);
    }
}
