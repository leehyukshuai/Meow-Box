using System.Management;
using MeowBox.Core.Models;

namespace MeowBox.Core.Services;

public sealed class BatteryControlService
{
    private const string WmiNamespace = @"\\.\root\wmi";
    private const string WmiClassName = "MICommonInterface";
    private const string WmiMethodName = "MiInterface";
    private const int BufferLength = 32;
    private const int SettleDelayMs = 700;
    private readonly object _commandSync = new();
    private readonly object _stateSync = new();
    private readonly WindowsPowerModeService _windowsPowerModeService = new();
    private BatteryControlState? _cachedState;

    public BatteryControlState QueryState()
    {
        lock (_commandSync)
        {
            var target = ResolveTarget();
            if (target is null)
            {
                var unsupportedState = new BatteryControlState
                {
                    Supported = false
                };

                UpdateCachedState(unsupportedState);
                return unsupportedState;
            }

            var performanceResponse = Invoke(target, CreateBuffer(fun1: 0xFA00, fun2: 0x0800, fun3: 0x0000, fun4: 0));
            var chargeResponse = Invoke(target, CreateBuffer(fun1: 0xFA00, fun2: 0x1000, fun3: 0x0002, fun4: 0));

            if (performanceResponse.Data0 is null)
            {
                throw new InvalidOperationException("Performance mode query returned an empty response.");
            }

            if (chargeResponse.Function != 0x1000 || chargeResponse.Data0 != 0x0002 || chargeResponse.Data1 is null)
            {
                throw new InvalidOperationException("Charge limit query returned an unexpected response.");
            }

            var state = new BatteryControlState
            {
                Supported = true,
                InstanceName = target.InstanceName,
                PerformanceModeKey = BatteryControlCatalog.GetPerformanceModeKey(performanceResponse.Data0.Value),
                SelectedPerformanceModeKey = BatteryControlCatalog.GetPerformanceModeKey(performanceResponse.Data0.Value),
                IsAcPowered = _windowsPowerModeService.IsAcPowered(),
                BatteryLevelPercent = _windowsPowerModeService.GetBatteryLevelPercent(),
                IsBatterySaverEnabled = string.Equals(
                    BatteryControlCatalog.GetPerformanceModeKey(performanceResponse.Data0.Value),
                    BatteryControlCatalog.Battery,
                    StringComparison.OrdinalIgnoreCase),
                ChargeLimitPercent = BatteryControlCatalog.GetChargeLimitPercent(chargeResponse.Data1.Value)
            };

            UpdateCachedState(state);
            return state;
        }
    }

