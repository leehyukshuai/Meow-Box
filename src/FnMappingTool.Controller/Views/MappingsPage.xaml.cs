using System.Collections.ObjectModel;
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshOsdIcons();
    }

    private void RefreshOsdIcons()
    {
        Controller.RefreshOsdIconCatalog();
        var selectedPath = Controller.SelectedMapping?.Action.OsdIconPath;

        OsdIconFiles.Clear();
        OsdIconFiles.Add(new OsdIconFileEntry
        {
            DisplayName = "No icon",
            Path = string.Empty
        });

        Directory.CreateDirectory(Controller.OsdIconDirectory);
        foreach (var file in Directory.GetFiles(Controller.OsdIconDirectory, "*.png", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            OsdIconFiles.Add(new OsdIconFileEntry
            {
                DisplayName = Path.GetFileNameWithoutExtension(file),
                Path = file
            });
        }

        OsdIconComboBox.ItemsSource = OsdIconFiles;
        OsdIconComboBox.SelectedItem = OsdIconFiles.FirstOrDefault(item =>
            string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? OsdIconFiles.FirstOrDefault();
    }

    private async void OnAddMappingClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Controller.AddMapping();
            MappingsListView.UpdateLayout();
            RefreshOsdIcons();
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
        }
    }

    private async void OnBrowseExecutableClick(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync([".exe", ".lnk", ".bat", "*"]);
        if (!string.IsNullOrWhiteSpace(path) && Controller.SelectedMapping is not null)
        {
            Controller.SelectedMapping.Action.Target = path;
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
    }

    private async void OnSaveMappingClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Controller.SaveSelectedMapping();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Could not save mapping", exception.Message);
        }
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
}
