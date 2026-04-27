using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Controller.ViewModels;

public sealed class TouchpadConfigurationViewModel : ObservableObject
{
    private bool _enabled;
    private int _lightPressThreshold;
    private int _deepPressThreshold;
    private int _longPressDurationMs;
    private int _pressSensitivityLevel;
    private int _feedbackLevel;
    private bool _deepPressHapticsEnabled;
    private TouchpadTriggerActionEditorViewModel? _selectedActionEditor;

    public TouchpadConfigurationViewModel(TouchpadConfiguration? model = null)
    {
        model ??= new TouchpadConfiguration();
        _enabled = model.Enabled;
        _lightPressThreshold = model.LightPressThreshold <= 0
            ? RuntimeDefaults.DefaultTouchpadLightPressThreshold
            : model.LightPressThreshold;
        _deepPressThreshold = model.DeepPressThreshold <= 0
            ? RuntimeDefaults.DefaultTouchpadDeepPressThreshold
            : model.DeepPressThreshold;
        _longPressDurationMs = model.LongPressDurationMs <= 0
            ? RuntimeDefaults.DefaultTouchpadCornerLongPressDurationMs
            : model.LongPressDurationMs;
        _pressSensitivityLevel = TouchpadHardwareSettings.NormalizeLevel(
            model.PressSensitivityLevel,
            TouchpadHardwareSettings.MapThresholdToPressSensitivityLevel(_lightPressThreshold));
        _lightPressThreshold = TouchpadHardwareSettings.MapPressSensitivityLevelToThreshold(_pressSensitivityLevel);
        _feedbackLevel = TouchpadHardwareSettings.NormalizeLevel(model.FeedbackLevel);
        _deepPressHapticsEnabled = model.DeepPressHapticsEnabled;

        SurfaceWidth = model.SurfaceWidth > 0
            ? model.SurfaceWidth
            : RuntimeDefaults.DefaultTouchpadSurfaceWidth;
        SurfaceHeight = model.SurfaceHeight > 0
            ? model.SurfaceHeight
            : RuntimeDefaults.DefaultTouchpadSurfaceHeight;

        MainRegionDeepPress = new TouchpadTriggerActionEditorViewModel(
            ResourceStringService.GetString("Touchpad.Trigger.MainDeepPress.Title", "Main region · Deep press"),
            ResourceStringService.GetString("Touchpad.Trigger.MainDeepPress.Description", "Runs once when a touch in the main region reaches the built-in deep press level. L, R, LT, and RT are excluded."),
            null,
            model.DeepPressAction);
        LeftEdgeSlide = new TouchpadTriggerActionEditorViewModel(
            ResourceStringService.GetString("Touchpad.Trigger.LeftSlide.Title", "Left side · Drag"),
            ResourceStringService.GetString("Touchpad.Trigger.LeftSlide.Description", "Choose what vertical dragging in the left edge region controls."),
            null,
            model.LeftEdgeSlideAction,
            simpleEdgeSlideMapping: true);
        RightEdgeSlide = new TouchpadTriggerActionEditorViewModel(
            ResourceStringService.GetString("Touchpad.Trigger.RightSlide.Title", "Right side · Drag"),
            ResourceStringService.GetString("Touchpad.Trigger.RightSlide.Description", "Choose what vertical dragging in the right edge region controls."),
            null,
            model.RightEdgeSlideAction,
            simpleEdgeSlideMapping: true);
        LeftTopCorner = new TouchpadCornerRegionViewModel(
            TouchpadCornerRegionId.LeftTop,
            model.LeftTopCorner);
        RightTopCorner = new TouchpadCornerRegionViewModel(
            TouchpadCornerRegionId.RightTop,
            model.RightTopCorner);
        GuidanceLines =
        [
            ResourceStringService.GetString("Touchpad.Guidance.MainExcludesCorners", "Main-region deep press excludes L, R, LT, and RT."),
            ResourceStringService.GetString("Touchpad.Guidance.EdgeSlideMapping", "Left-side and right-side drag can each be mapped to volume or brightness."),
            ResourceStringService.GetString("Touchpad.Guidance.CornerActions", "LT and RT can each run separate deep-press and long-press actions."),
            ResourceStringService.GetString("Touchpad.Guidance.LongPressAdjustable", "Long-press timing can be adjusted from the touchpad settings panel.")
        ];

        CornerRegions = [LeftTopCorner, RightTopCorner];
        AllActionEditors =
        [
            MainRegionDeepPress,
            LeftEdgeSlide,
            RightEdgeSlide,
            LeftTopCorner.DeepPress,
            RightTopCorner.DeepPress,
            LeftTopCorner.LongPress,
            RightTopCorner.LongPress
        ];
        SelectedActionEditor = AllActionEditors.FirstOrDefault();

        foreach (var editor in AllActionEditors)
        {
            editor.Action.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasAnyAssignedAction));
                OnPropertyChanged(nameof(EdgeSlideEnabled));
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

