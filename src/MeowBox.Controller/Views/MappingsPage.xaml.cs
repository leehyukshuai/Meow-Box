using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MeowBox.Controller.Services;
using MeowBox.Controller.ViewModels;
using MeowBox.Core.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;
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
        DispatcherQueue.TryEnqueue(() => XamlStringLocalizer.Apply(this));
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
        var dialog = new AppPickerDialog(apps)
        {
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedApp is not null && Controller.SelectedMapping is not null)
        {
            Controller.SelectedMapping.Action.Target = dialog.SelectedApp.LaunchTarget;
            TrySaveMappingAsync();
        }
    }

    private async void OnBrowseExecutableClick(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync([".exe", ".lnk", ".bat", "*"]);
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

        var dialog = new ActionPickerDialog(Controller.SelectedMapping.Action.Type)
        {
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || dialog.SelectedAction is null)
        {
            return;
        }

        Controller.SetSelectedActionType(dialog.SelectedAction.Key);
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

    private async Task<string?> PickFileAsync(IEnumerable<string> types)
    {
        if (App.MainWindow is null)
        {
            return null;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };

        foreach (var type in types)
        {
            picker.FileTypeFilter.Add(type);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = ResourceStringService.GetString("Dialog.Close", "Close")
        };

        await dialog.ShowAsync();
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

    private void OnOsdEnabledChanged(object sender, RoutedEventArgs e)
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
            await ShowMessageAsync(ResourceStringService.GetString("Mappings.Messages.SaveFailed.Title", "Could not save mapping"), exception.Message);
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
                XamlStringLocalizer.Apply(this);
            });
        }
    }

    private void OnMappingItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateEmptyStates();
            XamlStringLocalizer.Apply(this);
        });
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
