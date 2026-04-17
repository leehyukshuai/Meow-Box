using System.Collections.ObjectModel;
using MeowBox.Core.Models;

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
        ? LocalizedText.Pick("Service offline", "服务未连接")
        : DeepPressed
            ? LocalizedText.Pick("Deep press triggered", "重按已触发")
            : ButtonPressed
                ? LocalizedText.Pick("Pressing", "按压中")
                : HasInteraction
                    ? LocalizedText.Pick("Touching", "触摸中")
                    : HasReceivedInput
                        ? LocalizedText.Pick("Idle", "空闲")
                        : LocalizedText.Pick("Waiting for touchpad input", "等待触控板输入");

    public string StatusDescription => !ServiceAvailable
        ? LocalizedText.Pick("Start the worker to receive raw touchpad reports.", "启动后台服务后才会接收触控板原始报文。")
        : !IsRegistered
            ? LocalizedText.Pick("Touchpad raw input registration failed.", "触控板 Raw Input 注册失败。")
            : !HasReceivedInput
                ? LocalizedText.Pick("No touchpad frame has been received yet.", "尚未收到触控板帧。")
                : SupportsPressure
                    ? LocalizedText.Pick("Live pressure comes from raw HID contact pressure.", "实时压感来自原始 HID 触点压力字段。")
                    : LocalizedText.Pick("Pressure is unavailable for the current touchpad report.", "当前触控板报文未能提供压力数据。");

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