    public BatteryControlState SetPerformanceMode(string modeKey)
    {
        var target = ResolveTarget(required: true)!;
        var rawCode = BatteryControlCatalog.GetPerformanceRawCode(modeKey);
        _ = Invoke(target, CreateBuffer(fun1: 0xFB00, fun2: 0x0800, fun3: rawCode, fun4: 0));
        Thread.Sleep(SettleDelayMs);
        var state = QueryState();
        if (!string.Equals(state.PerformanceModeKey, modeKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Performance mode verification did not match the requested value.");
        }

        return state;
    }

    public bool TryGetCachedState(out BatteryControlState state)
    {
        lock (_stateSync)
        {
            if (_cachedState is null)
            {
                state = new BatteryControlState
                {
                    Supported = false
                };

                return false;
            }

            state = CloneState(_cachedState);
            return true;
        }
    }

    public BatteryControlState SetPerformanceModeFast(string modeKey)
    {
        lock (_commandSync)
        {
            var target = ResolveTarget(required: true)!;
            var normalizedModeKey = BatteryControlCatalog.NormalizePerformanceModeKey(modeKey);
            var rawCode = BatteryControlCatalog.GetPerformanceRawCode(normalizedModeKey);
            _ = Invoke(target, CreateBuffer(fun1: 0xFB00, fun2: 0x0800, fun3: rawCode, fun4: 0));

            BatteryControlState nextState;
            lock (_stateSync)
            {
                nextState = _cachedState is null
                    ? new BatteryControlState
                    {
                        Supported = true,
                        InstanceName = target.InstanceName,
                        PerformanceModeKey = normalizedModeKey,
                        SelectedPerformanceModeKey = normalizedModeKey,
                        IsAcPowered = _windowsPowerModeService.IsAcPowered(),
                        BatteryLevelPercent = _windowsPowerModeService.GetBatteryLevelPercent(),
                        IsBatterySaverEnabled = string.Equals(normalizedModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase),
                        ChargeLimitPercent = BatteryControlCatalog.DefaultChargeLimitPercent
                    }
                    : CloneState(_cachedState);

                nextState.Supported = true;
                nextState.InstanceName = string.IsNullOrWhiteSpace(nextState.InstanceName) ? target.InstanceName : nextState.InstanceName;
                nextState.PerformanceModeKey = normalizedModeKey;
                nextState.SelectedPerformanceModeKey = normalizedModeKey;
                nextState.IsAcPowered = _windowsPowerModeService.IsAcPowered();
                nextState.BatteryLevelPercent = _windowsPowerModeService.GetBatteryLevelPercent();
                nextState.IsBatterySaverEnabled = string.Equals(normalizedModeKey, BatteryControlCatalog.Battery, StringComparison.OrdinalIgnoreCase);
                _cachedState = CloneState(nextState);
            }

            return CloneState(nextState);
        }
    }

    public BatteryControlState SetChargeLimitPercentFast(int percent)
    {
        lock (_commandSync)
        {
            var target = ResolveTarget(required: true)!;
            var normalizedPercent = BatteryControlCatalog.NormalizeChargeLimitPercent(percent);
            var rawCode = BatteryControlCatalog.GetChargeLimitRawCode(normalizedPercent);
            _ = Invoke(target, CreateBuffer(fun1: 0xFB00, fun2: 0x1000, fun3: 0x0002, fun4: rawCode));

            BatteryControlState nextState;
            lock (_stateSync)
            {
                nextState = _cachedState is null
                    ? new BatteryControlState
                    {
                        Supported = true,
                        InstanceName = target.InstanceName,
                        PerformanceModeKey = BatteryControlCatalog.DefaultPerformanceModeKey,
                        SelectedPerformanceModeKey = BatteryControlCatalog.DefaultPerformanceModeKey,
                        IsAcPowered = _windowsPowerModeService.IsAcPowered(),
                        BatteryLevelPercent = _windowsPowerModeService.GetBatteryLevelPercent(),
                        ChargeLimitPercent = normalizedPercent
                    }
                    : CloneState(_cachedState);

                nextState.Supported = true;
                nextState.InstanceName = string.IsNullOrWhiteSpace(nextState.InstanceName) ? target.InstanceName : nextState.InstanceName;
                nextState.IsAcPowered = _windowsPowerModeService.IsAcPowered();
                nextState.BatteryLevelPercent = _windowsPowerModeService.GetBatteryLevelPercent();
                nextState.ChargeLimitPercent = normalizedPercent;
                _cachedState = CloneState(nextState);
            }

            return CloneState(nextState);
        }
    }

    public BatteryControlState SetChargeLimitPercent(int percent)
    {
        var target = ResolveTarget(required: true)!;
        var normalizedPercent = BatteryControlCatalog.NormalizeChargeLimitPercent(percent);
        var rawCode = BatteryControlCatalog.GetChargeLimitRawCode(normalizedPercent);
        _ = Invoke(target, CreateBuffer(fun1: 0xFB00, fun2: 0x1000, fun3: 0x0002, fun4: rawCode));
        Thread.Sleep(SettleDelayMs);
        var state = QueryState();
        if (state.ChargeLimitPercent != normalizedPercent)
        {
            throw new InvalidOperationException("Charge limit verification did not match the requested value.");
        }

        return state;
    }

    private static MiInterfaceTargetInfo? ResolveTarget(bool required = false)
    {
        using var searcher = new ManagementObjectSearcher(WmiNamespace, "SELECT * FROM " + WmiClassName);
        using var collection = searcher.Get();

        var targets = collection
            .OfType<ManagementObject>()
            .Select(instance => new MiInterfaceTargetInfo(
                instance["InstanceName"]?.ToString() ?? string.Empty,
                instance["Active"] is bool active && active,
                instance.Path.Path))
            .ToList();

        var target = targets
            .FirstOrDefault(item => item.Active && item.InstanceName.Contains("MIFS", StringComparison.OrdinalIgnoreCase))
            ?? targets.FirstOrDefault();

        if (target is null && required)
        {
            throw new InvalidOperationException("MICommonInterface instance was not found.");
        }

        return target;
    }

    private static MiInterfaceResponse Invoke(MiInterfaceTargetInfo targetInfo, byte[] inData)
    {
        using var target = new ManagementObject(targetInfo.Path);
        using var parameters = target.GetMethodParameters(WmiMethodName);
        parameters["InData"] = inData;
        var result = target.InvokeMethod(WmiMethodName, parameters, null);
        if (result is null)
        {
            throw new InvalidOperationException("The MICommonInterface call returned no result.");
        }

        var returnCode = Convert.ToUInt32(result["ReturnCode"] ?? 0u);
        if (returnCode != 0)
        {
            throw new InvalidOperationException("The MICommonInterface call failed with return code " + returnCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        var outData = result["OutData"] as byte[] ?? [];
        return ParseResponse(outData);
    }

    private static byte[] CreateBuffer(ushort fun1, ushort fun2, ushort fun3, uint fun4)
    {
        var bytes = new byte[BufferLength];
        Array.Copy(BitConverter.GetBytes(fun1), 0, bytes, 0, sizeof(ushort));
        Array.Copy(BitConverter.GetBytes(fun2), 0, bytes, 2, sizeof(ushort));
        Array.Copy(BitConverter.GetBytes(fun3), 0, bytes, 4, sizeof(ushort));
        Array.Copy(BitConverter.GetBytes(fun4), 0, bytes, 6, sizeof(uint));
        return bytes;
    }

    private static MiInterfaceResponse ParseResponse(byte[] outData)
    {
        if (outData.Length < 6)
        {
            return new MiInterfaceResponse();
        }

        return new MiInterfaceResponse
        {
            Status = BitConverter.ToUInt16(outData, 0),
            Function = BitConverter.ToUInt16(outData, 2),
            Data0 = BitConverter.ToUInt16(outData, 4),
            Data1 = outData.Length >= 10 ? BitConverter.ToUInt32(outData, 6) : null,
            Data2 = outData.Length >= 14 ? BitConverter.ToUInt32(outData, 10) : null,
            Data3 = outData.Length >= 18 ? BitConverter.ToUInt32(outData, 14) : null
        };
    }

    private void UpdateCachedState(BatteryControlState state)
    {
        lock (_stateSync)
        {
            _cachedState = CloneState(state);
        }
    }

    private static BatteryControlState CloneState(BatteryControlState state)
    {
        return new BatteryControlState
        {
            Supported = state.Supported,
            InstanceName = state.InstanceName,
            PerformanceModeKey = state.PerformanceModeKey,
            SelectedPerformanceModeKey = state.SelectedPerformanceModeKey,
            IsBatterySaverEnabled = state.IsBatterySaverEnabled,
            IsAcPowered = state.IsAcPowered,
            BatteryLevelPercent = state.BatteryLevelPercent,
            ChargeLimitPercent = state.ChargeLimitPercent
        };
    }

    private sealed record MiInterfaceTargetInfo(string InstanceName, bool Active, string Path);

    private sealed class MiInterfaceResponse
    {
        public ushort? Status { get; init; }

        public ushort? Function { get; init; }

        public ushort? Data0 { get; init; }

        public uint? Data1 { get; init; }

        public uint? Data2 { get; init; }

        public uint? Data3 { get; init; }
    }
}
