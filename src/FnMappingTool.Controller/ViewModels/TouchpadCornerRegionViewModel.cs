using System.Globalization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class TouchpadCornerRegionViewModel : ObservableObject
{
    public TouchpadCornerRegionViewModel(
        string regionId,
        TouchpadCornerRegionConfiguration? model,
        int surfaceWidth,
        int surfaceHeight)
    {
        model ??= regionId == TouchpadCornerRegionId.RightTop
            ? TouchpadCornerRegionConfiguration.CreateRightTopDefault()
            : TouchpadCornerRegionConfiguration.CreateLeftTopDefault();

        RegionId = regionId;
        SurfaceWidth = surfaceWidth;
        SurfaceHeight = surfaceHeight;
        Bounds = model.Bounds ?? new TouchpadRegionBoundsConfiguration();

        var isRightTop = string.Equals(regionId, TouchpadCornerRegionId.RightTop, StringComparison.OrdinalIgnoreCase);
        Title = isRightTop
            ? LocalizedText.Pick("Right top corner", "右上角")
            : LocalizedText.Pick("Left top corner", "左上角");
        Description = LocalizedText.Pick(
            "A touch that starts inside this corner can run its own deep-press or long-press action.",
            "从这个角落开始的触控可以运行独立的重按或长按动作。");
        PriorityHint = LocalizedText.Pick(
            "Corner deep press should override the global deep press action.",
            "角落重按应优先覆盖全局重按动作。");

        DeepPress = new TouchpadTriggerActionEditorViewModel(
            LocalizedText.Pick("Deep press", "重按"),
            LocalizedText.Pick(
                "Runs once when pressure reaches the built-in deep press level inside this corner.",
                "当这个角落内的压力达到内置重按力度时执行一次。"),
            model.DeepPressAction);
        LongPress = new TouchpadTriggerActionEditorViewModel(
            LocalizedText.Pick("Long press", "长按"),
            string.Format(
                CultureInfo.CurrentCulture,
                LocalizedText.Pick(
                    "Runs after holding inside this corner for about {0} ms.",
                    "在这个角落内持续按住约 {0} 毫秒后执行。"),
                RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs),
            model.LongPressAction);

        TriggerEditors = [DeepPress, LongPress];
    }

    public string RegionId { get; }

    public int SurfaceWidth { get; }

    public int SurfaceHeight { get; }

    public string Title { get; }

    public string Description { get; }

    public string PriorityHint { get; }

    public TouchpadRegionBoundsConfiguration Bounds { get; }

    public TouchpadTriggerActionEditorViewModel DeepPress { get; }

    public TouchpadTriggerActionEditorViewModel LongPress { get; }

    public IReadOnlyList<TouchpadTriggerActionEditorViewModel> TriggerEditors { get; }

    public string BoundsSummary => string.Format(
        CultureInfo.CurrentCulture,
        LocalizedText.Pick("X {0}-{1} · Y {2}-{3}", "X {0}-{1} · Y {2}-{3}"),
        Bounds.Left,
        Bounds.Right,
        Bounds.Top,
        Bounds.Bottom);

    public string CoverageSummary => string.Format(
        CultureInfo.CurrentCulture,
        LocalizedText.Pick("Surface {0} × {1}", "表面 {0} × {1}"),
        SurfaceWidth,
        SurfaceHeight);

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
