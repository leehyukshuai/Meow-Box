using System.Collections.ObjectModel;
using System.ComponentModel;
using MeowBox.Core.Models;

namespace MeowBox.Controller.ViewModels;

public sealed class KeyChordEditorViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _action;
    private StandardKeyGroupOption? _selectedGroup;
    private StandardKeyOption? _selectedPrimaryKey;
    private bool _isSynchronizing;

    public KeyChordEditorViewModel(ActionDefinitionViewModel action)
    {
        _action = action;
        _action.PropertyChanged += OnActionPropertyChanged;

        ControlModifier = new KeyChordModifierItemViewModel(this, KeyChordModifier.Control);
        ShiftModifier = new KeyChordModifierItemViewModel(this, KeyChordModifier.Shift);
        AltModifier = new KeyChordModifierItemViewModel(this, KeyChordModifier.Alt);
        WindowsModifier = new KeyChordModifierItemViewModel(this, KeyChordModifier.Windows);

        SyncFromAction();
    }

    public IReadOnlyList<StandardKeyGroupOption> Groups => StandardKeyCatalog.GroupOptions;

    public ObservableCollection<StandardKeyOption> FilteredPrimaryKeys { get; } = [];

    public KeyChordModifierItemViewModel ControlModifier { get; }

    public KeyChordModifierItemViewModel ShiftModifier { get; }

    public KeyChordModifierItemViewModel AltModifier { get; }

    public KeyChordModifierItemViewModel WindowsModifier { get; }

    public string ModifiersHeader => LocalizedText.Pick("Modifiers", "修饰键");

    public string PreviewHeader => LocalizedText.Pick("Preview", "预览");

    public StandardKeyGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (_isSynchronizing)
            {
                SetProperty(ref _selectedGroup, value);
                return;
            }

            if (value is null || !SetProperty(ref _selectedGroup, value))
            {
                return;
            }

            _action.ApplyPrimaryKeyGroup(value.Key);
            SyncFromAction();
        }
    }

    public StandardKeyOption? SelectedPrimaryKey
    {
        get => _selectedPrimaryKey;
        set
        {
            if (_isSynchronizing)
            {
                SetProperty(ref _selectedPrimaryKey, value);
                return;
            }

            if (value is null || !SetProperty(ref _selectedPrimaryKey, value))
            {
                return;
            }

            _action.PrimaryKey = value.Key;
            SyncFromAction();
        }
    }

    internal void SetModifier(string key, bool enabled)
    {
        _action.SetModifier(key, enabled);
        SyncFromAction();
    }

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ActionDefinitionViewModel.PrimaryKey) or
            nameof(ActionDefinitionViewModel.PrimaryKeyGroup) or
            nameof(ActionDefinitionViewModel.ModifierSelectionSignature) or
            nameof(ActionDefinitionViewModel.Type))
        {
            SyncFromAction();
        }
    }

    private void SyncFromAction()
    {
        _isSynchronizing = true;
        try
        {
            var groupKey = _action.GetEffectivePrimaryKeyGroup();
            var selectedGroup = Groups.FirstOrDefault(item =>
                string.Equals(item.Key, groupKey, StringComparison.OrdinalIgnoreCase))
                ?? Groups.FirstOrDefault();

            FilteredPrimaryKeys.Clear();
            foreach (var option in StandardKeyCatalog.All.Where(item => StandardKeyCatalog.MatchesGroup(item, groupKey)))
            {
                FilteredPrimaryKeys.Add(option);
            }

            SelectedGroup = selectedGroup;
            SelectedPrimaryKey = FilteredPrimaryKeys.FirstOrDefault(item =>
                string.Equals(item.Key, _action.PrimaryKey, StringComparison.OrdinalIgnoreCase));

            ControlModifier.SetIsSelected(_action.HasModifier(ControlModifier.Key));
            ShiftModifier.SetIsSelected(_action.HasModifier(ShiftModifier.Key));
            AltModifier.SetIsSelected(_action.HasModifier(AltModifier.Key));
            WindowsModifier.SetIsSelected(_action.HasModifier(WindowsModifier.Key));
        }
        finally
        {
            _isSynchronizing = false;
        }
    }
}

public sealed class KeyChordModifierItemViewModel : ObservableObject
{
    private readonly KeyChordEditorViewModel _owner;
    private bool _isSelected;

    public KeyChordModifierItemViewModel(KeyChordEditorViewModel owner, string key)
    {
        _owner = owner;
        Key = StandardKeyCatalog.NormalizeModifierKey(key);
        Label = StandardKeyCatalog.GetModifierLabel(Key);
    }

    public string Key { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
            {
                return;
            }

            _owner.SetModifier(Key, value);
        }
    }

    internal void SetIsSelected(bool value)
    {
        SetProperty(ref _isSelected, value, nameof(IsSelected));
    }
}
