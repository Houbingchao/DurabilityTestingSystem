namespace DurabilityTestingSystem.Infrastructure;

/// <summary>
/// CAN 卡适配层。接入真实硬件时，使用厂家 SDK 实现该接口，页面层无需引用厂家 DLL。
/// </summary>
public interface ICanTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    string? LastError { get; }
    event EventHandler<CanFrame>? FrameReceived;
    event EventHandler? ConnectionStateChanged;
    Task ConnectAsync(int channel, int baudRate, CancellationToken cancellationToken = default);
    Task SendAsync(CanFrame frame, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}

public sealed record CanFrame(
    uint Id,
    byte[] Data,
    DateTime Timestamp,
    bool IsExtended = false,
    int Channel = 0,
    bool IsFd = false,
    bool BitRateSwitch = false,
    ulong HardwareTimestampMicroseconds = 0,
    uint NativeId = 0);

/// <summary>
/// 模拟量采集适配层。正式项目由新超仁达 PCIE-1604 实现；接口保留通道化语义，
/// 使上层不依赖厂家 DLL 的结构体和函数调用约定。
/// </summary>
public interface IAnalogAcquisition : IAsyncDisposable
{
    bool IsConnected { get; }
    string DeviceSummary { get; }
    string? LastError { get; }
    Task ConnectAsync(AnalogAcquisitionConfiguration configuration, CancellationToken cancellationToken = default);
    Task<AnalogSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}

public sealed record AnalogSnapshot(
    DateTime Timestamp,
    IReadOnlyDictionary<int, double> ChannelVoltages,
    ushort DigitalInputs,
    long Sequence);

public enum AnalogInputTopology
{
    SingleEnded,
    Differential
}

public enum AnalogVoltageRange
{
    PlusMinus1V = 0,
    PlusMinus2V = 1,
    PlusMinus5V = 2,
    PlusMinus10V = 3
}

public sealed record AnalogChannelRequest(
    int Channel,
    AnalogVoltageRange Range,
    bool Differential = false);

public sealed record AnalogAcquisitionConfiguration(
    int BoardId,
    int SampleRateHz,
    int ReadTimeoutMilliseconds,
    int FilterWindow,
    AnalogInputTopology InputTopology,
    IReadOnlyList<AnalogChannelRequest> Channels);

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
