using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

/// <summary>
/// 厂家硬件适配器的高层边界。正式适配器内部可组合 CAN、模拟量采集和数字量安全输入。
/// </summary>
public interface IHardwarePlatform : IAsyncDisposable
{
    bool IsConfigured { get; }
    SystemHealthSnapshot Health { get; }
    event EventHandler<SystemHealthSnapshot>? HealthChanged;

    Task<OperationResult> ConnectAndSelfCheckAsync(TestSettings settings, CancellationToken cancellationToken = default);
    Task<OperationResult> BeginPullAsync(TestSettings settings, CancellationToken cancellationToken = default);
    Task<OperationResult> BeginHoldAsync(TestSettings settings, CancellationToken cancellationToken = default);
    Task<OperationResult> BeginReturnAsync(TestSettings settings, CancellationToken cancellationToken = default);
    Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ResetAsync(CancellationToken cancellationToken = default);
    Task<LiveSample> ReadSampleAsync(int cycle, string phase, CancellationToken cancellationToken = default);
}

public interface IHardwarePlatformFactory
{
    IHardwarePlatform Create();
}

public sealed class UnconfiguredHardwarePlatform : IHardwarePlatform
{
    private readonly string _reason;

    public UnconfiguredHardwarePlatform(string reason) => _reason = reason;

    public bool IsConfigured => false;
    public SystemHealthSnapshot Health => new()
    {
        Mode = RuntimeMode.Production,
        CanStartTest = false,
        Summary = _reason,
        Devices =
        [
            Offline("can", "CAN 通讯卡"),
            Offline("analog", "模拟量采集"),
            Offline("motor", "安全带电机"),
            Offline("safety", "安全联锁")
        ]
    };

    public event EventHandler<SystemHealthSnapshot>? HealthChanged;

    public Task<OperationResult> ConnectAndSelfCheckAsync(TestSettings settings, CancellationToken cancellationToken = default)
    {
        HealthChanged?.Invoke(this, Health);
        return Task.FromResult(OperationResult.Fail(_reason));
    }

    public Task<OperationResult> BeginPullAsync(TestSettings settings, CancellationToken cancellationToken = default) => Fail();
    public Task<OperationResult> BeginHoldAsync(TestSettings settings, CancellationToken cancellationToken = default) => Fail();
    public Task<OperationResult> BeginReturnAsync(TestSettings settings, CancellationToken cancellationToken = default) => Fail();
    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default) => Fail();
    public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok("未配置硬件，无需停机。"));
    public Task<OperationResult> ResetAsync(CancellationToken cancellationToken = default) => Fail();

    public Task<LiveSample> ReadSampleAsync(int cycle, string phase, CancellationToken cancellationToken = default) =>
        Task.FromException<LiveSample>(new InvalidOperationException(_reason));

    private Task<OperationResult> Fail() => Task.FromResult(OperationResult.Fail(_reason));

    private DeviceStatus Offline(string key, string name) => new()
    {
        Key = key,
        Name = name,
        State = DeviceConnectionState.NotConfigured,
        Message = _reason
    };

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
