using System.Diagnostics;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

/// <summary>
/// 正式多工位试验执行器。UI 只发出高层命令；厂家报文、批量采集和现场联锁由平台适配器实现。
/// 当前基线按“命令已成功提交 + 阶段计时”顺序执行且绝不跨阶段补计循环；正式解锁前仍必须
/// 由 DBC/字节协议适配器把命令 ACK、动作完成、驱动状态和节点心跳纳入平台成功条件。
/// </summary>
public sealed class HardwareTestEngine : ITestEngine
{
    private const string PullPhase = "正向拉伸";
    private const string HoldPhase = "负载保持";
    private const string ReturnPhase = "反向回程";
    private const string IntervalPhase = "动作间隔";

    private readonly IHardwarePlatform _platform;
    private readonly SystemProfile _profile;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _elapsedStopwatch = new();
    private readonly Stopwatch _phaseStopwatch = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly List<int> _activeStationIds = [];
    private readonly Dictionary<int, StationRuntimeStatus> _stationStatuses = [];
    private readonly Dictionary<(int StationId, string Signal), long> _pendingViolations = [];
    private TestSettings _settings = new();
    private bool _tickBusy;
    private bool _disposed;
    private bool _stopConfirmed = true;
    private int _configurationGeneration;
    private int _connectedConfigurationGeneration = -1;
    private int _cycle;
    private string _phase = "待机";

    public HardwareTestEngine(IHardwarePlatform platform, SystemProfile profile)
    {
        _platform = platform;
        _profile = profile;
        _settings.EnsureStationConfigurations();
        InitializeStationStatuses();
        SynchronizeActiveStations();
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += async (_, _) => await TickAsync();
        _platform.HealthChanged += PlatformOnHealthChanged;
    }

    public RuntimeMode Mode => RuntimeMode.Production;
    public TestRunState State { get; private set; } = TestRunState.Ready;
    public TimeSpan Elapsed => _elapsedStopwatch.Elapsed;
    public double PeakForce => _stationStatuses.Values.Select(x => x.PeakForce).DefaultIfEmpty().Max();
    public int CurrentCycle => _cycle;
    public SystemHealthSnapshot Health => _platform.Health;
    public IReadOnlyList<int> ActiveStationIds => _activeStationIds;
    public IReadOnlyDictionary<int, StationRuntimeStatus> StationStatuses => _stationStatuses;
    public bool IsOperationInProgress => _operationGate.CurrentCount == 0 || _tickBusy;

    public event EventHandler<LiveSample>? SampleReceived;
    public event EventHandler<TestRunState>? StateChanged;
    public event EventHandler<SystemHealthSnapshot>? HealthChanged;

    public void ApplySettings(TestSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
            throw new InvalidOperationException("运行、暂停或报警锁存期间不能更换参数/配方快照。");

        settings.EnsureStationConfigurations();
        _settings = settings;
        _timer.Interval = Math.Clamp(settings.SampleInterval, 20, 5000);
        InitializeStationStatuses();
        SynchronizeActiveStations();
        MarkConfigurationChanged();
    }

