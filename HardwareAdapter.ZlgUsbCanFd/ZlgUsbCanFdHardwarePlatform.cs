using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.HardwareAdapter.ZlgUsbCanFd;

/// <summary>
/// 当前现场硬件平台的第一阶段实现：真实接入周立功 USBCANFD-200U。
/// 电机报文、模拟量采集卡和安全 I/O 未冻结前，始终保持 CanStartTest=false。
/// </summary>
public sealed class ZlgUsbCanFdHardwarePlatform : IHardwarePlatform
{
    private const string PendingInterfaces = "CAN 卡已冻结；电机 DBC/字节协议、模拟量采集卡和安全 I/O 尚未冻结，禁止正式试验。";
    private ZlgUsbCanFdTransport? _can;
    private SystemHealthSnapshot _health = CreateInitialHealth();

    public bool IsConfigured => true;
    public SystemHealthSnapshot Health => _health;
    public event EventHandler<SystemHealthSnapshot>? HealthChanged;

    public async Task<OperationResult> ConnectAndSelfCheckAsync(
        SystemProfile profile,
        TestSettings settings,
        IReadOnlyCollection<int> stationIds,
        CancellationToken cancellationToken = default)
    {
        settings.EnsureStationConfigurations();
        if (stationIds.Count == 0 || stationIds.Any(id => !StationTopology.IsSupported(id)))
            return OperationResult.Fail($"工位只能在 1~{StationTopology.MaximumStationCount} 范围内选择。");

        var validation = ValidateCanSettings(settings);
        if (!validation.Success) return validation;

        var selectedStations = settings.Stations
            .Where(x => x.Enabled && stationIds.Contains(x.StationId))
            .ToArray();
        if (selectedStations.Length != stationIds.Distinct().Count())
            return OperationResult.Fail("选择中包含未启用或没有硬件映射的工位。");

        await DisposeCanAsync().ConfigureAwait(false);
        _can = new ZlgUsbCanFdTransport(new ZlgUsbCanFdOptions(
            settings.CanDeviceIndex,
            settings.CanBusMode,
            settings.CanDataBaudRate,
            settings.CanFdStandard,
            settings.CanTerminationEnabled,
            settings.CanTransmitTimeout));

        try
        {
            foreach (var channel in selectedStations.Select(x => x.CanChannel).Distinct().Order())
                await _can.ConnectAsync(channel, settings.CanBaudRate, cancellationToken).ConfigureAwait(false);

            var channelText = string.Join("、", _can.ConnectedChannels.Select(x => $"CAN{x}"));
            _health = new SystemHealthSnapshot
            {
                Mode = RuntimeMode.Production,
                CanStartTest = false,
                Summary = $"{_can.DeviceSummary} 已连接（{channelText}）；{PendingInterfaces}",
                Devices =
                [
                    Device("can", "周立功 USBCANFD-200U", DeviceConnectionState.Online,
                        $"USB 索引 {settings.CanDeviceIndex} · {channelText} · {settings.CanBusMode} · {settings.CanBaudRate / 1000} kbps"),
                    Device("motor", "安全带电机协议", DeviceConnectionState.NotConfigured, "等待冻结 DBC 或正式字节协议，不发送任何动作帧"),
                    Device("analog", "力/流/压/位移采集", DeviceConnectionState.NotConfigured, "等待模拟量采集卡型号、SDK 和量程标定"),
                    Device("safety", "安全联锁", DeviceConnectionState.NotConfigured, "等待急停、安全门、限位和使能回路 I/O 定义")
                ]
            };
            RaiseHealthChanged();
            return OperationResult.Fail($"USBCANFD-200U 连接成功，但完整设备自检未通过：{PendingInterfaces}");
        }
        catch (Exception ex)
        {
            _health = new SystemHealthSnapshot
            {
                Mode = RuntimeMode.Production,
                CanStartTest = false,
                Summary = $"USBCANFD-200U 连接失败：{ex.Message}",
                Devices =
                [
                    Device("can", "周立功 USBCANFD-200U", DeviceConnectionState.Fault, ex.Message),
                    Device("motor", "安全带电机协议", DeviceConnectionState.NotConfigured, "等待 DBC/字节协议"),
                    Device("analog", "力/流/压/位移采集", DeviceConnectionState.NotConfigured, "等待采集硬件"),
                    Device("safety", "安全联锁", DeviceConnectionState.NotConfigured, "等待 I/O 定义")
                ]
            };
            RaiseHealthChanged();
            await DisposeCanAsync().ConfigureAwait(false);
            return OperationResult.Fail(_health.Summary);
        }
    }

