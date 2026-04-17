using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class TouchpadTriggerActionEditorViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _action;

    public TouchpadTriggerActionEditorViewModel(
        string title,
        string description,
        IEnumerable<string>? guidanceLines = null,
        ActionDefinitionConfiguration? model = null)
    {
        Title = title;
        Description = description;
        GuidanceLines =
        [
            .. (guidanceLines ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
        ];
        _action = new ActionDefinitionViewModel(model);
        _action.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ActionDefinitionViewModel.Type) or
                nameof(ActionDefinitionViewModel.PrimaryKey) or
                nameof(ActionDefinitionViewModel.ModifierSelectionSignature) or
                nameof(ActionDefinitionViewModel.Target) or
                nameof(ActionDefinitionViewModel.Arguments))
            {
                OnPropertyChanged(nameof(ActionSummary));
                OnPropertyChanged(nameof(ActionDescription));
                OnPropertyChanged(nameof(ActionIconGlyph));
            }
        };

    }

    public string Title { get; }

    public string Description { get; }

    public IReadOnlyList<string> GuidanceLines { get; }

    public Visibility GuidanceVisibility => GuidanceLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public ActionDefinitionViewModel Action => _action;

    public string ActionSummary => Action.ActionLabel;

    public string ActionDescription => Action.ActionDescription;

    public string ActionIconGlyph => Action.ActionIconGlyph;

}
