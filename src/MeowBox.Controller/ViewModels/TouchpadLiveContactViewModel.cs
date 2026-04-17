using MeowBox.Core.Models;

namespace MeowBox.Controller.ViewModels;

public sealed class TouchpadLiveContactViewModel : ObservableObject
{
    private int _slotIndex;
    private bool _tip;
    private bool _confidence;
    private int _contactId;
    private int _x;
    private int _y;
    private int _pressure;

    public int SlotIndex
    {
        get => _slotIndex;
        private set => SetProperty(ref _slotIndex, value);
    }

    public bool Tip
    {
        get => _tip;
        private set => SetProperty(ref _tip, value);
    }

    public bool Confidence
    {
        get => _confidence;
        private set => SetProperty(ref _confidence, value);
    }

    public int ContactId
    {
        get => _contactId;
        private set => SetProperty(ref _contactId, value);
    }

    public int X
    {
        get => _x;
        private set => SetProperty(ref _x, value);
    }

    public int Y
    {
        get => _y;
        private set => SetProperty(ref _y, value);
    }

    public int Pressure
    {
        get => _pressure;
        private set => SetProperty(ref _pressure, value);
    }

    public string Label => $"S{SlotIndex}";

    public string Summary => $"#{ContactId} · X {X} · Y {Y} · P {Pressure}";

    public string CompactTelemetry => $"X {X}  Y {Y}  P {Pressure}";

    public void Update(TouchpadLiveContactSnapshot snapshot)
    {
        SlotIndex = snapshot.SlotIndex;
        Tip = snapshot.Tip;
        Confidence = snapshot.Confidence;
        ContactId = snapshot.ContactId;
        X = snapshot.X;
        Y = snapshot.Y;
        Pressure = snapshot.Pressure;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CompactTelemetry));
    }
}
