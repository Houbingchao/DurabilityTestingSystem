using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

/// <summary>
/// 试验执行引擎统一接口。UI 只依赖本接口，不直接依赖模拟器或厂家 SDK。
/// </summary>
public interface ITestEngine : IDisposable
{
    RuntimeMode Mode { get; }
    TestRunState State { get; }
    TimeSpan Elapsed { get; }
    double PeakForce { get; }
    int CurrentCycle { get; }
    SystemHealthSnapshot Health { get; }
    IReadOnlyList<int> ActiveStationIds { get; }
    IReadOnlyDictionary<int, StationRuntimeStatus> StationStatuses { get; }
    bool IsOperationInProgress { get; }

    event EventHandler<LiveSample>? SampleReceived;
    event EventHandler<TestRunState>? StateChanged;
    event EventHandler<SystemHealthSnapshot>? HealthChanged;

    void ApplySettings(TestSettings settings);
    OperationResult ConfigureActiveStations(IReadOnlyCollection<int> stationIds);
    Task<OperationResult> ConnectAndSelfCheckAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> StartAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> ResetAsync(CancellationToken cancellationToken = default);
}
