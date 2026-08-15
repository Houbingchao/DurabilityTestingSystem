using System.Collections.Concurrent;
using DurabilityTestingSystem.HardwareAdapter.XinChaoRenDaPcie1604;
using DurabilityTestingSystem.HardwareAdapter.ZlgUsbCanFd;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.HardwareAdapter.Site;

/// <summary>
/// 本项目冻结硬件的组合平台：USBCANFD-200U + PCIE-1604 + P-881B。
/// 电机协议及安全输入验收完成前，允许分别诊断 CAN/DAQ，但绝不开放动作启动。
/// </summary>
public sealed class SiteHardwarePlatform : IHardwarePlatform
{
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly ConcurrentDictionary<int, string> _lastControllerFrames = new();
    private readonly IMotorProtocolEncoder _motorProtocol = new PendingMotorProtocolEncoder();
    private ZlgUsbCanFdTransport? _can;
    private Pcie1604Acquisition? _analog;
    private TestSettings? _settings;
    private int[] _selectedStationIds = [];
    private SystemHealthSnapshot _health = CreateInitialHealth();
    private bool _disposed;

    public bool IsConfigured => true;
    public SystemHealthSnapshot Health => _health;
    public event EventHandler<SystemHealthSnapshot>? HealthChanged;

    public async Task<OperationResult> ConnectAndSelfCheckAsync(
        SystemProfile profile,
        TestSettings settings,
        IReadOnlyCollection<int> stationIds,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings.EnsureStationConfigurations();
        var selected = stationIds.Distinct().OrderBy(x => x).ToArray();
        if (selected.Length == 0 || selected.Any(id => !StationTopology.IsSupported(id)))
            return OperationResult.Fail($"工位只能在 1~{StationTopology.MaximumStationCount} 范围内选择。");
        if (selected.Any(id => !settings.Stations.Any(x => x.StationId == id && x.Enabled)))
            return OperationResult.Fail("选择中包含未启用或没有硬件映射的工位。");

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectDevicesAsync().ConfigureAwait(false);
            _settings = settings;
            _selectedStationIds = selected;
            var devices = new List<DeviceStatus>();
            var faults = new List<string>();

            await ConnectCanAsync(settings, selected, devices, faults, cancellationToken).ConfigureAwait(false);
            await ConnectAnalogAsync(settings, selected, devices, faults, cancellationToken).ConfigureAwait(false);

            var qualificationMissing = profile.Qualification?.MissingItems() ?? ["部署验收信息缺失"];
            var terminalState = profile.Qualification?.TerminalBoardCompatibilityApproved == true
                ? DeviceConnectionState.Online
                : DeviceConnectionState.Warning;
            devices.Add(Device(
                "terminal",
                AnalogHardwareBaseline.TerminalDisplayName,
                terminalState,
                terminalState == DeviceConnectionState.Online
                    ? "厂家兼容确认已登记；仍须按通道核对焊接模式、线缆和标定记录"
                    : "尚无 PCIE-1604/P-881B 书面兼容确认；两份手册的配套型号及 DB37 第19脚定义不一致"));

            var safetyApproved = profile.Qualification?.SafetySignalConditioningApproved == true;
            devices.Add(Device(
                "safety",
                "硬件安全回路与隔离输入",
                safetyApproved ? DeviceConnectionState.Online : DeviceConnectionState.Warning,
                safetyApproved
                    ? "隔离调理验收已登记；软件输入不替代急停/STO/安全继电器"
                    : "PCIE-1604 DI 为 0~5 V TTL，禁止直接接24 V限位/安全门；等待隔离调理和硬件安全回路验收"));

            devices.Add(Device(
                "motor",
                "安全带电机协议",
                DeviceConnectionState.NotConfigured,
                _motorProtocol.Status));

            foreach (var station in settings.Stations.Where(x => selected.Contains(x.StationId)))
            {
                var calibrated = !string.IsNullOrWhiteSpace(station.CalibrationRecordId) &&
                                 !station.CalibrationRecordId.Contains("待标定", StringComparison.OrdinalIgnoreCase);
                devices.Add(Device(
                    $"station-{station.StationId}",
                    station.Name,
                    calibrated ? DeviceConnectionState.Online : DeviceConnectionState.Warning,
                    $"CAN{station.CanChannel}/节点{station.CanNodeId} · {station.ForceChannel}/{station.CurrentChannel}/{station.VoltageChannel}/{station.DisplacementChannel} · 标定 {station.CalibrationRecordId}"));
                if (!calibrated)
                    faults.Add($"{station.Name} 缺少有效标定记录");
            }

            if (qualificationMissing.Count > 0)
                faults.Add("部署验收未完成：" + string.Join("、", qualificationMissing));
            faults.Add(_motorProtocol.Status);

            var requiredCanChannels = settings.Stations
                .Where(x => selected.Contains(x.StationId))
                .Select(x => x.CanChannel)
                .Distinct()
                .Order()
                .ToArray();
            var allHardwareConnected = _can?.IsConnected == true &&
                                       _can.ConnectedChannels.Order().SequenceEqual(requiredCanChannels) &&
                                       _analog?.IsConnected == true;
            _health = new SystemHealthSnapshot
            {
                Mode = RuntimeMode.Production,
                CanStartTest = false,
                Summary = allHardwareConnected
                    ? $"冻结硬件 CAN/DAQ 已连接，但正式启动仍锁定：{string.Join("；", faults)}"
                    : $"冻结硬件自检未通过：{string.Join("；", faults)}",
                Devices = devices
            };
            RaiseHealthChanged();
            return OperationResult.Fail(_health.Summary);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public Task<OperationResult> BeginPullAsync(int stationId, TestSettings settings, CancellationToken cancellationToken = default) =>
        SendMotorCommandAsync(stationId, MotorCommand.Pull, settings, cancellationToken);
    public Task<OperationResult> BeginHoldAsync(int stationId, TestSettings settings, CancellationToken cancellationToken = default) =>
        SendMotorCommandAsync(stationId, MotorCommand.Hold, settings, cancellationToken);
    public Task<OperationResult> BeginReturnAsync(int stationId, TestSettings settings, CancellationToken cancellationToken = default) =>
        SendMotorCommandAsync(stationId, MotorCommand.Return, settings, cancellationToken);
    public Task<OperationResult> PauseAsync(int stationId, CancellationToken cancellationToken = default) =>
        SendMotorCommandAsync(stationId, MotorCommand.Pause, _settings, cancellationToken);
    public Task<OperationResult> ResetAsync(int stationId, CancellationToken cancellationToken = default) =>
        SendMotorCommandAsync(stationId, MotorCommand.Reset, _settings, cancellationToken);
    public Task<OperationResult> StopAsync(int stationId, CancellationToken cancellationToken = default) =>
        SendMotorCommandAsync(stationId, MotorCommand.Stop, _settings, cancellationToken);

    public async Task<IReadOnlyList<LiveSample>> ReadSamplesAsync(
        IReadOnlyCollection<int> stationIds,
        int cycle,
        string phase,
        CancellationToken cancellationToken = default)
    {
        if (_settings is null || _analog?.IsConnected != true)
            throw new InvalidOperationException("PCIE-1604 尚未通过连接自检。");
        var requested = stationIds.Distinct().OrderBy(x => x).ToArray();
        if (requested.Length == 0 || requested.Any(id => !_selectedStationIds.Contains(id)))
            throw new InvalidOperationException("批量读取请求包含未连接的工位。");

        var snapshot = await _analog.ReadAsync(cancellationToken).ConfigureAwait(false);
        var samples = new List<LiveSample>(requested.Length);
        foreach (var station in _settings.Stations.Where(x => requested.Contains(x.StationId)).OrderBy(x => x.StationId))
        {
            var forceVoltage = GetVoltage(snapshot, station.ForceChannel);
            var currentVoltage = GetVoltage(snapshot, station.CurrentChannel);
            var voltageVoltage = GetVoltage(snapshot, station.VoltageChannel);
            var displacementVoltage = GetVoltage(snapshot, station.DisplacementChannel);

            ValidateElectricalSignal(station.ForceChannel, forceVoltage, _settings.ForceSignalType);
            ValidateElectricalSignal(station.CurrentChannel, currentVoltage, _settings.CurrentSignalType);
            ValidateElectricalSignal(station.VoltageChannel, voltageVoltage, _settings.VoltageSignalType);
            ValidateElectricalSignal(station.DisplacementChannel, displacementVoltage, _settings.DisplacementSignalType);

            samples.Add(new LiveSample
            {
                StationId = station.StationId,
                StationName = station.Name,
                Time = snapshot.Timestamp,
                Force = P881BSignalConverter.ToEngineeringValue(forceVoltage, _settings.ForceSignalType, _settings.SensorRange,
                    station.ForceCalibrationGain, station.ForceCalibrationOffset),
                Current = P881BSignalConverter.ToEngineeringValue(currentVoltage, _settings.CurrentSignalType, _settings.CurrentSensorRange,
                    station.CurrentCalibrationGain, station.CurrentCalibrationOffset),
                Voltage = P881BSignalConverter.ToEngineeringValue(voltageVoltage, _settings.VoltageSignalType, _settings.VoltageSensorRange,
                    station.VoltageCalibrationGain, station.VoltageCalibrationOffset),
                Displacement = P881BSignalConverter.ToEngineeringValue(displacementVoltage, _settings.DisplacementSignalType, _settings.DisplacementSensorRange,
                    station.DisplacementCalibrationGain, station.DisplacementCalibrationOffset),
                AcquisitionSequence = snapshot.Sequence,
                DigitalInputs = snapshot.DigitalInputs,
                ForceInputVoltage = forceVoltage,
                CurrentInputVoltage = currentVoltage,
                VoltageInputVoltage = voltageVoltage,
                DisplacementInputVoltage = displacementVoltage,
                DataQuality = "有效",
                ControllerFrame = _lastControllerFrames.GetValueOrDefault(station.StationId, string.Empty),
                Cycle = cycle,
                Phase = phase
            });
        }
        return samples;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _connectionGate.WaitAsync().ConfigureAwait(false);
        try { await DisconnectDevicesAsync().ConfigureAwait(false); }
        finally
        {
            _connectionGate.Release();
            _connectionGate.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private async Task ConnectCanAsync(
        TestSettings settings,
        IReadOnlyCollection<int> selected,
        ICollection<DeviceStatus> devices,
        ICollection<string> faults,
        CancellationToken cancellationToken)
    {
        _can = new ZlgUsbCanFdTransport(new ZlgUsbCanFdOptions(
            settings.CanDeviceIndex,
            settings.CanBusMode,
            settings.CanDataBaudRate,
            settings.CanFdStandard,
            settings.CanTerminationEnabled,
            settings.CanTransmitTimeout));
        _can.FrameReceived += CanFrameReceived;
        _can.ConnectionStateChanged += CanConnectionStateChanged;

        try
        {
            var stations = settings.Stations.Where(x => selected.Contains(x.StationId)).ToArray();
            var requiredChannels = stations.Select(x => x.CanChannel).Distinct().Order().ToArray();
            foreach (var channel in requiredChannels)
                await _can.ConnectAsync(channel, settings.CanBaudRate, cancellationToken).ConfigureAwait(false);
            if (!_can.ConnectedChannels.Order().SequenceEqual(requiredChannels))
                throw new IOException("USBCANFD-200U 未完成全部所需 CAN 通道的连接。");
            var channelText = string.Join("、", _can.ConnectedChannels.Select(x => $"CAN{x}"));
            devices.Add(Device("can", CanHardwareBaseline.DisplayName, DeviceConnectionState.Online,
                $"USB索引 {settings.CanDeviceIndex} · {channelText} · {settings.CanBusMode} · {settings.CanBaudRate / 1000} kbps"));
        }
        catch (Exception ex)
        {
            faults.Add("CAN：" + ex.Message);
            devices.Add(Device("can", CanHardwareBaseline.DisplayName, DeviceConnectionState.Fault, ex.Message));
            if (_can is not null)
            {
                _can.FrameReceived -= CanFrameReceived;
                _can.ConnectionStateChanged -= CanConnectionStateChanged;
                await _can.DisposeAsync().ConfigureAwait(false);
                _can = null;
            }
        }
    }

    private async Task ConnectAnalogAsync(
        TestSettings settings,
        IReadOnlyCollection<int> selected,
        ICollection<DeviceStatus> devices,
        ICollection<string> faults,
        CancellationToken cancellationToken)
    {
        _analog = new Pcie1604Acquisition();
        try
        {
            var requests = BuildAnalogRequests(settings, selected);
            var topology = settings.AnalogInputMode == AnalogHardwareBaseline.DifferentialMode
                ? AnalogInputTopology.Differential
                : AnalogInputTopology.SingleEnded;
            await _analog.ConnectAsync(new AnalogAcquisitionConfiguration(
                settings.AnalogBoardId,
                settings.AnalogScanRate,
                settings.AnalogReadTimeout,
                settings.FilterWindow,
                topology,
                requests), cancellationToken).ConfigureAwait(false);
            var physicalChannelCount = requests.Sum(x => x.Differential ? 2 : 1);
            devices.Add(Device("analog", AnalogHardwareBaseline.DisplayName, DeviceConnectionState.Online,
                $"{_analog.DeviceSummary} · {settings.AnalogInputMode} · {requests.Count}测量点/{physicalChannelCount}物理AI · {settings.AnalogScanRate}扫描/s"));
        }
        catch (Exception ex)
        {
            faults.Add("DAQ：" + ex.Message);
            devices.Add(Device("analog", AnalogHardwareBaseline.DisplayName, DeviceConnectionState.Fault, ex.Message));
        }
    }

    private static IReadOnlyList<AnalogChannelRequest> BuildAnalogRequests(TestSettings settings, IReadOnlyCollection<int> selected)
    {
        var differential = settings.AnalogInputMode == AnalogHardwareBaseline.DifferentialMode;
        var requests = new List<AnalogChannelRequest>();
        foreach (var station in settings.Stations.Where(x => selected.Contains(x.StationId)))
        {
            Add(station.ForceChannel, settings.ForceSignalType);
            Add(station.CurrentChannel, settings.CurrentSignalType);
            Add(station.VoltageChannel, settings.VoltageSignalType);
            Add(station.DisplacementChannel, settings.DisplacementSignalType);
        }
        return requests.GroupBy(x => x.Channel).Select(x => x.First()).OrderBy(x => x.Channel).ToArray();

        void Add(string channelText, string signalType)
        {
            if (!AnalogHardwareBaseline.TryParseAnalogChannel(channelText, out var channel))
                throw new InvalidOperationException($"无效 PCIE-1604 通道：{channelText}。");
            requests.Add(new AnalogChannelRequest(channel, P881BSignalConverter.SelectInputRange(signalType), differential));
        }
    }

    private static double GetVoltage(AnalogSnapshot snapshot, string channelText)
    {
        if (!AnalogHardwareBaseline.TryParseAnalogChannel(channelText, out var channel) ||
            !snapshot.ChannelVoltages.TryGetValue(channel, out var value))
        {
            throw new IOException($"PCIE-1604 快照中缺少 {channelText} 数据。");
        }
        return value;
    }

    private static void ValidateElectricalSignal(string channel, double voltage, string signalType)
    {
        if (!P881BSignalConverter.IsElectricalSignalPlausible(voltage, signalType))
            throw new IOException($"{channel} 电气信号异常：{voltage:0.000} V，不符合 {signalType} 预期范围。");
    }

    private void CanFrameReceived(object? sender, CanFrame frame)
    {
        if (_settings is null) return;
        var stations = _settings.Stations.Where(x => x.CanChannel == frame.Channel).ToArray();
        var hex = $"{frame.Id:X8}  {BitConverter.ToString(frame.Data).Replace('-', ' ')} · HWTS {frame.HardwareTimestampMicroseconds} us";
        if (stations.Length == 1)
        {
            _lastControllerFrames[stations[0].StationId] = hex;
            return;
        }

        // 在 DBC/字节协议尚未冻结前，不能仅凭“同一 CAN 通道”把一帧报文
        // 归给该通道的所有工位。保留原始帧和硬件时间戳，同时明确标为未归属。
        foreach (var station in stations)
            _lastControllerFrames[station.StationId] = $"共享CAN{frame.Channel}未归属 · {hex}";
    }

    private void CanConnectionStateChanged(object? sender, EventArgs e)
    {
        if (_can?.IsConnected != false) return;
        _health = new SystemHealthSnapshot
        {
            Mode = RuntimeMode.Production,
            CanStartTest = false,
            Summary = _can.LastError ?? "USBCANFD-200U 已离线。",
            Devices = _health.Devices.Select(x => x.Key == "can"
                ? Device("can", CanHardwareBaseline.DisplayName, DeviceConnectionState.Fault,
                    _can.LastError ?? "USB CAN 连接已断开")
                : x).ToArray()
        };
        RaiseHealthChanged();
    }

    private async Task DisconnectDevicesAsync()
    {
        if (_can is not null)
        {
            _can.FrameReceived -= CanFrameReceived;
            _can.ConnectionStateChanged -= CanConnectionStateChanged;
            await _can.DisposeAsync().ConfigureAwait(false);
            _can = null;
        }
        if (_analog is not null)
        {
            await _analog.DisposeAsync().ConfigureAwait(false);
            _analog = null;
        }
        _lastControllerFrames.Clear();
    }

    private async Task<OperationResult> SendMotorCommandAsync(
        int stationId,
        MotorCommand command,
        TestSettings? settings,
        CancellationToken cancellationToken)
    {
        if (settings is null) return OperationResult.Fail("尚未加载现场硬件配置。");
        var station = settings.Stations.FirstOrDefault(x => x.StationId == stationId && x.Enabled);
        if (station is null) return OperationResult.Fail($"工位 {stationId} 未启用或无硬件映射。");
        var encoded = _motorProtocol.TryEncode(command, station, settings, out var frame);
        if (!encoded.Success || frame is null) return encoded;
        if (_can?.IsConnected != true) return OperationResult.Fail("USBCANFD-200U 尚未连接。");
        await _can.SendAsync(frame, cancellationToken).ConfigureAwait(false);
        _lastControllerFrames[stationId] = $"TX {frame.Id:X8}  {BitConverter.ToString(frame.Data).Replace('-', ' ')}";
        return OperationResult.Ok($"{station.Name} {command} 命令已发送。");
    }

    private void RaiseHealthChanged() => HealthChanged?.Invoke(this, _health);

    private static SystemHealthSnapshot CreateInitialHealth() => new()
    {
        Mode = RuntimeMode.Production,
        CanStartTest = false,
        Summary = "已加载冻结硬件组合适配器，等待连接 USBCANFD-200U 与 PCIE-1604；正式动作仍受协议和安全验收门槛锁定。",
        Devices =
        [
            Device("can", CanHardwareBaseline.DisplayName, DeviceConnectionState.Disconnected, "等待连接 USB CAN 设备"),
            Device("analog", AnalogHardwareBaseline.DisplayName, DeviceConnectionState.Disconnected, "等待打开 PCIe 板卡和 x64 SDK"),
            Device("terminal", AnalogHardwareBaseline.TerminalDisplayName, DeviceConnectionState.Warning, "等待厂家书面兼容确认和逐针测试"),
            Device("motor", "安全带电机协议", DeviceConnectionState.NotConfigured, PendingMotorProtocolEncoder.PendingReason),
            Device("safety", "硬件安全回路与隔离输入", DeviceConnectionState.NotConfigured, "等待安全回路验收")
        ]
    };

    private static DeviceStatus Device(string key, string name, DeviceConnectionState state, string message) => new()
    {
        Key = key,
        Name = name,
        State = state,
        Message = message,
        UpdatedAt = DateTime.Now
    };
}
