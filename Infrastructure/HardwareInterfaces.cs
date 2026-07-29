namespace DurabilityTestingSystem.Infrastructure;

/// <summary>
/// CAN 卡适配层。接入真实硬件时，使用厂家 SDK 实现该接口，页面层无需引用厂家 DLL。
/// </summary>
public interface ICanTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    event EventHandler<CanFrame>? FrameReceived;
    Task ConnectAsync(int channel, int baudRate, CancellationToken cancellationToken = default);
    Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}

public sealed record CanFrame(uint Id, byte[] Data, DateTime Timestamp, bool IsExtended = false);

/// <summary>
/// 模拟量采集适配层。Modbus TCP、USB DAQ 或 PCIe 采集卡均可实现该接口。
/// </summary>
public interface IAnalogAcquisition : IAsyncDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task<AnalogSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}

public sealed record AnalogSnapshot(
    DateTime Timestamp,
    double ForceRaw,
    double CurrentRaw,
    double VoltageRaw,
    IReadOnlyDictionary<int, double>? AdditionalChannels = null);

/// <summary>
/// 硬件安全信号只读接口。急停和安全门的最终安全动作必须由硬件回路完成。
/// </summary>
public interface ISafetyInterlock
{
    bool EmergencyStopHealthy { get; }
    bool SafetyDoorClosed { get; }
    bool PositiveLimitActive { get; }
    bool NegativeLimitActive { get; }
    event EventHandler? StateChanged;
}
