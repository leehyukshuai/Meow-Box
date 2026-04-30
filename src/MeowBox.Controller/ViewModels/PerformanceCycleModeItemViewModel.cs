using MeowBox.Core.Models;

namespace MeowBox.Controller.ViewModels;

public sealed class PerformanceCycleModeItemViewModel : ObservableObject
{
    private bool _isIncluded;
    private bool _canChangeInclusion = true;
    private bool _canMoveUp;
    private bool _canMoveDown;

    public PerformanceCycleModeItemViewModel(string key)
    {
        Key = BatteryControlCatalog.NormalizePerformanceModeCycleKey(key);
    }

    public string Key { get; }

    public string Label => BatteryControlCatalog.GetPerformanceModeCycleLabel(Key);

    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetProperty(ref _isIncluded, value);
    }

    public bool CanChangeInclusion
    {
        get => _canChangeInclusion;
        set => SetProperty(ref _canChangeInclusion, value);
    }

    public bool CanMoveUp
    {
        get => _canMoveUp;
        set => SetProperty(ref _canMoveUp, value);
    }

    public bool CanMoveDown
    {
        get => _canMoveDown;
        set => SetProperty(ref _canMoveDown, value);
    }
}
