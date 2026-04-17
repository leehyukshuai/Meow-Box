using System.Collections.ObjectModel;
using System.ComponentModel;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class StandardKeyPickerViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _action;
    private StandardKeyGroupOption? _selectedGroup;
    private StandardKeyOption? _selectedKey;
    private bool _isSynchronizing;

    public StandardKeyPickerViewModel(ActionDefinitionViewModel action)
    {
        _action = action;
        _action.PropertyChanged += OnActionPropertyChanged;
        SyncFromAction();
    }

    public IReadOnlyList<StandardKeyGroupOption> Groups => StandardKeyCatalog.GroupOptions;

    public ObservableCollection<StandardKeyOption> FilteredKeys { get; } = [];

    public StandardKeyGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (!SetProperty(ref _selectedGroup, value) || _isSynchronizing)
            {
                return;
            }

            _action.ApplyStandardKeyGroup(value?.Key);
            SyncFromAction();
        }
    }

    public StandardKeyOption? SelectedKey
    {
        get => _selectedKey;
        set
        {
            if (!SetProperty(ref _selectedKey, value) || _isSynchronizing)
            {
                return;
            }

            _action.StandardKey = value?.Key ?? string.Empty;
            SyncFromAction();
        }
    }

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ActionDefinitionViewModel.StandardKey) or
            nameof(ActionDefinitionViewModel.StandardKeyGroup) or
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
            var groupKey = _action.GetEffectiveStandardKeyGroup();
            var selectedGroup = Groups.FirstOrDefault(item =>
                string.Equals(item.Key, groupKey, StringComparison.OrdinalIgnoreCase))
                ?? Groups.FirstOrDefault();

            FilteredKeys.Clear();
            foreach (var option in StandardKeyCatalog.All.Where(item => StandardKeyCatalog.MatchesGroup(item, groupKey)))
            {
                FilteredKeys.Add(option);
            }

            SelectedGroup = selectedGroup;
            SelectedKey = FilteredKeys.FirstOrDefault(item =>
                string.Equals(item.Key, _action.StandardKey, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _isSynchronizing = false;
        }
    }
}
