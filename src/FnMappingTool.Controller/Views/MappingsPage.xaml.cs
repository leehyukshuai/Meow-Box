using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FnMappingTool.Controller.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FnMappingTool.Controller.Views;

public sealed partial class MappingsPage : Page
{
    public FnMappingToolController Controller => App.Controller;

    public MappingsPage()
    {
        InitializeComponent();
        DataContext = Controller;
    }

    private async void OnAddMappingClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Controller.AddMapping();
            MappingsListView.UpdateLayout();
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

    private async void OnBrowseOsdIconClick(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync([".png", ".jpg", ".jpeg", ".bmp", ".ico", "*"]);
        if (!string.IsNullOrWhiteSpace(path) && Controller.SelectedMapping is not null)
        {
            Controller.SelectedMapping.Action.OsdIconPath = path;
        }
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