    public int LightPressThreshold
    {
        get => _lightPressThreshold;
        set => SetProperty(ref _lightPressThreshold, Math.Clamp(value, 20, RuntimeDefaults.DefaultTouchpadDeepPressThreshold - 1));
    }

    public int LongPressDurationMs
    {
        get => _longPressDurationMs;
        set => SetProperty(ref _longPressDurationMs, Math.Clamp(value, 200, 3000));
    }

    public int PressSensitivityLevel
    {
        get => _pressSensitivityLevel;
        set
        {
            var normalized = TouchpadHardwareSettings.NormalizeLevel(value);
            if (SetProperty(ref _pressSensitivityLevel, normalized))
            {
                LightPressThreshold = TouchpadHardwareSettings.MapPressSensitivityLevelToThreshold(normalized);
            }
        }
    }

    public int FeedbackLevel
    {
        get => _feedbackLevel;
        set => SetProperty(ref _feedbackLevel, TouchpadHardwareSettings.NormalizeLevel(value));
    }

    public bool DeepPressHapticsEnabled
    {
        get => _deepPressHapticsEnabled;
        set => SetProperty(ref _deepPressHapticsEnabled, value);
    }

    public bool EdgeSlideEnabled => LeftEdgeSlide.Action.HasAssignedAction || RightEdgeSlide.Action.HasAssignedAction;

    public TouchpadTriggerActionEditorViewModel? SelectedActionEditor
    {
        get => _selectedActionEditor;
        set => SetProperty(ref _selectedActionEditor, value);
    }

    public int SurfaceWidth { get; }

    public int SurfaceHeight { get; }

    public bool HasAnyAssignedAction => AllActionEditors.Any(item => item.Action.HasAssignedAction);

    public TouchpadTriggerActionEditorViewModel MainRegionDeepPress { get; }

    public ActionDefinitionViewModel DeepPressAction => MainRegionDeepPress.Action;

    public TouchpadTriggerActionEditorViewModel LeftEdgeSlide { get; }

    public TouchpadTriggerActionEditorViewModel RightEdgeSlide { get; }

    public TouchpadCornerRegionViewModel LeftTopCorner { get; }

    public TouchpadCornerRegionViewModel RightTopCorner { get; }

    public IReadOnlyList<string> GuidanceLines { get; }

    public IReadOnlyList<TouchpadCornerRegionViewModel> CornerRegions { get; }

    public IReadOnlyList<TouchpadTriggerActionEditorViewModel> AllActionEditors { get; }

    public TouchpadConfiguration ToConfiguration()
    {
        return new TouchpadConfiguration
        {
            Enabled = HasAnyAssignedAction,
            LightPressThreshold = TouchpadHardwareSettings.MapPressSensitivityLevelToThreshold(PressSensitivityLevel),
            PressSensitivityLevel = PressSensitivityLevel,
            DeepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold,
            LongPressDurationMs = LongPressDurationMs,
            FeedbackLevel = FeedbackLevel,
            DeepPressHapticsEnabled = DeepPressHapticsEnabled,
            EdgeSlideEnabled = EdgeSlideEnabled,
            SurfaceWidth = SurfaceWidth,
            SurfaceHeight = SurfaceHeight,
            DeepPressAction = MainRegionDeepPress.Action.ToConfiguration(),
            LeftEdgeSlideAction = LeftEdgeSlide.Action.ToConfiguration(),
            RightEdgeSlideAction = RightEdgeSlide.Action.ToConfiguration(),
            LeftTopCorner = LeftTopCorner.ToConfiguration(),
            RightTopCorner = RightTopCorner.ToConfiguration()
        };
    }
}
