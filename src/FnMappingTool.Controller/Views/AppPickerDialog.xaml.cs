using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using FnMappingTool.Controller.Services;
using FnMappingTool.Core.Services;

namespace FnMappingTool.Controller.Views;

public sealed partial class AppPickerDialog : ContentDialog
{
    private readonly IReadOnlyList<InstalledAppEntry> _allApps;

    public AppPickerDialog(IReadOnlyList<InstalledAppEntry> apps)
    {
        _allApps = apps;
        InitializeComponent();
        XamlStringLocalizer.Apply(this);
        PrimaryButtonClick += OnPrimaryButtonClick;
        IsPrimaryButtonEnabled = false;

        FilteredApps = new ObservableCollection<InstalledAppEntry>(_allApps);
        AppsListView.ItemsSource = FilteredApps;
        LoadingInfoBar.Message = _allApps.Count == 0
            ? Localizer.GetString("AppPicker.NoInstalledApps")
            : Localizer.Format("AppPicker.FoundInstalledApps", _allApps.Count);
        LoadingInfoBar.Severity = _allApps.Count == 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
    }

    public ObservableCollection<InstalledAppEntry> FilteredApps { get; }

    public InstalledAppEntry? SelectedApp => AppsListView.SelectedItem as InstalledAppEntry;

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchTextBox.Text.Trim();
        FilteredApps.Clear();

        foreach (var app in _allApps.Where(app => string.IsNullOrWhiteSpace(query) || app.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredApps.Add(app);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = SelectedApp is not null;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SelectedApp is null)
        {
            args.Cancel = true;
        }
    }
}