    public OperationResult ConfigureActiveStations(IReadOnlyCollection<int> stationIds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selected = stationIds.Distinct().OrderBy(x => x).ToArray();
        if (selected.Length == 0) return OperationResult.Fail("请至少选择一个试验工位。");
        if (selected.Length > StationTopology.MaximumStationCount || selected.Any(x => !StationTopology.IsSupported(x)))
            return OperationResult.Fail($"最多只能同时选择 {StationTopology.MaximumStationCount} 个工位（{StationTopology.CapacityDescription}）。");
        var unavailable = selected.Where(id => !_settings.Stations.Any(x => x.StationId == id && x.Enabled)).ToArray();
        if (unavailable.Length > 0) return OperationResult.Fail($"工位 {string.Join("、", unavailable)} 未在参数设置中启用。");
        if (State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
            return OperationResult.Fail("运行、暂停或报警锁存期间不能改变工位选择。");
        if (_activeStationIds.SequenceEqual(selected)) return OperationResult.Ok($"已选择 {selected.Length} 个工位。");

        _activeStationIds.Clear();
        _activeStationIds.AddRange(selected);
        MarkConfigurationChanged();
        return OperationResult.Ok($"已选择 {selected.Length} 个工位。");
    }

    public async Task<OperationResult> ConnectAndSelfCheckAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
            return OperationResult.Fail("运行、暂停或报警锁存期间禁止重连设备。");

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
                return OperationResult.Fail("运行、暂停或报警锁存期间禁止重连设备。");
            var generationAtStart = _configurationGeneration;
            var result = await _platform.ConnectAndSelfCheckAsync(
                _profile, _settings, _activeStationIds, cancellationToken).ConfigureAwait(true);
            if (generationAtStart != _configurationGeneration)
                return OperationResult.Fail("自检期间参数或工位发生变化，本次自检结果作废，请重新连接与自检。");
            if (result.Success && _platform.Health.CanStartTest)
                _connectedConfigurationGeneration = _configurationGeneration;
            HealthChanged?.Invoke(this, _platform.Health);
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (State == TestRunState.Running) return OperationResult.Ok("试验已在运行。");
            if (State == TestRunState.Alarm) return OperationResult.Fail("报警已锁存；必须先确认停机、排除故障并复位。");
            if (State == TestRunState.Completed) return OperationResult.Fail("上一轮试验已完成，请先执行复位再开始新试验。");
            if (_activeStationIds.Count == 0) return OperationResult.Fail("请至少选择一个试验工位。");

            var validation = SettingsValidator.Validate(_settings);
            if (!validation.Success) return validation;

            if (_connectedConfigurationGeneration != _configurationGeneration || !_platform.Health.CanStartTest)
            {
                var check = await _platform.ConnectAndSelfCheckAsync(
                    _profile, _settings, _activeStationIds, cancellationToken).ConfigureAwait(true);
                HealthChanged?.Invoke(this, _platform.Health);
                if (!check.Success || !_platform.Health.CanStartTest) return OperationResult.Fail(check.Message);
                _connectedConfigurationGeneration = _configurationGeneration;
            }

            var resuming = State == TestRunState.Paused;
            if (!resuming)
            {
                _cycle = 1;
                _phase = PullPhase;
                _elapsedStopwatch.Reset();
                _phaseStopwatch.Reset();
                ResetRuntimeStatuses();
            }

            var command = await ExecutePhaseCommandAsync(_phase, cancellationToken).ConfigureAwait(true);
            if (!command.Success) return await EnterAlarmWhileGateHeldAsync(command.Message).ConfigureAwait(true);

            _stopConfirmed = false;
            _elapsedStopwatch.Start();
            if (resuming) _phaseStopwatch.Start();
            else _phaseStopwatch.Restart();
            SetState(TestRunState.Running);
            foreach (var id in _activeStationIds) _stationStatuses[id].State = TestRunState.Running;
            _timer.Start();
            return OperationResult.Ok($"正式试验已启动，共 {_activeStationIds.Count} 个工位并行运行。");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Stop();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (State != TestRunState.Running) return OperationResult.Fail("当前试验不在运行状态。");
            var result = await ExecuteForStationsAsync(
                (id, token) => _platform.PauseAsync(id, token), cancellationToken).ConfigureAwait(true);
            if (!result.Success) return await EnterAlarmWhileGateHeldAsync(result.Message).ConfigureAwait(true);
            _elapsedStopwatch.Stop();
            _phaseStopwatch.Stop();
            SetState(TestRunState.Paused);
            foreach (var id in _activeStationIds) _stationStatuses[id].State = TestRunState.Paused;
            return OperationResult.Ok("全部选中工位已暂停；本轮参数和配方快照保持冻结。");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Stop();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            _elapsedStopwatch.Stop();
            _phaseStopwatch.Stop();
            var result = await StopAllBestEffortAsync(cancellationToken).ConfigureAwait(true);
            _stopConfirmed = result.Success;
            if (!result.Success)
            {
                SetAlarmState("停机未全部确认：" + result.Message);
                return OperationResult.Fail("停机未全部确认，报警保持锁存；请使用硬件急停/STO并检查各工位。" + Environment.NewLine + result.Message);
            }

            if (State == TestRunState.Alarm)
            {
                foreach (var id in _activeStationIds)
                    _stationStatuses[id].Message = "停机已确认；排除故障后可执行人工复位";
                return OperationResult.Ok("全部工位停机已确认，报警仍保持锁存，等待人工复位。");
            }

            SetState(TestRunState.Ready);
            foreach (var id in _activeStationIds) _stationStatuses[id].State = TestRunState.Ready;
            return OperationResult.Ok("全部选中工位已停止并确认。");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State is TestRunState.Running or TestRunState.Paused)
            return OperationResult.Fail("运行或暂停状态禁止复位；请先安全停机。");
        if (!_stopConfirmed)
            return OperationResult.Fail("尚未确认全部工位已停止，禁止复位。");

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (State is TestRunState.Running or TestRunState.Paused)
                return OperationResult.Fail("运行或暂停状态禁止复位；请先安全停机。");
            if (!_stopConfirmed)
                return OperationResult.Fail("尚未确认全部工位已停止，禁止复位。");
            var result = await ExecuteForStationsAsync(
                (id, token) => _platform.ResetAsync(id, token), cancellationToken).ConfigureAwait(true);
            if (!result.Success)
            {
                SetAlarmState("复位命令失败：" + result.Message);
                return result;
            }

