using MeowBox.Core.Models;
using MeowBox.Core.Services;

namespace MeowBox.Worker.Services;

internal sealed class TouchpadEdgeSlideService : IDisposable
{
    private readonly NativeActionService _nativeActionService;
    private readonly Func<TouchpadConfiguration> _getTouchpadConfiguration;
    private readonly Action<string> _setLastEventSummary;
    private readonly Action<string> _setStateMessage;
    private readonly object _edgeSlideSync = new();
    private readonly object _edgeSlideHapticsSync = new();

    private int _pendingBrightnessEdgeSteps;
    private int _pendingVolumeEdgeSteps;
    private bool _edgeSlideProcessing;
    private bool _edgeSlideHapticsPrimed;
    private TouchpadPrivateHidService.PulseSession? _edgeSlidePulseSession;

    public TouchpadEdgeSlideService(
        NativeActionService nativeActionService,
        Func<TouchpadConfiguration> getTouchpadConfiguration,
        Action<string> setLastEventSummary,
        Action<string> setStateMessage)
    {
        _nativeActionService = nativeActionService;
        _getTouchpadConfiguration = getTouchpadConfiguration;
        _setLastEventSummary = setLastEventSummary;
        _setStateMessage = setStateMessage;
    }

    public void Queue(TouchpadEdgeSlideTarget target, int signedSteps, string summary)
    {
        if (target == TouchpadEdgeSlideTarget.None)
        {
            return;
        }

        lock (_edgeSlideSync)
        {
            if (target == TouchpadEdgeSlideTarget.Brightness)
            {
                _pendingBrightnessEdgeSteps += signedSteps;
            }
            else
            {
                _pendingVolumeEdgeSteps += signedSteps;
            }

            _setLastEventSummary(summary);
            if (_edgeSlideProcessing)
            {
                return;
            }

            _edgeSlideProcessing = true;
        }

        _ = Task.Run(ProcessPendingAsync);
    }

    public void PrewarmIfNeeded(bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                EnsureHapticsReady();
            }
            catch (Exception exception)
            {
                _edgeSlideHapticsPrimed = false;
                _setStateMessage(exception.Message);
            }
        });
    }

    public void Dispose()
    {
        ReleasePulseSession();
    }

    private async Task ProcessPendingAsync()
    {
        while (true)
        {
            int brightnessSteps;
            int volumeSteps;
            lock (_edgeSlideSync)
            {
                brightnessSteps = _pendingBrightnessEdgeSteps;
                volumeSteps = _pendingVolumeEdgeSteps;
                _pendingBrightnessEdgeSteps = 0;
                _pendingVolumeEdgeSteps = 0;

                if (brightnessSteps == 0 && volumeSteps == 0)
                {
                    _edgeSlideProcessing = false;
                    return;
                }
            }

            try
            {
                if (brightnessSteps != 0 || volumeSteps != 0)
                {
                    EnsureHapticsReady();
                }

                ApplyBrightnessSteps(brightnessSteps);
                ApplyVolumeSteps(volumeSteps);
            }
            catch (Exception exception)
            {
                _setStateMessage(exception.Message);
            }
            finally
            {
                _nativeActionService.ReleaseBrightnessAdjustment();
            }

            await Task.Yield();
        }
    }

    private void ApplyBrightnessSteps(int steps)
    {
        if (steps == 0)
        {
            return;
        }

        for (var index = 0; index < Math.Abs(steps); index++)
        {
            if (steps > 0)
            {
                ExecuteStep(_nativeActionService.BrightnessEdgeSlideUp);
            }
            else
            {
                ExecuteStep(_nativeActionService.BrightnessEdgeSlideDown);
            }
        }
    }

    private void ApplyVolumeSteps(int steps)
    {
        if (steps == 0)
        {
            return;
        }

        for (var index = 0; index < Math.Abs(steps); index++)
        {
            if (steps > 0)
            {
                ExecuteStep(_nativeActionService.EdgeSlideVolumeUp);
            }
            else
            {
                ExecuteStep(_nativeActionService.EdgeSlideVolumeDown);
            }
        }
    }

    private void ExecuteStep(Action stepAction)
    {
        if (!_edgeSlideHapticsPrimed)
        {
            EnsureHapticsReady();
        }

        TryPulse();
        stepAction();
    }

    private void EnsureHapticsReady()
    {
        if (_edgeSlideHapticsPrimed)
        {
            return;
        }

        lock (_edgeSlideHapticsSync)
        {
            if (_edgeSlideHapticsPrimed)
            {
                return;
            }

            var touchpad = _getTouchpadConfiguration();
            TouchpadPrivateHidService.SetVibration(TouchpadHardwareSettings.NormalizeLevel(touchpad.FeedbackLevel));
            TouchpadPrivateHidService.SetHaptic(true);
            _edgeSlideHapticsPrimed = true;
        }
    }

    private void TryPulse()
    {
        try
        {
            GetOrCreatePulseSession().Pulse();
        }
        catch
        {
            ReleasePulseSession();
            _edgeSlideHapticsPrimed = false;
        }
    }

    private TouchpadPrivateHidService.PulseSession GetOrCreatePulseSession()
    {
        lock (_edgeSlideHapticsSync)
        {
            _edgeSlidePulseSession ??= TouchpadPrivateHidService.CreatePulseSession();
            return _edgeSlidePulseSession;
        }
    }

    private void ReleasePulseSession()
    {
        lock (_edgeSlideHapticsSync)
        {
            _edgeSlidePulseSession?.Dispose();
            _edgeSlidePulseSession = null;
        }
    }

    internal enum TouchpadEdgeSlideTarget
    {
        None,
        Brightness,
        Volume
    }
}
