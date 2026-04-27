using Microsoft.UI.Xaml;
using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Controller.ViewModels;

public sealed class ActionDefinitionViewModel : ObservableObject
{
    private string _type;
    private string _primaryKey;
    private string _primaryKeyGroup;
    private List<string> _modifierKeys;
    private string _target;
    private string _arguments;

    public ActionDefinitionViewModel(ActionDefinitionConfiguration? model = null)
    {
        model ??= new ActionDefinitionConfiguration();

        var normalizedChord = StandardKeyCatalog.NormalizeChord(model.KeyChord);
        _type = model.Type ?? HotkeyActionType.None;
        _primaryKey = StandardKeyCatalog.NormalizeKey(normalizedChord?.PrimaryKey);
        _primaryKeyGroup = StandardKeyCatalog.GetPreferredGroupKey(_primaryKey);
        _modifierKeys = [.. StandardKeyCatalog.NormalizeModifierKeys(normalizedChord?.Modifiers)];
        _target = model.Target ?? string.Empty;
        _arguments = model.Arguments ?? string.Empty;
        KeyChordEditor = new KeyChordEditorViewModel(this);
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
                OnPropertyChanged(nameof(HasAssignedActionVisibility));
                OnPropertyChanged(nameof(TargetVisibility));
                OnPropertyChanged(nameof(ArgumentsVisibility));
                OnPropertyChanged(nameof(InstalledAppPickerVisibility));
                OnPropertyChanged(nameof(KeyChordEditorVisibility));
                OnPropertyChanged(nameof(HasAssignedAction));
            }
        }
    }

    public string PrimaryKey
    {
        get => _primaryKey;
        set
        {
            var normalizedValue = StandardKeyCatalog.NormalizeKey(value);
            if (!SetProperty(ref _primaryKey, normalizedValue))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(normalizedValue))
            {
                var preferredGroup = StandardKeyCatalog.GetPreferredGroupKey(normalizedValue);
                if (!string.Equals(_primaryKeyGroup, preferredGroup, StringComparison.OrdinalIgnoreCase))
                {
                    _primaryKeyGroup = preferredGroup;
                    OnPropertyChanged(nameof(PrimaryKeyGroup));
                }
            }

            OnPropertyChanged(nameof(PrimaryKeyLabel));
            OnPropertyChanged(nameof(KeyChordDisplayText));
            OnPropertyChanged(nameof(ActionDescription));
        }
    }

    public string PrimaryKeyGroup
    {
        get => _primaryKeyGroup;
        set
        {
            var normalizedValue = StandardKeyCatalog.NormalizeGroupKey(value);
            SetProperty(ref _primaryKeyGroup, normalizedValue);
        }
    }

    public string ModifierSelectionSignature => string.Join("|", _modifierKeys);

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

    public string PrimaryKeyLabel => StandardKeyCatalog.GetLabel(PrimaryKey);

    public string KeyChordDisplayText => StandardKeyCatalog.BuildKeyChordText(PrimaryKey, _modifierKeys);

    public KeyChordEditorViewModel KeyChordEditor { get; }

    public bool HasAssignedAction => !string.IsNullOrWhiteSpace(Type);

    public Visibility HasAssignedActionVisibility => HasAssignedAction ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TargetVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ArgumentsVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InstalledAppPickerVisibility => Type == HotkeyActionType.OpenApplication ? Visibility.Visible : Visibility.Collapsed;

    public Visibility KeyChordEditorVisibility => Type == HotkeyActionType.SendStandardKey ? Visibility.Visible : Visibility.Collapsed;

    public ActionDefinitionConfiguration ToConfiguration()
    {
        var chord = BuildKeyChordConfiguration();
        return new ActionDefinitionConfiguration
        {
            Type = Type,
            KeyChord = Type == HotkeyActionType.SendStandardKey ? chord : null,
            Target = Type == HotkeyActionType.OpenApplication && !string.IsNullOrWhiteSpace(Target) ? Target.Trim() : null,
            Arguments = Type == HotkeyActionType.OpenApplication && !string.IsNullOrWhiteSpace(Arguments) ? Arguments.Trim() : null
        };
    }

    public void ClearAssignment()
    {
        Type = HotkeyActionType.None;
        PrimaryKey = string.Empty;
        PrimaryKeyGroup = StandardKeyCatalog.GroupOptions[0].Key;
        SetModifierKeys([]);
        Target = string.Empty;
        Arguments = string.Empty;
    }

    public void ClearPrimaryKeyIfGroupMismatch()
    {
        if (!string.IsNullOrWhiteSpace(PrimaryKey) &&
            !StandardKeyCatalog.MatchesGroup(PrimaryKey, PrimaryKeyGroup))
        {
            PrimaryKey = string.Empty;
        }
    }

    public string GetEffectivePrimaryKeyGroup()
    {
        return !string.IsNullOrWhiteSpace(PrimaryKey)
            ? StandardKeyCatalog.GetPreferredGroupKey(PrimaryKey)
            : StandardKeyCatalog.NormalizeGroupKey(PrimaryKeyGroup);
    }

    public void ApplyPrimaryKeyGroup(string? group)
    {
        PrimaryKeyGroup = StandardKeyCatalog.NormalizeGroupKey(group);
        ClearPrimaryKeyIfGroupMismatch();
    }

    public bool HasModifier(string key)
    {
        var normalizedKey = StandardKeyCatalog.NormalizeModifierKey(key);
        return !string.IsNullOrWhiteSpace(normalizedKey) &&
               _modifierKeys.Contains(normalizedKey, StringComparer.OrdinalIgnoreCase);
    }

    public void SetModifier(string key, bool enabled)
    {
        var normalizedKey = StandardKeyCatalog.NormalizeModifierKey(key);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return;
        }

        var updatedKeys = _modifierKeys
            .Where(item => !string.Equals(item, normalizedKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (enabled)
        {
            updatedKeys.Add(normalizedKey);
        }

        SetModifierKeys(updatedKeys);
    }

    private void SetModifierKeys(IEnumerable<string> modifierKeys)
    {
        var normalizedKeys = StandardKeyCatalog.NormalizeModifierKeys(modifierKeys);
        if (_modifierKeys.SequenceEqual(normalizedKeys, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _modifierKeys = [.. normalizedKeys];
        OnPropertyChanged(nameof(ModifierSelectionSignature));
        OnPropertyChanged(nameof(KeyChordDisplayText));
        OnPropertyChanged(nameof(ActionDescription));
    }

    private KeyChordConfiguration? BuildKeyChordConfiguration()
    {
        return StandardKeyCatalog.NormalizeChord(new KeyChordConfiguration
        {
            PrimaryKey = PrimaryKey,
            Modifiers = [.. _modifierKeys]
        });
    }

    private string BuildActionDescription()
    {
        if (Type == HotkeyActionType.SendStandardKey)
        {
            var chordText = KeyChordDisplayText;
            if (!string.IsNullOrWhiteSpace(PrimaryKey) && !string.IsNullOrWhiteSpace(chordText))
            {
                return string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    ResourceStringService.GetString("Action.SendKey.WithChord", "Sends the key shortcut {0}."),
                    chordText);
            }

            if (!string.IsNullOrWhiteSpace(chordText))
            {
                return string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    ResourceStringService.GetString("Action.SendKey.ChooseKey", "Choose a primary key to complete {0}."),
                    chordText);
            }

            return ResourceStringService.GetString("Action.SendKey.NoKey", "Sends the keyboard key or shortcut that you choose below.");
        }

        return ActionCatalog.GetDescription(Type);
    }
}
