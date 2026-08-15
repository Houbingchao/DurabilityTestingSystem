using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.HardwareAdapter.Site;

internal enum MotorCommand
{
    Pull,
    Hold,
    Return,
    Pause,
    Stop,
    Reset
}

/// <summary>
/// 电机协议边界。客户提供并确认DBC或字节协议后，只替换编码器实现，
/// 不允许在UI、试验引擎或CAN驱动层散落报文ID和字节偏移。
/// </summary>
internal interface IMotorProtocolEncoder
{
    bool IsValidated { get; }
    string Status { get; }
    OperationResult TryEncode(
        MotorCommand command,
        StationConfiguration station,
        TestSettings settings,
        out CanFrame? frame);
}

/// <summary>
/// 当前安全占位实现：在协议未冻结时拒绝生成任何猜测报文。
/// </summary>
internal sealed class PendingMotorProtocolEncoder : IMotorProtocolEncoder
{
    public const string PendingReason = "电机 DBC/字节协议编解码尚未实现，禁止发送动作帧。";

    public bool IsValidated => false;
    public string Status => PendingReason;

    public OperationResult TryEncode(
        MotorCommand command,
        StationConfiguration station,
        TestSettings settings,
        out CanFrame? frame)
    {
        frame = null;
        return OperationResult.Fail(PendingReason);
    }
}
