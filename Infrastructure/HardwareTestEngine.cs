using System.Diagnostics;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

/// <summary>
/// 正式试验执行器。负责通用循环时序、采样、软件阈值保护和状态流转；
/// 具体 CAN 报文、模拟量读取与安全输入由 IHardwarePlatform 实现。
/// </summary>
public sealed class HardwareTestEngine : ITestEngine
{
    private readonly IHardwarePlatform _platform;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();
    private TestSettings _settings = new();
    private bool _tickBusy;
    private int _cycle;
    private string _phase = "待机";

    public HardwareTestEngine(IHardwarePlatform platform)
    {
        _platform = platform;
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += async (_, _) => await TickAsync();
        _platform.HealthChanged += (_, health) => HealthChanged?.Invoke(this, health);
    }

    public RuntimeMode Mode => RuntimeMode.Production;
    public TestRunState State { get; private set; } = TestRunState.Ready;
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public double PeakForce { get; private set; }
    public int CurrentCycle => _cycle;
    public SystemHealthSnapshot Health => _platform.Health;

    public event EventHandler<LiveSample>? SampleReceived;
    public event EventHandler<TestRunState>? StateChanged;
    public event EventHandler<SystemHealthSnapshot>? HealthChanged;

    public void ApplySettings(TestSettings settings)
    {
        _settings = settings;
        _timer.Interval = Math.Clamp(settings.SampleInterval, 20, 5000);
    }

    public async Task<OperationResult> ConnectAndSelfCheckAsync(CancellationToken cancellationToken = default)
    {
        var result = await _platform.ConnectAndSelfCheckAsync(_settings, cancellationToken);
        HealthChanged?.Invoke(this, _platform.Health);
        return result;
    }

    public async Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (State == TestRunState.Running) return OperationResult.Ok("试验已在运行。");
        if (State == TestRunState.Alarm) return OperationResult.Fail("系统处于报警状态，请排除故障并复位后再启动。");

        var validation = SettingsValidator.Validate(_settings);
        if (!validation.Success) return validation;

        if (!_platform.Health.CanStartTest)
        {
            var check = await ConnectAndSelfCheckAsync(cancellationToken);
            if (!check.Success || !_platform.Health.CanStartTest)
                return OperationResult.Fail(check.Message);
        }

        OperationResult command;
        if (State == TestRunState.Paused)
        {
            command = await ExecutePhaseCommandAsync(_phase, cancellationToken);
        }
        else
        {
            _cycle = 1;
            _phase = "正向拉伸";
            PeakForce = 0;
            _stopwatch.Reset();
            command = await _platform.BeginPullAsync(_settings, cancellationToken);
        }

        if (!command.Success) return await EnterAlarmAsync(command.Message);
        _stopwatch.Start();
        _timer.Start();
        SetState(TestRunState.Running);
        return OperationResult.Ok("正式试验已启动。");
    }

    public async Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        if (State != TestRunState.Running) return OperationResult.Fail("当前试验不在运行状态。");
        var result = await _platform.PauseAsync(cancellationToken);
        if (!result.Success) return await EnterAlarmAsync(result.Message);
        _timer.Stop();
        _stopwatch.Stop();
        SetState(TestRunState.Paused);
        return OperationResult.Ok("试验已暂停。");
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        _timer.Stop();
        _stopwatch.Stop();
        var result = await _platform.StopAsync(cancellationToken);
        if (State != TestRunState.Alarm) SetState(TestRunState.Ready);
        return result;
    }

    public async Task<OperationResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        if (State == TestRunState.Running) return OperationResult.Fail("请先停止试验，再执行复位。");
        var result = await _platform.ResetAsync(cancellationToken);
        if (!result.Success) return result;
        _stopwatch.Reset();
        _cycle = 0;
        _phase = "待机";
        PeakForce = 0;
        SetState(TestRunState.Ready);
        SampleReceived?.Invoke(this, new LiveSample());
        return OperationResult.Ok("硬件与试验状态已复位。");
    }

    private async Task TickAsync()
    {
        if (_tickBusy || State != TestRunState.Running) return;
        _tickBusy = true;
        try
        {
            if (!_platform.Health.CanStartTest)
            {
                await EnterAlarmAsync(_platform.Health.Summary);
                return;
            }

            var pull = Math.Max(.05, _settings.PullDuration);
            var hold = Math.Max(0, _settings.HoldDuration);
            var back = Math.Max(.05, _settings.ReturnDuration);
            var cycleDuration = pull + hold + back;
            var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            var calculatedCycle = Math.Min(_settings.TargetCycles, (int)(elapsedSeconds / cycleDuration) + 1);
            var local = elapsedSeconds % cycleDuration;
            var desiredPhase = local < pull
                ? "正向拉伸"
                : local < pull + hold
                    ? "负载保持"
                    : "反向回程";

            if (calculatedCycle != _cycle || desiredPhase != _phase)
            {
                _cycle = calculatedCycle;
                _phase = desiredPhase;
                var transition = await ExecutePhaseCommandAsync(_phase, CancellationToken.None);
                if (!transition.Success)
                {
                    await EnterAlarmAsync(transition.Message);
                    return;
                }
            }

            var sample = await _platform.ReadSampleAsync(_cycle, _phase, CancellationToken.None);
            PeakForce = Math.Max(PeakForce, sample.Force);
            SampleReceived?.Invoke(this, sample);

            var protectionMessage = ValidateProtection(sample);
            if (protectionMessage is not null)
            {
                await EnterAlarmAsync(protectionMessage);
                return;
            }

            if (_cycle >= _settings.TargetCycles && elapsedSeconds >= _settings.TargetCycles * cycleDuration)
            {
                _timer.Stop();
                _stopwatch.Stop();
                await _platform.StopAsync();
                SetState(TestRunState.Completed);
            }
        }
        catch (Exception ex)
        {
            await EnterAlarmAsync($"正式采集或控制异常：{ex.Message}");
        }
        finally
        {
            _tickBusy = false;
        }
    }

    private string? ValidateProtection(LiveSample sample)
    {
        if (sample.Force > _settings.MaxForceProtection)
            return $"拉力超出硬保护上限：{sample.Force:0.0} N > {_settings.MaxForceProtection:0.0} N";
        if (Math.Abs(sample.Current) > _settings.MaxCurrentProtection)
            return $"驱动电流超限：{sample.Current:0.00} A";
        if (Math.Abs(sample.Voltage) > _settings.MaxVoltageProtection)
            return $"母线电压超限：{sample.Voltage:0.0} V";
        return null;
    }

    private Task<OperationResult> ExecutePhaseCommandAsync(string phase, CancellationToken cancellationToken) => phase switch
    {
        "正向拉伸" => _platform.BeginPullAsync(_settings, cancellationToken),
        "负载保持" => _platform.BeginHoldAsync(_settings, cancellationToken),
        "反向回程" => _platform.BeginReturnAsync(_settings, cancellationToken),
        _ => Task.FromResult(OperationResult.Fail($"未知试验阶段：{phase}"))
    };

    private async Task<OperationResult> EnterAlarmAsync(string message)
    {
        _timer.Stop();
        _stopwatch.Stop();
        try { await _platform.StopAsync(); } catch { /* 保留原始报警原因 */ }
        SetState(TestRunState.Alarm);
        return OperationResult.Fail(message);
    }

    private void SetState(TestRunState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _platform.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
