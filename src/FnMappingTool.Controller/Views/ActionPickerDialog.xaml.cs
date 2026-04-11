using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using FnMappingTool.Controller.Services;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.Views;

public sealed partial class ActionPickerDialog : ContentDialog
{
    private readonly IReadOnlyList<ActionOption> _allActions = ActionCatalog.All;

    public ActionPickerDialog(string? selectedActionType)
    {
        InitializeComponent();
        XamlStringLocalizer.Apply(this);
        PrimaryButtonClick += OnPrimaryButtonClick;
        IsPrimaryButtonEnabled = false;

        FilteredActions = [];
        CategoryOptions =
        [
            ..ActionCatalog.TagOptions
        ];

        CategoryComboBox.ItemsSource = CategoryOptions;
        CategoryComboBox.SelectedItem = CategoryOptions.FirstOrDefault();
        ActionsListView.ItemsSource = FilteredActions;

        RefreshActions(selectedActionType);
    }

    public ObservableCollection<ActionOption> FilteredActions { get; }

    public IReadOnlyList<ActionTagOption> CategoryOptions { get; }

    public ActionOption? SelectedAction => ActionsListView.SelectedItem as ActionOption;

    private void OnFilterChanged(object sender, object e)
    {
        RefreshActions(SelectedAction?.Key);
    }

    private void RefreshActions(string? preferredActionType)
    {
        var selectedTag = (CategoryComboBox.SelectedItem as ActionTagOption)?.Key;
        var query = SearchTextBox.Text.Trim();

        FilteredActions.Clear();
        foreach (var action in _allActions.Where(action =>
                     ActionCatalog.MatchesTag(action, selectedTag) &&
                     (string.IsNullOrWhiteSpace(query) ||
                      action.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                      action.Description.Contains(query, StringComparison.OrdinalIgnoreCase))))
        {
            FilteredActions.Add(action);
        }

        ActionsListView.SelectedItem = FilteredActions.FirstOrDefault(action =>
            string.Equals(action.Key, preferredActionType, StringComparison.OrdinalIgnoreCase));

        if (ActionsListView.SelectedItem is null && FilteredActions.Count > 0)
        {
            ActionsListView.SelectedIndex = 0;
        }

        IsPrimaryButtonEnabled = SelectedAction is not null;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = SelectedAction is not null;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SelectedAction is null)
        {
            args.Cancel = true;
        }
    }
}
