using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Controller.ViewModels;

public sealed class TouchpadCornerRegionViewModel : ObservableObject
{
    public TouchpadCornerRegionViewModel(
        string regionId,
        TouchpadCornerRegionConfiguration? model)
    {
        model ??= regionId == TouchpadCornerRegionId.RightTop
            ? TouchpadCornerRegionConfiguration.CreateRightTopDefault()
            : TouchpadCornerRegionConfiguration.CreateLeftTopDefault();

        RegionId = regionId;
        Bounds = model.Bounds ?? new TouchpadRegionBoundsConfiguration();

        var isRightTop = string.Equals(regionId, TouchpadCornerRegionId.RightTop, StringComparison.OrdinalIgnoreCase);
        var regionLabel = isRightTop
            ? ResourceStringService.GetString("Touchpad.Corner.RightTop", "Right top")
            : ResourceStringService.GetString("Touchpad.Corner.LeftTop", "Left top");
        Title = isRightTop
            ? ResourceStringService.GetString("Touchpad.Corner.RightTopTitle", "Right top corner")
            : ResourceStringService.GetString("Touchpad.Corner.LeftTopTitle", "Left top corner");
        Description = ResourceStringService.GetString("Touchpad.Corner.Description", "A touch that starts inside this corner can run its own deep-press or long-press action.");

        DeepPress = new TouchpadTriggerActionEditorViewModel(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                ResourceStringService.GetString("Touchpad.Corner.DeepPressFormat", "{0} · Deep press"),
                regionLabel),
            ResourceStringService.GetString("Touchpad.Corner.DeepPressDescription", "Runs once when a touch that starts in this corner reaches the built-in deep press level."),
            null,
            model.DeepPressAction);
        LongPress = new TouchpadTriggerActionEditorViewModel(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                ResourceStringService.GetString("Touchpad.Corner.LongPressFormat", "{0} · Long press"),
                regionLabel),
            ResourceStringService.GetString("Touchpad.Corner.LongPressDescription", "Runs after a touch that starts in this corner keeps holding until the configured long-press duration is reached."),
            null,
            model.LongPressAction);
    }

    public string RegionId { get; }

    public string Title { get; }

    public string Description { get; }

    public TouchpadRegionBoundsConfiguration Bounds { get; }

    public TouchpadTriggerActionEditorViewModel DeepPress { get; }

    public TouchpadTriggerActionEditorViewModel LongPress { get; }

    public TouchpadCornerRegionConfiguration ToConfiguration()
    {
        return new TouchpadCornerRegionConfiguration
        {
            Id = RegionId,
            Bounds = new TouchpadRegionBoundsConfiguration
            {
                Left = Bounds.Left,
                Top = Bounds.Top,
                Right = Bounds.Right,
                Bottom = Bounds.Bottom
            },
            DeepPressAction = DeepPress.Action.ToConfiguration(),
            LongPressAction = LongPress.Action.ToConfiguration()
        };
    }
}
