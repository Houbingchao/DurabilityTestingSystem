using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

public sealed class DemoTestEngine : ITestEngine
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Random _random = new(8675309);
    private TestSettings _settings = new();
    private DateTime _lastTick;
    private double _simulatedSeconds;
    private int _cycle;

    public TestRunState State { get; private set; } = TestRunState.Ready;
    public RuntimeMode Mode => RuntimeMode.Demo;
    public TimeSpan Elapsed => TimeSpan.FromSeconds(_simulatedSeconds / DemoSpeed);
    public double PeakForce { get; private set; }
    public int CurrentCycle => _cycle;
    public const double DemoSpeed = 8.0;
    public SystemHealthSnapshot Health { get; private set; } = CreateDemoHealth();

    public event EventHandler<LiveSample>? SampleReceived;
    public event EventHandler<TestRunState>? StateChanged;
    public event EventHandler<SystemHealthSnapshot>? HealthChanged;

    public DemoTestEngine()
    {
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += Tick;
    }

    public void ApplySettings(TestSettings settings)
    {
        _settings = settings;
        _timer.Interval = Math.Clamp(settings.SampleInterval, 50, 1000);
    }

    public Task<OperationResult> ConnectAndSelfCheckAsync(CancellationToken cancellationToken = default)
    {
        Health = CreateDemoHealth();
        HealthChanged?.Invoke(this, Health);
        return Task.FromResult(OperationResult.Ok("演示模式自检完成，所有设备状态均为模拟。"));
    }

    public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (State == TestRunState.Running) return Task.FromResult(OperationResult.Ok("试验已在运行。"));
        var validation = SettingsValidator.Validate(_settings);
        if (!validation.Success) return Task.FromResult(validation);
        _lastTick = DateTime.Now;
        State = TestRunState.Running;
        _timer.Start();
        StateChanged?.Invoke(this, State);
        return Task.FromResult(OperationResult.Ok("演示试验已启动。"));
    }

    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        if (State != TestRunState.Running) return Task.FromResult(OperationResult.Fail("当前试验不在运行状态。"));
        _timer.Stop();
        State = TestRunState.Paused;
        StateChanged?.Invoke(this, State);
        return Task.FromResult(OperationResult.Ok("演示试验已暂停。"));
    }

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        _timer.Stop();
        State = TestRunState.Ready;
        StateChanged?.Invoke(this, State);
        return Task.FromResult(OperationResult.Ok("演示试验已停止。"));
    }

    public Task<OperationResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        _timer.Stop();
        _simulatedSeconds = 0;
        _cycle = 0;
        PeakForce = 0;
        State = TestRunState.Ready;
        StateChanged?.Invoke(this, State);
        SampleReceived?.Invoke(this, new LiveSample());
        return Task.FromResult(OperationResult.Ok("演示试验已复位。"));
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var delta = Math.Clamp((now - _lastTick).TotalSeconds, 0, 1) * DemoSpeed;
        _lastTick = now;
        _simulatedSeconds += delta;

        var pull = Math.Max(.2, _settings.PullDuration);
        var hold = Math.Max(.2, _settings.HoldDuration);
        var back = Math.Max(.2, _settings.ReturnDuration);
        var cycleDuration = pull + hold + back;
        _cycle = Math.Min(_settings.TargetCycles, (int)(_simulatedSeconds / cycleDuration) + 1);
        var local = _simulatedSeconds % cycleDuration;

        double normalized;
        string phase;
        if (local < pull)
        {
            normalized = Ease(local / pull);
            phase = "正向拉伸";
        }
        else if (local < pull + hold)
        {
            normalized = 1;
            phase = "负载保持";
        }
        else
        {
            normalized = 1 - Ease((local - pull - hold) / back);
            phase = "反向回程";
        }

        var ripple = Math.Sin(_simulatedSeconds * 8.7) * 3.8 + (_random.NextDouble() - .5) * 3;
        var force = Math.Max(0, normalized * _settings.TargetForce + ripple * normalized);
        var current = 0.42 + normalized * 3.35 + Math.Sin(_simulatedSeconds * 4.1) * .08;
        var voltage = 47.8 + Math.Sin(_simulatedSeconds * .9) * .35 - normalized * .22;
        var position = normalized * 320;
        PeakForce = Math.Max(PeakForce, force);

        SampleReceived?.Invoke(this, new LiveSample
        {
            Time = now,
            Force = force,
            Current = current,
            Voltage = voltage,
            Position = position,
            Cycle = _cycle,
            Phase = phase
        });

        if (_cycle >= _settings.TargetCycles)
        {
            _timer.Stop();
            State = TestRunState.Completed;
            StateChanged?.Invoke(this, State);
        }
    }

    private static double Ease(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }

    private static SystemHealthSnapshot CreateDemoHealth() => new()
    {
        Mode = RuntimeMode.Demo,
        CanStartTest = true,
        Summary = "演示模式：数据与设备状态由内置模拟器生成。",
        Devices =
        [
            Simulated("can", "CAN 通讯卡", "模拟在线 · 500 kbps"),
            Simulated("analog", "模拟量采集", "模拟在线 · 10 Hz"),
            Simulated("motor", "安全带电机", "模拟使能就绪"),
            Simulated("safety", "安全联锁", "模拟回路正常")
        ]
    };

    private static DeviceStatus Simulated(string key, string name, string message) => new()
    {
        Key = key,
        Name = name,
        State = DeviceConnectionState.Online,
        Message = message
    };

    public void Dispose() => _timer.Dispose();
}