    public Task<OperationResult> BeginPullAsync(int stationId, TestSettings settings, CancellationToken cancellationToken = default) => ProtocolPending();
    public Task<OperationResult> BeginHoldAsync(int stationId, TestSettings settings, CancellationToken cancellationToken = default) => ProtocolPending();
    public Task<OperationResult> BeginReturnAsync(int stationId, TestSettings settings, CancellationToken cancellationToken = default) => ProtocolPending();
    public Task<OperationResult> PauseAsync(int stationId, CancellationToken cancellationToken = default) => ProtocolPending();
    public Task<OperationResult> ResetAsync(int stationId, CancellationToken cancellationToken = default) => ProtocolPending();

    public Task<OperationResult> StopAsync(int stationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Fail("电机停止报文尚未冻结，软件未发送未知 CAN 帧；危险运动必须由硬件急停/使能回路切断。"));

    public Task<IReadOnlyList<LiveSample>> ReadSamplesAsync(IReadOnlyCollection<int> stationIds, int cycle, string phase, CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<LiveSample>>(new InvalidOperationException("该适配器仅用于 CAN 单卡诊断，请使用 SiteHardwarePlatform 读取 PCIE-1604 数据。"));

    public async ValueTask DisposeAsync()
    {
        await DisposeCanAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task DisposeCanAsync()
    {
        if (_can is null) return;
        await _can.DisposeAsync().ConfigureAwait(false);
        _can = null;
    }

    private static Task<OperationResult> ProtocolPending() =>
        Task.FromResult(OperationResult.Fail("电机 DBC/字节协议尚未冻结，禁止发送动作命令。"));

    private static OperationResult ValidateCanSettings(TestSettings settings)
    {
        if (!string.Equals(settings.CanDevice, CanHardwareBaseline.DisplayName, StringComparison.Ordinal))
            return OperationResult.Fail($"CAN 设备必须使用已冻结型号：{CanHardwareBaseline.DisplayName}。");
        if (settings.CanDeviceIndex is < 0 or > 31)
            return OperationResult.Fail("USBCANFD-200U USB 设备索引必须在 0~31 范围内。");
        if (!CanHardwareBaseline.SupportedArbitrationBaudRates.Contains(settings.CanBaudRate))
            return OperationResult.Fail("USBCANFD-200U 仲裁域波特率不受支持。");
        if (settings.CanBusMode == "CAN FD" && !CanHardwareBaseline.SupportedDataBaudRates.Contains(settings.CanDataBaudRate))
            return OperationResult.Fail("USBCANFD-200U 数据域波特率不受支持。");
        if (settings.Stations.Where(x => x.Enabled).Any(x => x.CanChannel is < 0 or >= CanHardwareBaseline.ChannelCount))
            return OperationResult.Fail("USBCANFD-200U 只有 CAN0、CAN1 两个通道。");
        return OperationResult.Ok("USBCANFD-200U 配置校验通过。");
    }

    private void RaiseHealthChanged() => HealthChanged?.Invoke(this, _health);

    private static SystemHealthSnapshot CreateInitialHealth() => new()
    {
        Mode = RuntimeMode.Production,
        CanStartTest = false,
        Summary = $"已加载 {CanHardwareBaseline.DisplayName} 适配器，等待连接测试；{PendingInterfaces}",
        Devices =
        [
            Device("can", "周立功 USBCANFD-200U", DeviceConnectionState.Disconnected, "等待连接 USB 设备"),
            Device("motor", "安全带电机协议", DeviceConnectionState.NotConfigured, "等待 DBC/字节协议"),
            Device("analog", "力/流/压/位移采集", DeviceConnectionState.NotConfigured, "等待采集硬件"),
            Device("safety", "安全联锁", DeviceConnectionState.NotConfigured, "等待 I/O 定义")
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
