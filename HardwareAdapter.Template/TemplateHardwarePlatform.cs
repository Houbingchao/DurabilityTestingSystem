using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.HardwareAdapter.Template;

/// <summary>
/// 现场硬件适配器起始模板。
/// 安装最终 CAN 卡、采集卡并取得厂家 SDK/协议后，在这个项目中引用厂家 DLL，
/// 再逐项替换 TODO。模板始终返回“未就绪”，因此不会误启动电机。
/// </summary>
public sealed class TemplateHardwarePlatform : IHardwarePlatform
{
    private const string NotReady = "硬件适配器模板尚未完成，请实现厂家 SDK 与安全联锁后再启用。";
    private SystemHealthSnapshot _health = CreateHealth(NotReady);

    public bool IsConfigured => false;
    public SystemHealthSnapshot Health => _health;
    public event EventHandler<SystemHealthSnapshot>? HealthChanged;

    public Task<OperationResult> ConnectAndSelfCheckAsync(
        TestSettings settings,
        CancellationToken cancellationToken = default)
    {
        // TODO 1: 打开 CAN 通道，核对波特率、终端电阻和驱动器心跳。
        // TODO 2: 打开模拟量采集设备，读取各通道并检查断线/超量程。
        // TODO 3: 读取急停、安全门、正反限位；安全回路不健康时必须失败。
        // TODO 4: 只有全部关键设备通过后，才将 CanStartTest 设置为 true。
        _health = CreateHealth(NotReady);
        HealthChanged?.Invoke(this, _health);
        return Task.FromResult(OperationResult.Fail(NotReady));
    }

    public Task<OperationResult> BeginPullAsync(TestSettings settings, CancellationToken cancellationToken = default)
    {
        // TODO: 按驱动器协议发送使能、方向、速度/位置/力矩指令，并校验应答。
        return NotImplemented();
    }

    public Task<OperationResult> BeginHoldAsync(TestSettings settings, CancellationToken cancellationToken = default)
    {
        // TODO: 进入保持阶段；如采用力闭环，应在独立、限幅的控制器中实现。
        return NotImplemented();
    }

    public Task<OperationResult> BeginReturnAsync(TestSettings settings, CancellationToken cancellationToken = default)
    {
        // TODO: 低速回程，正反限位必须在硬件和软件两层生效。
        return NotImplemented();
    }

    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default) => NotImplemented();

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        // TODO: 此方法应发送驱动器停止/去使能；即使通讯失败，也应由硬件安全回路停机。
        return Task.FromResult(OperationResult.Ok("模板未连接硬件，无需发送停止命令。"));
    }

    public Task<OperationResult> ResetAsync(CancellationToken cancellationToken = default) => NotImplemented();

    public Task<LiveSample> ReadSampleAsync(int cycle, string phase, CancellationToken cancellationToken = default)
    {
        // TODO: 一次读取拉力、电流、电压和位置，完成标定、单位换算、时间戳和质量判断。
        return Task.FromException<LiveSample>(new InvalidOperationException(NotReady));
    }

    public ValueTask DisposeAsync()
    {
        // TODO: 关闭 CAN、采集卡与厂家 SDK 句柄。
        return ValueTask.CompletedTask;
    }

    private static Task<OperationResult> NotImplemented() =>
        Task.FromResult(OperationResult.Fail(NotReady));

    private static SystemHealthSnapshot CreateHealth(string message) => new()
    {
        Mode = RuntimeMode.Production,
        CanStartTest = false,
        Summary = message,
        Devices =
        [
            Device("can", "CAN 通讯卡", message),
            Device("analog", "模拟量采集", message),
            Device("motor", "安全带电机", message),
            Device("safety", "安全联锁", message)
        ]
    };

    private static DeviceStatus Device(string key, string name, string message) => new()
    {
        Key = key,
        Name = name,
        State = DeviceConnectionState.NotConfigured,
        Message = message
    };
}