            var samples = await _platform.ReadSamplesAsync(
                _activeStationIds, 0, "复位确认", cancellationToken).ConfigureAwait(true);
            var resetFailure = samples.FirstOrDefault(x =>
                !double.IsFinite(x.Displacement) ||
                Math.Abs(x.Displacement) > _settings.ResetDisplacementTolerance);
            if (resetFailure is not null)
            {
                SetAlarmState($"{resetFailure.StationName} 复位确认失败：位移 {resetFailure.Displacement:0.###} mm");
                return OperationResult.Fail($"{resetFailure.StationName} 未回到复位容差 ±{_settings.ResetDisplacementTolerance:0.###} mm 内。");
            }
            if (!_platform.Health.CanStartTest)
            {
                SetAlarmState("复位后硬件自检状态不允许启动：" + _platform.Health.Summary);
                return OperationResult.Fail(_platform.Health.Summary);
            }

            _elapsedStopwatch.Reset();
            _phaseStopwatch.Reset();
            _cycle = 0;
            _phase = "待机";
            ResetRuntimeStatuses();
            SetState(TestRunState.Ready);
            foreach (var sample in samples) SampleReceived?.Invoke(this, sample);
            return OperationResult.Ok("全部工位已复位，并通过位移与硬件状态确认。");
        }
        catch (Exception ex)
        {
            SetAlarmState("复位确认异常：" + ex.Message);
            return OperationResult.Fail("复位确认失败：" + ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task TickAsync()
    {
        if (_tickBusy || State != TestRunState.Running || _disposed) return;
        _tickBusy = true;
        try
        {
            await _operationGate.WaitAsync().ConfigureAwait(true);
            try
            {
                if (State != TestRunState.Running) return;
                if (!_platform.Health.CanStartTest)
                {
                    await EnterAlarmWhileGateHeldAsync(_platform.Health.Summary).ConfigureAwait(true);
                    return;
                }

                var samples = await _platform.ReadSamplesAsync(
                    _activeStationIds, _cycle, _phase, CancellationToken.None).ConfigureAwait(true);
                if (samples.Count != _activeStationIds.Count ||
                    !samples.Select(x => x.StationId).Order().SequenceEqual(_activeStationIds.Order()))
                {
                    await EnterAlarmWhileGateHeldAsync("PCIE-1604 批量扫描未返回全部选中工位的数据。").ConfigureAwait(true);
                    return;
                }

                foreach (var sample in samples)
                {
                    UpdateAndPublishSample(sample);
                    var violation = ValidateProtectionWithDelay(sample);
                    if (violation is not null)
                    {
                        await EnterAlarmWhileGateHeldAsync($"{sample.StationName}：{violation}").ConfigureAwait(true);
                        return;
                    }
                }

                if (_phaseStopwatch.Elapsed < PhaseDuration(_phase)) return;
                await AdvanceOnePhaseAsync().ConfigureAwait(true);
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (Exception ex)
        {
            await _operationGate.WaitAsync().ConfigureAwait(true);
            try { await EnterAlarmWhileGateHeldAsync($"采集或控制异常：{ex.Message}").ConfigureAwait(true); }
            finally { _operationGate.Release(); }
        }
        finally
        {
            _tickBusy = false;
        }
    }

    private async Task AdvanceOnePhaseAsync()
    {
        string nextPhase;
        switch (_phase)
        {
            case PullPhase:
                nextPhase = HoldPhase;
                break;
            case HoldPhase:
                nextPhase = ReturnPhase;
                break;
            case ReturnPhase:
                nextPhase = IntervalPhase;
                break;
            case IntervalPhase:
            {
                var notReset = _activeStationIds
                    .Where(id => Math.Abs(_stationStatuses[id].LastSample?.Displacement ?? double.PositiveInfinity) >
                                 _settings.ResetDisplacementTolerance)
                    .ToArray();
                if (notReset.Length > 0)
                {
                    await EnterAlarmWhileGateHeldAsync(
                        $"{string.Join("、", notReset.Select(x => $"工位{x}"))} 弹簧/机构未复位，位移超过 {_settings.ResetDisplacementTolerance:0.0} mm。").ConfigureAwait(true);
                    return;
                }

                if (_cycle >= _settings.TargetCycles)
                {
                    _timer.Stop();
                    _elapsedStopwatch.Stop();
                    _phaseStopwatch.Stop();
                    var stopResult = await StopAllBestEffortAsync(CancellationToken.None).ConfigureAwait(true);
                    _stopConfirmed = stopResult.Success;
                    if (!stopResult.Success)
                    {
                        SetAlarmState("完成试验时停机未全部确认：" + stopResult.Message);
                        return;
                    }
                    SetState(TestRunState.Completed);
                    foreach (var id in _activeStationIds)
                    {
                        _stationStatuses[id].State = TestRunState.Completed;
                        _stationStatuses[id].Message = "试验完成，已确认停机";
                    }
                    return;
                }

                _cycle++;
                nextPhase = PullPhase;
                break;
            }
            default:
                await EnterAlarmWhileGateHeldAsync($"未知试验阶段：{_phase}").ConfigureAwait(true);
                return;
        }

        var transition = await ExecutePhaseCommandAsync(nextPhase, CancellationToken.None).ConfigureAwait(true);
        if (!transition.Success)
        {
            await EnterAlarmWhileGateHeldAsync(transition.Message).ConfigureAwait(true);
            return;
        }
        _phase = nextPhase;
        _phaseStopwatch.Restart();
    }

    private void UpdateAndPublishSample(LiveSample sample)
    {
        if (!double.IsFinite(sample.Force) || !double.IsFinite(sample.Current) ||
            !double.IsFinite(sample.Voltage) || !double.IsFinite(sample.Displacement))
            throw new IOException($"{sample.StationName} 返回 NaN/Infinity，无效采样必须立即停机。");
        if (!string.Equals(sample.DataQuality, "有效", StringComparison.OrdinalIgnoreCase))
            throw new IOException($"{sample.StationName} 数据质量无效：{sample.DataQuality}");

        var status = _stationStatuses[sample.StationId];
        status.CurrentCycle = sample.Cycle;
        status.PeakForce = Math.Max(status.PeakForce, sample.Force);
        status.PeakDisplacement = Math.Max(status.PeakDisplacement, Math.Abs(sample.Displacement));
        status.Phase = sample.Phase;
        status.LastSample = sample;
        status.Message = "运行正常";
        // 先发布并进入持久化缓冲，再判断保护；触发保护的原始样本不能被丢失。
        SampleReceived?.Invoke(this, sample);
    }

    private (string Key, string Message)? ValidateProtection(LiveSample sample)
    {
        if (sample.Force > _settings.MaxForceProtection) return ("force", $"拉力 {sample.Force:0.0} N 超过保护上限");
        if (Math.Abs(sample.Current) > _settings.MaxCurrentProtection) return ("current", $"电流 {sample.Current:0.00} A 超过保护上限");
        if (Math.Abs(sample.Voltage) > _settings.MaxVoltageProtection) return ("voltage", $"电压 {sample.Voltage:0.00} V 超过保护上限");
        if (Math.Abs(sample.Displacement) > _settings.MaxDisplacementProtection) return ("displacement", $"位移 {sample.Displacement:0.00} mm 超过保护上限");
        return null;
    }

    private string? ValidateProtectionWithDelay(LiveSample sample)
    {
        var violation = ValidateProtection(sample);
        if (violation is null)
        {
            foreach (var pendingKey in _pendingViolations.Keys.Where(x => x.StationId == sample.StationId).ToArray())
                _pendingViolations.Remove(pendingKey);
            return null;
        }

        var (signal, message) = violation.Value;
        foreach (var stale in _pendingViolations.Keys
                     .Where(x => x.StationId == sample.StationId && x.Signal != signal).ToArray())
            _pendingViolations.Remove(stale);
        var key = (sample.StationId, signal);
        if (!_pendingViolations.TryGetValue(key, out var since))
        {
            since = Stopwatch.GetTimestamp();
            _pendingViolations[key] = since;
        }
        var duration = Stopwatch.GetElapsedTime(since).TotalMilliseconds;
        return duration >= _settings.OverLimitDelay ? $"{message}，连续 {duration:0} ms" : null;
    }

    private TimeSpan PhaseDuration(string phase) => phase switch
    {
        PullPhase => TimeSpan.FromSeconds(Math.Max(.05, _settings.PullDuration)),
        HoldPhase => TimeSpan.FromSeconds(Math.Max(0, _settings.HoldDuration)),
        ReturnPhase => TimeSpan.FromSeconds(Math.Max(.05, _settings.ReturnDuration)),
        IntervalPhase => TimeSpan.FromSeconds(Math.Max(0, _settings.ActionInterval)),
        _ => TimeSpan.Zero
    };

    private Task<OperationResult> ExecutePhaseCommandAsync(string phase, CancellationToken token) => phase switch
    {
        PullPhase => ExecuteForStationsAsync((id, ct) => _platform.BeginPullAsync(id, _settings, ct), token),
        HoldPhase => ExecuteForStationsAsync((id, ct) => _platform.BeginHoldAsync(id, _settings, ct), token),
        ReturnPhase => ExecuteForStationsAsync((id, ct) => _platform.BeginReturnAsync(id, _settings, ct), token),
        IntervalPhase => ExecuteForStationsAsync((id, ct) => _platform.PauseAsync(id, ct), token),
        _ => Task.FromResult(OperationResult.Fail($"未知试验阶段：{phase}"))
    };

    private async Task<OperationResult> ExecuteForStationsAsync(
        Func<int, CancellationToken, Task<OperationResult>> operation,
        CancellationToken cancellationToken)
    {
        foreach (var stationId in _activeStationIds)
        {
            var result = await operation(stationId, cancellationToken).ConfigureAwait(false);
            if (!result.Success) return OperationResult.Fail($"工位 {stationId}：{result.Message}");
        }
        return OperationResult.Ok();
    }

    private async Task<OperationResult> StopAllBestEffortAsync(CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var stationId in _activeStationIds)
        {
            try
            {
                var timeout = TimeSpan.FromMilliseconds(Math.Clamp(_settings.CommunicationTimeout, 250, 10_000));
                var result = await _platform.StopAsync(stationId, cancellationToken)
                    .WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                if (!result.Success) failures.Add($"工位 {stationId}：{result.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"工位 {stationId}：{ex.Message}");
            }
        }
        return failures.Count == 0
            ? OperationResult.Ok("全部工位均已收到并确认停机。")
            : OperationResult.Fail(string.Join("；", failures));
    }

    private async Task<OperationResult> EnterAlarmWhileGateHeldAsync(string message)
    {
        _timer.Stop();
        _elapsedStopwatch.Stop();
        _phaseStopwatch.Stop();
        var stop = await StopAllBestEffortAsync(CancellationToken.None).ConfigureAwait(true);
        _stopConfirmed = stop.Success;
        var fullMessage = stop.Success ? message : $"{message}；停机未全部确认：{stop.Message}";
        SetAlarmState(fullMessage);
        return OperationResult.Fail(fullMessage);
    }

    private void SetAlarmState(string message)
    {
        foreach (var id in _activeStationIds)
        {
            _stationStatuses[id].State = TestRunState.Alarm;
            _stationStatuses[id].Message = message;
        }
        SetState(TestRunState.Alarm);
    }

    private void InitializeStationStatuses()
    {
        var validStationIds = _settings.Stations.Select(x => x.StationId).ToHashSet();
        foreach (var staleId in _stationStatuses.Keys.Where(x => !validStationIds.Contains(x)).ToArray())
            _stationStatuses.Remove(staleId);
        foreach (var station in _settings.Stations)
        {
            if (!_stationStatuses.ContainsKey(station.StationId))
                _stationStatuses[station.StationId] = new StationRuntimeStatus { StationId = station.StationId, StationName = station.Name };
            else
                _stationStatuses[station.StationId].StationName = station.Name;
        }
    }

    private void SynchronizeActiveStations()
    {
        var enabled = _settings.Stations.Where(x => x.Enabled).Select(x => x.StationId).ToHashSet();
        if (_activeStationIds.Count > 0 && _activeStationIds.All(enabled.Contains)) return;
        _activeStationIds.Clear();
        _activeStationIds.AddRange(_settings.Stations
            .Where(x => x.Enabled)
            .OrderBy(x => x.StationId)
            .Take(StationTopology.StandardStationCount)
            .Select(x => x.StationId));
    }

    private void ResetRuntimeStatuses()
    {
        foreach (var status in _stationStatuses.Values)
        {
            status.State = TestRunState.Ready;
            status.CurrentCycle = 0;
            status.PeakForce = 0;
            status.PeakDisplacement = 0;
            status.Phase = "待机";
            status.Message = "就绪";
            status.LastSample = null;
        }
        _pendingViolations.Clear();
        _stopConfirmed = true;
    }

    private void MarkConfigurationChanged()
    {
        _configurationGeneration++;
        _connectedConfigurationGeneration = -1;
    }

    private void SetState(TestRunState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void PlatformOnHealthChanged(object? sender, SystemHealthSnapshot health) =>
        HealthChanged?.Invoke(this, health);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _elapsedStopwatch.Stop();
        _phaseStopwatch.Stop();
        _platform.HealthChanged -= PlatformOnHealthChanged;
        if (State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
        {
            try
            {
                StopAllBestEffortAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
                // 最终危险能量切断必须由急停/STO/安全继电器保证；异常会由现场验收故障注入覆盖。
            }
        }
        _timer.Dispose();
        _platform.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        _operationGate.Dispose();
    }
}
