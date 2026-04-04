using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FnMappingTool.Controller.Services;

namespace FnMappingTool.Controller.Views;

public sealed partial class KeysPage : Page
{
    public FnMappingToolController Controller => App.Controller;

    public KeysPage()
    {
        InitializeComponent();
        DataContext = Controller;
    }

    private async void OnAddKeyClick(object sender, RoutedEventArgs e)
    {
        if (!Controller.ServiceRunning)
        {
            await ShowMessageAsync("Background service is stopped", "Start the background service in Settings before capturing a new key.");
            return;
        }

        var dialog = new AddKeyDialog(Controller)
        {
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.CapturedEvent is not null)
        {
            try
            {
                Controller.AddKey(dialog.KeyName, dialog.CapturedEvent);
            }
            catch (Exception exception)
            {
                await ShowMessageAsync("Could not add key", exception.Message);
            }
        }
    }

    private async void OnSaveKeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Controller.SaveSelectedKey();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("Could not save key", exception.Message);
        }
    }

    private async void OnDeleteKeyClick(object sender, RoutedEventArgs e)
    {
        if (Controller.SelectedKey is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Delete key",
            Content = "This will also delete mappings that use the selected key.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Controller.DeleteSelectedKey();
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
