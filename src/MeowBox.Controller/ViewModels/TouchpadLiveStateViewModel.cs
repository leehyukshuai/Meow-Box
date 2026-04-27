using System.Collections.ObjectModel;
using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Controller.ViewModels;

public sealed class TouchpadLiveStateViewModel : ObservableObject
{
    private bool _serviceAvailable;
    private bool _isRegistered;
    private bool _hasReceivedInput;
    private bool _supportsPressure;
    private DateTimeOffset _timestamp;
    private string _deviceName = string.Empty;
    private bool _hasInteraction;
    private bool _buttonPressed;
    private bool _deepPressed;
    private int _pressure;
    private int _peakPressure;
    private int _lightPressThreshold = RuntimeDefaults.DefaultTouchpadLightPressThreshold;
    private int _deepPressThreshold = RuntimeDefaults.DefaultTouchpadDeepPressThreshold;
    private ushort _scanTime;
    private byte _contactCount;

    public ObservableCollection<TouchpadLiveContactViewModel> Contacts { get; } = [];

    public bool ServiceAvailable
    {
        get => _serviceAvailable;
        private set
        {
            if (SetProperty(ref _serviceAvailable, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusDescription));
            }
        }
    }

    public bool IsRegistered
    {
        get => _isRegistered;
        private set
        {
            if (SetProperty(ref _isRegistered, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusDescription));
            }
        }
    }

    public bool HasReceivedInput
    {
        get => _hasReceivedInput;
        private set
        {
            if (SetProperty(ref _hasReceivedInput, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusDescription));
                OnPropertyChanged(nameof(IsVisualizerEmpty));
            }
        }
    }

    public bool SupportsPressure
    {
        get => _supportsPressure;
        private set
        {
            if (SetProperty(ref _supportsPressure, value))
            {
                OnPropertyChanged(nameof(StatusDescription));
            }
        }
    }

    public DateTimeOffset Timestamp
    {
        get => _timestamp;
        private set => SetProperty(ref _timestamp, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    public bool HasInteraction
    {
        get => _hasInteraction;
        private set
        {
            if (SetProperty(ref _hasInteraction, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsVisualizerEmpty));
            }
        }
    }

    public bool ButtonPressed
    {
        get => _buttonPressed;
        private set
        {
            if (SetProperty(ref _buttonPressed, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool DeepPressed
    {
        get => _deepPressed;
        private set
        {
            if (SetProperty(ref _deepPressed, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public int Pressure
    {
        get => _pressure;
        private set => SetProperty(ref _pressure, value);
    }

    public int PeakPressure
    {
        get => _peakPressure;
        private set => SetProperty(ref _peakPressure, value);
    }

    public int LightPressThreshold
    {
        get => _lightPressThreshold;
        private set => SetProperty(ref _lightPressThreshold, value);
    }

    public int DeepPressThreshold
    {
        get => _deepPressThreshold;
        private set => SetProperty(ref _deepPressThreshold, value);
    }

    public ushort ScanTime
    {
        get => _scanTime;
        private set => SetProperty(ref _scanTime, value);
    }

    public byte ContactCount
    {
        get => _contactCount;
        private set => SetProperty(ref _contactCount, value);
    }

    public bool IsVisualizerEmpty => !ServiceAvailable || !HasReceivedInput;

    public string StatusText => !ServiceAvailable
        ? ResourceStringService.GetString("Touchpad.Status.ServiceOffline", "Service offline")
        : DeepPressed
            ? ResourceStringService.GetString("Touchpad.Status.DeepPressTriggered", "Deep press triggered")
            : ButtonPressed
                ? ResourceStringService.GetString("Touchpad.Status.Pressing", "Pressing")
                : HasInteraction
                    ? ResourceStringService.GetString("Touchpad.Status.Touching", "Touching")
                    : HasReceivedInput
                        ? ResourceStringService.GetString("Touchpad.Status.Idle", "Idle")
                        : ResourceStringService.GetString("Touchpad.Status.Waiting", "Waiting for touchpad input");

    public string StatusDescription => !ServiceAvailable
        ? ResourceStringService.GetString("Touchpad.StatusDesc.Offline", "Start the worker to receive raw touchpad reports.")
        : !IsRegistered
            ? ResourceStringService.GetString("Touchpad.StatusDesc.NotRegistered", "Touchpad raw input registration failed.")
            : !HasReceivedInput
                ? ResourceStringService.GetString("Touchpad.StatusDesc.NoFrame", "No touchpad frame has been received yet.")
                : SupportsPressure
                    ? ResourceStringService.GetString("Touchpad.StatusDesc.HasPressure", "Live pressure comes from raw HID contact pressure.")
                    : ResourceStringService.GetString("Touchpad.StatusDesc.NoPressure", "Pressure is unavailable for the current touchpad report.");

    public void Update(TouchpadLiveStateSnapshot? snapshot, bool serviceAvailable, int fallbackThreshold)
    {
        ServiceAvailable = serviceAvailable;

        snapshot ??= new TouchpadLiveStateSnapshot
        {
            LightPressThreshold = RuntimeDefaults.DefaultTouchpadLightPressThreshold,
            DeepPressThreshold = fallbackThreshold
        };

        IsRegistered = snapshot.IsRegistered;
        HasReceivedInput = snapshot.HasReceivedInput;
        SupportsPressure = snapshot.SupportsPressure;
        Timestamp = snapshot.Timestamp;
        DeviceName = snapshot.DeviceName ?? string.Empty;
        HasInteraction = snapshot.HasInteraction;
        ButtonPressed = snapshot.ButtonPressed;
        DeepPressed = snapshot.DeepPressed;
        Pressure = snapshot.Pressure;
        PeakPressure = snapshot.PeakPressure;
        LightPressThreshold = snapshot.LightPressThreshold > 0 ? snapshot.LightPressThreshold : RuntimeDefaults.DefaultTouchpadLightPressThreshold;
        DeepPressThreshold = snapshot.DeepPressThreshold > 0 ? snapshot.DeepPressThreshold : fallbackThreshold;
        ScanTime = snapshot.ScanTime;
        ContactCount = snapshot.ContactCount;

        Contacts.Clear();
        foreach (var contact in snapshot.Contacts.OrderByDescending(static item => item.Pressure))
        {
            var viewModel = new TouchpadLiveContactViewModel();
            viewModel.Update(contact);
            Contacts.Add(viewModel);
        }

        OnPropertyChanged(nameof(IsVisualizerEmpty));
    }
}
