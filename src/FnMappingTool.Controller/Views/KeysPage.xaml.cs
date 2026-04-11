using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FnMappingTool.Controller.Services;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FnMappingTool.Controller.Views;

public sealed partial class KeysPage : Page
{
    public FnMappingToolController Controller => App.Controller;

    public KeysPage()
    {
        InitializeComponent();
        XamlStringLocalizer.Apply(this);
        DataContext = Controller;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged += OnControllerPropertyChanged;
        Controller.KeyItems.CollectionChanged += OnKeyItemsCollectionChanged;
        UpdateEmptyStates();
        ScheduleLocalizationRefresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Controller.PropertyChanged -= OnControllerPropertyChanged;
        Controller.KeyItems.CollectionChanged -= OnKeyItemsCollectionChanged;
    }

    private async void OnAddKeyClick(object sender, RoutedEventArgs e)
    {
        if (!Controller.ServiceRunning)
        {
            await ShowMessageAsync(
                Localizer.GetString("Keys.Messages.ServiceStopped.Title"),
                Localizer.GetString("Keys.Messages.ServiceStopped.Body"));
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
                await ShowMessageAsync(Localizer.GetString("Keys.Messages.AddFailed.Title"), exception.Message);
            }
        }
    }

    private async void OnKeyNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (Controller.SelectedKey is null)
        {
            return;
        }

        try
        {
            Controller.SaveSelectedKey();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync(Localizer.GetString("Keys.Messages.SaveFailed.Title"), exception.Message);
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
            Title = Localizer.GetString("Keys.Messages.Delete.Title"),
            Content = Localizer.GetString("Keys.Messages.Delete.Body"),
            PrimaryButtonText = Localizer.GetString("Dialog.Delete"),
            CloseButtonText = Localizer.GetString("Dialog.Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Controller.DeleteSelectedKey();
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FnMappingToolController.SelectedKey))
        {
            DispatcherQueue.TryEnqueue(() =>
        {
            UpdateEmptyStates();
            ScheduleLocalizationRefresh();
        });
        }
    }

    private void OnKeyItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateEmptyStates();
            ScheduleLocalizationRefresh();
        });
    }

    private void ScheduleLocalizationRefresh()
    {
        DispatcherQueue.TryEnqueue(() => XamlStringLocalizer.Apply(this));
        _ = Task.Delay(200).ContinueWith(_ =>
        {
            DispatcherQueue.TryEnqueue(() => XamlStringLocalizer.Apply(this));
        }, TaskScheduler.Default);
    }

    private void UpdateEmptyStates()
    {
        var hasKeys = Controller.KeyItems.Count > 0;
        var hasSelection = Controller.SelectedKey is not null;

        KeysListView.Visibility = hasKeys ? Visibility.Visible : Visibility.Collapsed;
        KeysEmptyStatePanel.Visibility = hasKeys ? Visibility.Collapsed : Visibility.Visible;
        KeyDetailsContentPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        KeyDetailsEmptyStatePanel.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = Localizer.GetString("Dialog.Close")
        };

        await dialog.ShowAsync();
    }
}