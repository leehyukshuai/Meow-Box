using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MeowBox.Controller.Services;
using MeowBox.Controller.ViewModels;
using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Controller.Views;

public sealed partial class MappingsPage : Page
{
    private ActionDefinitionViewModel? _subscribedAction;

    public MeowBoxController Controller => App.Controller;

    public MappingsPage()
    {
        InitializeComponent();
        DataContext = Controller;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged += OnControllerPropertyChanged;
        Controller.MappingItems.CollectionChanged += OnMappingItemsCollectionChanged;
        SubscribeToSelectedMappingAction();
        UpdateEmptyStates();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        Controller.MappingItems.CollectionChanged -= OnMappingItemsCollectionChanged;
        UnsubscribeFromSelectedMappingAction();
    }

    private void SubscribeToSelectedMappingAction()
    {
        var action = Controller.SelectedMapping?.Action;
        if (ReferenceEquals(action, _subscribedAction))
        {
            return;
        }

        UnsubscribeFromSelectedMappingAction();
        _subscribedAction = action;
        if (_subscribedAction is not null)
        {
            _subscribedAction.PropertyChanged += OnSelectedMappingActionChanged;
        }
    }

    private void UnsubscribeFromSelectedMappingAction()
    {
        if (_subscribedAction is not null)
        {
            _subscribedAction.PropertyChanged -= OnSelectedMappingActionChanged;
            _subscribedAction = null;
        }
    }

    private void OnSelectedMappingActionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Controller.IsReloadingConfiguration)
        {
            return;
        }

        if (e.PropertyName is nameof(ActionDefinitionViewModel.PrimaryKey) or
            nameof(ActionDefinitionViewModel.PrimaryKeyGroup) or
            nameof(ActionDefinitionViewModel.ModifierSelectionSignature))
        {
            TrySaveMappingAsync();
        }
    }

    private async void OnPickInstalledAppClick(object sender, RoutedEventArgs e)
    {
        var apps = await Controller.GetInstalledAppsAsync();
        var target = await ActionEditorDialogService.PickInstalledAppTargetAsync(Content.XamlRoot, apps);
        if (!string.IsNullOrWhiteSpace(target) && Controller.SelectedMapping is not null)
        {
            Controller.SelectedMapping.Action.Target = target;
            TrySaveMappingAsync();
        }
    }

    private async void OnBrowseExecutableClick(object sender, RoutedEventArgs e)
    {
        var path = await ActionEditorDialogService.PickLaunchPathAsync([".exe", ".lnk", ".bat", "*"]);
        if (!string.IsNullOrWhiteSpace(path) && Controller.SelectedMapping is not null)
        {
            Controller.SelectedMapping.Action.Target = path;
            TrySaveMappingAsync();
        }
    }

    private async void OnChooseActionClick(object sender, RoutedEventArgs e)
    {
        if (Controller.SelectedMapping is null)
        {
            return;
        }

        var actionType = await ActionEditorDialogService.PickActionTypeAsync(Content.XamlRoot, Controller.SelectedMapping.Action.Type);
        if (string.IsNullOrWhiteSpace(actionType))
        {
            return;
        }

        Controller.SetSelectedActionType(actionType);
        TrySaveMappingAsync();
    }

    private void OnClearActionClick(object sender, RoutedEventArgs e)
    {
        if (Controller.SelectedMapping is null)
        {
            return;
        }

        Controller.ClearSelectedMappingAction();
        TrySaveMappingAsync();
    }

    private void OnMappingEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (Controller.IsReloadingConfiguration)
        {
            return;
        }

        TrySaveMappingAsync();
    }

    private void OnMappingEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (Controller.IsReloadingConfiguration || Controller.SelectedMapping is null)
        {
            return;
        }

        TrySaveMappingAsync();
    }

    private async void TrySaveMappingAsync()
    {
        if (Controller.IsReloadingConfiguration || Controller.SelectedMapping is null)
        {
            return;
        }

        try
        {
            Controller.SaveSelectedMapping();
        }
        catch (Exception exception)
        {
            await ActionEditorDialogService.ShowMessageAsync(
                Content.XamlRoot,
                ResourceStringService.GetString("Mappings.Messages.SaveFailed.Title", "Could not save mapping"),
                exception.Message);
        }
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MeowBoxController.SelectedMapping))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                SubscribeToSelectedMappingAction();
                UpdateEmptyStates();
            });
        }
    }

    private void OnMappingItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateEmptyStates);
    }

    private void UpdateEmptyStates()
    {
        var hasMappings = Controller.MappingItems.Count > 0;
        var hasSelection = Controller.SelectedMapping is not null;

        MappingsListView.Visibility = hasMappings ? Visibility.Visible : Visibility.Collapsed;
        MappingsEmptyStatePanel.Visibility = hasMappings ? Visibility.Collapsed : Visibility.Visible;
        MappingDetailsContentPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        MappingDetailsEmptyStatePanel.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
    }
}
