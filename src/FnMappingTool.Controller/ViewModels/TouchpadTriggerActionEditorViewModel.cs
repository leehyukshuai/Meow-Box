using System.Collections.ObjectModel;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class TouchpadTriggerActionEditorViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _action;
    private bool _isRefreshingStandardKeys;

    public TouchpadTriggerActionEditorViewModel(
        string title,
        string description,
        ActionDefinitionConfiguration? model = null)
    {
        Title = title;
        Description = description;
        _action = new ActionDefinitionViewModel(model);
        _action.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ActionDefinitionViewModel.Type) or nameof(ActionDefinitionViewModel.StandardKeyGroup))
            {
                RefreshStandardKeys();
            }

            if (e.PropertyName is nameof(ActionDefinitionViewModel.Type) or
                nameof(ActionDefinitionViewModel.StandardKey) or
                nameof(ActionDefinitionViewModel.Target) or
                nameof(ActionDefinitionViewModel.Arguments))
            {
                OnPropertyChanged(nameof(ActionSummary));
                OnPropertyChanged(nameof(ActionDescription));
                OnPropertyChanged(nameof(ActionIconGlyph));
            }
        };

        RefreshStandardKeys();
    }

    public string Title { get; }

    public string Description { get; }

    public IReadOnlyList<StandardKeyGroupOption> StandardKeyGroups { get; } = StandardKeyCatalog.GroupOptions;

    public ObservableCollection<StandardKeyOption> FilteredStandardKeys { get; } = [];

    public ActionDefinitionViewModel Action => _action;

    public string ActionSummary => Action.ActionLabel;

    public string ActionDescription => Action.ActionDescription;

    public string ActionIconGlyph => Action.ActionIconGlyph;

    public bool IsRefreshingStandardKeys
    {
        get => _isRefreshingStandardKeys;
        private set => SetProperty(ref _isRefreshingStandardKeys, value);
    }

    public void RefreshStandardKeys()
    {
        IsRefreshingStandardKeys = true;
        try
        {
            var selectedGroup = Action.StandardKeyGroup;
            FilteredStandardKeys.Clear();
            foreach (var option in StandardKeyCatalog.All.Where(item => StandardKeyCatalog.MatchesGroup(item, selectedGroup)))
            {
                FilteredStandardKeys.Add(option);
            }
        }
        finally
        {
            IsRefreshingStandardKeys = false;
        }
    }
}
