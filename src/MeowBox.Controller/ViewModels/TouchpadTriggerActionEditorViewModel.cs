using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Controller.ViewModels;

public sealed class TouchpadTriggerActionEditorViewModel : ObservableObject
{
    private readonly ActionDefinitionViewModel _action;
    private readonly bool _simpleEdgeSlideMapping;

    public TouchpadTriggerActionEditorViewModel(
        string title,
        string description,
        IEnumerable<string>? guidanceLines = null,
        ActionDefinitionConfiguration? model = null,
        bool simpleEdgeSlideMapping = false)
    {
        Title = title;
        Description = description;
        _simpleEdgeSlideMapping = simpleEdgeSlideMapping;
        GuidanceLines =
        [
            .. (guidanceLines ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
        ];
        _action = new ActionDefinitionViewModel(model);
        _action.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ActionDefinitionViewModel.Type) or
                nameof(ActionDefinitionViewModel.PrimaryKey) or
                nameof(ActionDefinitionViewModel.ModifierSelectionSignature) or
                nameof(ActionDefinitionViewModel.Target) or
                nameof(ActionDefinitionViewModel.Arguments))
            {
                OnPropertyChanged(nameof(ActionSummary));
                OnPropertyChanged(nameof(ActionDescription));
                OnPropertyChanged(nameof(ActionIconGlyph));
                OnPropertyChanged(nameof(StandardActionPickerVisibility));
                OnPropertyChanged(nameof(SimpleEdgeSlideMappingVisibility));
                OnPropertyChanged(nameof(IsEdgeSlideMappingOff));
                OnPropertyChanged(nameof(IsEdgeSlideMappingVolume));
                OnPropertyChanged(nameof(IsEdgeSlideMappingBrightness));
            }
        };

    }

    public string Title { get; }

    public string Description { get; }

    public IReadOnlyList<string> GuidanceLines { get; }

    public Visibility GuidanceVisibility => GuidanceLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StandardActionPickerVisibility => _simpleEdgeSlideMapping ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SimpleEdgeSlideMappingVisibility => _simpleEdgeSlideMapping ? Visibility.Visible : Visibility.Collapsed;

    public ActionDefinitionViewModel Action => _action;

    public string ActionSummary => _simpleEdgeSlideMapping ? GetEdgeSlideMappingLabel() : Action.ActionLabel;

    public string ActionDescription => _simpleEdgeSlideMapping ? GetEdgeSlideMappingDescription() : Action.ActionDescription;

    public string ActionIconGlyph => _simpleEdgeSlideMapping ? GetEdgeSlideMappingGlyph() : Action.ActionIconGlyph;

    public bool IsEdgeSlideMappingOff
    {
        get => _simpleEdgeSlideMapping && string.IsNullOrWhiteSpace(Action.Type);
        set
        {
            if (!_simpleEdgeSlideMapping || !value)
            {
                return;
            }

            Action.Type = HotkeyActionType.None;
        }
    }

    public bool IsEdgeSlideMappingVolume
    {
        get => _simpleEdgeSlideMapping && string.Equals(Action.Type, HotkeyActionType.VolumeUp, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (!_simpleEdgeSlideMapping || !value)
            {
                return;
            }

            Action.Type = HotkeyActionType.VolumeUp;
        }
    }

    public bool IsEdgeSlideMappingBrightness
    {
        get => _simpleEdgeSlideMapping && string.Equals(Action.Type, HotkeyActionType.BrightnessUp, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (!_simpleEdgeSlideMapping || !value)
            {
                return;
            }

            Action.Type = HotkeyActionType.BrightnessUp;
        }
    }

    public string EdgeSlideMappingOffLabel => ResourceStringService.GetString("Touchpad.EdgeSlide.Disabled.Label", "Disable mapping");

    public string EdgeSlideMappingVolumeLabel => ResourceStringService.GetString("Touchpad.EdgeSlide.Volume.Label", "Adjust volume");

    public string EdgeSlideMappingBrightnessLabel => ResourceStringService.GetString("Touchpad.EdgeSlide.Brightness.Label", "Adjust brightness");

    private string GetEdgeSlideMappingLabel()
    {
        return Action.Type switch
        {
            HotkeyActionType.VolumeUp => ResourceStringService.GetString("Touchpad.EdgeSlide.Volume.Label", "Adjust volume"),
            HotkeyActionType.BrightnessUp => ResourceStringService.GetString("Touchpad.EdgeSlide.Brightness.Label", "Adjust brightness"),
            _ => ResourceStringService.GetString("Touchpad.EdgeSlide.Disabled.Label", "Disable mapping")
        };
    }

    private string GetEdgeSlideMappingDescription()
    {
        return Action.Type switch
        {
            HotkeyActionType.VolumeUp => ResourceStringService.GetString(
                "Touchpad.EdgeSlide.Volume.Description",
                "Dragging vertically in this edge region adjusts system volume."),
            HotkeyActionType.BrightnessUp => ResourceStringService.GetString(
                "Touchpad.EdgeSlide.Brightness.Description",
                "Dragging vertically in this edge region adjusts display brightness."),
            _ => ResourceStringService.GetString(
                "Touchpad.EdgeSlide.Disabled.Description",
                "This edge drag mapping is disabled.")
        };
    }

    private string GetEdgeSlideMappingGlyph()
    {
        return Action.Type switch
        {
            HotkeyActionType.VolumeUp => "",
            HotkeyActionType.BrightnessUp => "",
            _ => ActionCatalog.NoActionIconGlyph
        };
    }

}
