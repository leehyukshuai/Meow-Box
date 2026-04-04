using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FnMappingTool.Controller.Models;
using FnMappingTool.Controller.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FnMappingTool.Controller.Views;

public sealed partial class MappingsPage : Page
{
    public FnMappingToolController Controller => App.Controller;

    public ObservableCollection<OsdIconFileEntry> OsdIconFiles { get; } = [];

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
        RefreshOsdIcons();
        UpdateEmptyStates();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        Controller.MappingItems.CollectionChanged -= OnMappingItemsCollectionChanged;
    }

    private void RefreshOsdIcons()
    {
        Controller.RefreshOsdIconCatalog();
        var selectedPath = Controller.SelectedMapping?.Action.OsdIconPath;

        OsdIconFiles.Clear();
        OsdIconFiles.Add(new OsdIconFileEntry
        {
            DisplayName = "No icon",
            RelativePath = string.Empty
        });

        Directory.CreateDirectory(Controller.OsdIconDirectory);
        foreach (var file in Directory.GetFiles(Controller.OsdIconDirectory, "*.png", SearchOption.AllDirectories)
                     .OrderBy(
                         static file => Path.GetRelativePath(App.Controller.OsdIconDirectory, file),
                         StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(Controller.OsdIconDirectory, file);
            OsdIconFiles.Add(new OsdIconFileEntry
            {
                DisplayName = Path.ChangeExtension(relativePath, null) ?? relativePath,
                RelativePath = relativePath
            });
        }

        OsdIconComboBox.ItemsSource = OsdIconFiles;
        OsdIconComboBox.SelectedItem = OsdIconFiles.FirstOrDefault(item =>
            string.Equals(item.RelativePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? OsdIconFiles.FirstOrDefault();
    }

    private async void OnAddMappingClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Controller.AddMapping();
            MappingsListView.UpdateLayout();
            RefreshOsdIcons();
            UpdateEmptyStates();
            if (Controller.SelectedMapping is not null)
            {
                MappingsListView.ScrollIntoView(Controller.SelectedMapping);
                MappingsListView.Focus(FocusState.Programmatic);
            }
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Could not add mapping", exception.Message);
        }
    }

    private void OnMappingReferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        Controller.RefreshMappingReferences();
        TrySaveMappingAsync();
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

    private async void OnDeleteMappingClick(object sender, RoutedEventArgs e)
    {
        if (Controller.SelectedMapping is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Delete mapping",
            Content = "Delete the selected mapping?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Controller.DeleteSelectedMapping();
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
            CloseButtonText = "Close"
        };

        await dialog.ShowAsync();
    }

    private void OnMappingEditorLostFocus(object sender, RoutedEventArgs e)
    {
        TrySaveMappingAsync();
    }

    private void OnMappingEditorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TrySaveMappingAsync();
    }

    private async void TrySaveMappingAsync()
    {
        if (Controller.SelectedMapping is null)
        {
            return;
        }

        try
        {
            Controller.SaveSelectedMapping();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Could not save mapping", exception.Message);
        }
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FnMappingToolController.SelectedMapping))
        {
            DispatcherQueue.TryEnqueue(UpdateEmptyStates);
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
        MappingDetailsEmptyStatePanel.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
    }
}
