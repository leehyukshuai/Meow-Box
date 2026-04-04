using Microsoft.UI.Xaml;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class ActionOptionItemViewModel : ObservableObject
{
    private bool _isSelected;

    public ActionOptionItemViewModel(ActionOption option)
    {
        Option = option;
    }

    public ActionOption Option { get; }

    public string Key => Option.Key;

    public string Label => Option.Label;

    public string Description => Option.Description;

    public string IconGlyph => Option.IconGlyph;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SelectionStripVisibility));
                OnPropertyChanged(nameof(SelectedOverlayVisibility));
            }
        }
    }

    public Visibility SelectionStripVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SelectedOverlayVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
}
