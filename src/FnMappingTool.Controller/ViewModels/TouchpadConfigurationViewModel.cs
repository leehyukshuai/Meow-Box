using System.Globalization;
using FnMappingTool.Core.Models;

namespace FnMappingTool.Controller.ViewModels;

public sealed class TouchpadConfigurationViewModel : ObservableObject
{
    private bool _enabled;
    private int _deepPressThreshold;

    public TouchpadConfigurationViewModel(TouchpadConfiguration? model = null)
    {
        model ??= new TouchpadConfiguration();
        _enabled = model.Enabled;
        _deepPressThreshold = model.DeepPressThreshold <= 0
            ? RuntimeDefaults.DefaultTouchpadDeepPressThreshold
            : model.DeepPressThreshold;

        SurfaceWidth = model.SurfaceWidth > 0
            ? model.SurfaceWidth
            : RuntimeDefaults.DefaultTouchpadSurfaceWidth;
        SurfaceHeight = model.SurfaceHeight > 0
            ? model.SurfaceHeight
            : RuntimeDefaults.DefaultTouchpadSurfaceHeight;

        GlobalDeepPress = new TouchpadTriggerActionEditorViewModel(
            LocalizedText.Pick("Global deep press", "全局重按"),
            LocalizedText.Pick(
                "Runs once when pressure reaches the built-in deep press level anywhere on the touchpad, unless a corner override matches first.",
                "当触控板任意位置达到内置重按力度时执行一次；若角落重按命中，则优先执行角落动作。"),
            model.DeepPressAction);
        LeftTopCorner = new TouchpadCornerRegionViewModel(
            TouchpadCornerRegionId.LeftTop,
            model.LeftTopCorner,
            SurfaceWidth,
            SurfaceHeight);
        RightTopCorner = new TouchpadCornerRegionViewModel(
            TouchpadCornerRegionId.RightTop,
            model.RightTopCorner,
            SurfaceWidth,
            SurfaceHeight);

        CornerRegions = [LeftTopCorner, RightTopCorner];
        AllActionEditors =
        [
            GlobalDeepPress,
            LeftTopCorner.DeepPress,
            LeftTopCorner.LongPress,
            RightTopCorner.DeepPress,
            RightTopCorner.LongPress
        ];

        foreach (var editor in AllActionEditors)
        {
            editor.Action.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasAnyAssignedAction));
            };
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public int DeepPressThreshold
    {
        get => _deepPressThreshold;
        set => SetProperty(ref _deepPressThreshold, Math.Clamp(value, 100, 4000));
    }

    public int SurfaceWidth { get; }

    public int SurfaceHeight { get; }

    public string SurfaceSizeLabel => string.Format(
        CultureInfo.CurrentCulture,
        "{0} × {1}",
        SurfaceWidth,
        SurfaceHeight);

    public bool HasAnyAssignedAction => AllActionEditors.Any(item => item.Action.HasAssignedAction);

    public TouchpadTriggerActionEditorViewModel GlobalDeepPress { get; }

    public ActionDefinitionViewModel DeepPressAction => GlobalDeepPress.Action;

    public TouchpadCornerRegionViewModel LeftTopCorner { get; }

    public TouchpadCornerRegionViewModel RightTopCorner { get; }

    public IReadOnlyList<TouchpadCornerRegionViewModel> CornerRegions { get; }

    public IReadOnlyList<TouchpadTriggerActionEditorViewModel> AllActionEditors { get; }

    public string ActionSummary => GlobalDeepPress.ActionSummary;

    public string ActionDescription => GlobalDeepPress.ActionDescription;

    public string ActionIconGlyph => GlobalDeepPress.ActionIconGlyph;

    public TouchpadConfiguration ToConfiguration()
    {
        return new TouchpadConfiguration
        {
            Enabled = HasAnyAssignedAction,
            DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold,
            SurfaceWidth = SurfaceWidth,
            SurfaceHeight = SurfaceHeight,
            DeepPressAction = GlobalDeepPress.Action.ToConfiguration(),
            LeftTopCorner = LeftTopCorner.ToConfiguration(),
            RightTopCorner = RightTopCorner.ToConfiguration()
        };
    }
}
