using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MeowBox.Controller.Views;
using MeowBox.Core.Models;
using MeowBox.Core.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MeowBox.Controller.Services;

public static class ActionEditorDialogService
{
    public static async Task<string?> PickActionTypeAsync(XamlRoot xamlRoot, string currentActionType)
    {
        var dialog = new ActionPickerDialog(currentActionType)
        {
            RequestedTheme = App.ThemeService.GetResolvedTheme(),
            XamlRoot = xamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? dialog.SelectedAction?.Key
            : null;
    }

    public static async Task<string?> PickInstalledAppTargetAsync(XamlRoot xamlRoot, IReadOnlyList<InstalledAppEntry> apps)
    {
        var dialog = new AppPickerDialog(apps)
        {
            RequestedTheme = App.ThemeService.GetResolvedTheme(),
            XamlRoot = xamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? dialog.SelectedApp?.LaunchTarget
            : null;
    }

    public static async Task<string?> PickLaunchPathAsync(IEnumerable<string> fileTypes)
    {
        if (App.MainWindow is null)
        {
            return null;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };

        foreach (var type in fileTypes)
        {
            picker.FileTypeFilter.Add(type);
        }

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task ShowMessageAsync(
        XamlRoot xamlRoot,
        string title,
        string message,
        ContentDialogButton defaultButton = ContentDialogButton.Close)
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = App.ThemeService.GetResolvedTheme(),
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = ResourceStringService.GetString("Dialog.Close", "Close"),
            DefaultButton = defaultButton
        };

        await dialog.ShowAsync();
    }
}
