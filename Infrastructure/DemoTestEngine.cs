using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

public sealed class DemoTestEngine : ITestEngine
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Random _random = new(8675309);
    private readonly Dictionary<int, StationRuntimeStatus> _stationStatuses = [];
    private readonly List<int> _activeStationIds = [];
    private TestSettings _settings = new();
    private DateTime _lastTick;
    private double _simulatedSeconds;
    private int _cycle;
    private long _sampleSequence;

    public TestRunState State { get; private set; } = TestRunState.Ready;
    public RuntimeMode Mode => RuntimeMode.Demo;
    public TimeSpan Elapsed => TimeSpan.FromSeconds(_simulatedSeconds / DemoSpeed);
    public double PeakForce => _stationStatuses.Values.Select(x => x.PeakForce).DefaultIfEmpty().Max();
    public int CurrentCycle => _cycle;
    public const double DemoSpeed = 8.0;
    public SystemHealthSnapshot Health { get; private set; }
    public IReadOnlyList<int> ActiveStationIds => _activeStationIds;
    public IReadOnlyDictionary<int, StationRuntimeStatus> StationStatuses => _stationStatuses;
    public bool IsOperationInProgress => false;

    public event EventHandler<LiveSample>? SampleReceived;
    public event EventHandler<TestRunState>? StateChanged;
    public event EventHandler<SystemHealthSnapshot>? HealthChanged;

    public DemoTestEngine()
    {
        _settings.EnsureStationConfigurations();
        InitializeStationStatuses();
        SynchronizeActiveStations();
        Health = CreateDemoHealth(_settings);
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += Tick;
    }

    public void ApplySettings(TestSettings settings)
    {
        settings.EnsureStationConfigurations();
        _settings = settings;
        _timer.Interval = Math.Clamp(settings.SampleInterval, 50, 1000);
        InitializeStationStatuses();
        SynchronizeActiveStations();
        Health = CreateDemoHealth(settings);
    }

    public OperationResult ConfigureActiveStations(IReadOnlyCollection<int> stationIds)
    {
        var selected = stationIds.Distinct().OrderBy(x => x).ToArray();
        if (selected.Length == 0) return OperationResult.Fail("请至少选择一个试验工位。");
        if (selected.Length > StationTopology.MaximumStationCount || selected.Any(x => !StationTopology.IsSupported(x)))
            return OperationResult.Fail($"最多只能同时选择 {StationTopology.MaximumStationCount} 个工位（{StationTopology.CapacityDescription}）。");
        var unavailable = selected.Where(id => !_settings.Stations.Any(x => x.StationId == id && x.Enabled)).ToArray();
        if (unavailable.Length > 0) return OperationResult.Fail($"工位 {string.Join("、", unavailable)} 未在参数设置中启用。");
        if (State is TestRunState.Running or TestRunState.Paused)
            return OperationResult.Fail("试验运行期间不能改变工位选择。");
        _activeStationIds.Clear();
        _activeStationIds.AddRange(selected);
        return OperationResult.Ok($"已选择 {selected.Length} 个工位：{string.Join("、", selected.Select(x => $"工位{x}"))}");
    }

    public Task<OperationResult> ConnectAndSelfCheckAsync(CancellationToken cancellationToken = default)
    {
        Health = CreateDemoHealth(_settings);
        HealthChanged?.Invoke(this, Health);
        return Task.FromResult(OperationResult.Ok($"演示模式自检完成，{_settings.Stations.Count(x => x.Enabled)} 个已启用工位状态均为模拟。"));
    }

    public Task<OperationResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (State == TestRunState.Running) return Task.FromResult(OperationResult.Ok("试验已在运行。"));
        var validation = SettingsValidator.Validate(_settings);
        if (!validation.Success) return Task.FromResult(validation);
        if (_activeStationIds.Count == 0) return Task.FromResult(OperationResult.Fail("请至少选择一个试验工位。"));
        if (State != TestRunState.Paused)
        {
            _simulatedSeconds = 0;
            _cycle = 1;
            _sampleSequence = 0;
            ResetRuntimeStatuses();
        }
        _lastTick = DateTime.Now;
        State = TestRunState.Running;
        foreach (var id in _activeStationIds) _stationStatuses[id].State = TestRunState.Running;
        _timer.Start();
        StateChanged?.Invoke(this, State);
        return Task.FromResult(OperationResult.Ok($"演示试验已启动，共 {_activeStationIds.Count} 个工位并行运行。"));
    }

    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        if (State != TestRunState.Running) return Task.FromResult(OperationResult.Fail("当前试验不在运行状态。"));
        _timer.Stop();
        State = TestRunState.Paused;
        foreach (var id in _activeStationIds) _stationStatuses[id].State = TestRunState.Paused;
        StateChanged?.Invoke(this, State);
        return Task.FromResult(OperationResult.Ok("全部选中工位已暂停。"));
    }

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        _timer.Stop();
        State = TestRunState.Ready;
        foreach (var id in _activeStationIds) _stationStatuses[id].State = TestRunState.Ready;
        StateChanged?.Invoke(this, State);
        return Task.FromResult(OperationResult.Ok("全部选中工位已停止。"));
    }

    public Task<OperationResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        _timer.Stop();
        _simulatedSeconds = 0;
        _cycle = 0;
        _sampleSequence = 0;
        State = TestRunState.Ready;
        ResetRuntimeStatuses();
        StateChanged?.Invoke(this, State);
        foreach (var id in _activeStationIds)
            SampleReceived?.Invoke(this, new LiveSample { StationId = id, StationName = StationName(id) });
        return Task.FromResult(OperationResult.Ok("全部工位状态已复位。"));
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
        var interval = Math.Max(0, _settings.ActionInterval);
        var cycleDuration = pull + hold + back + interval;
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
        else if (local < pull + hold + back)
        {
            normalized = 1 - Ease((local - pull - hold) / back);
            phase = "反向回程";
        }
        else
        {
            normalized = 0;
            phase = "动作间隔";
        }

        var acquisitionSequence = ++_sampleSequence;
        foreach (var stationId in _activeStationIds)
        {
            var phaseOffset = stationId * .37;
            var ripple = Math.Sin(_simulatedSeconds * 8.7 + phaseOffset) * 3.8 + (_random.NextDouble() - .5) * 3;
            var force = Math.Max(0, normalized * (_settings.TargetForce + stationId * 1.7) + ripple * normalized);
            var current = 0.35 + stationId * .08 + normalized * (11.8 + stationId * .35) + Math.Sin(_simulatedSeconds * 4.1 + phaseOffset) * .16;
            var voltage = 13.52 + Math.Sin(_simulatedSeconds * .9 + phaseOffset) * .08 - normalized * .12;
            var displacement = Math.Max(0, normalized * (64 + stationId * 1.2) + Math.Sin(_simulatedSeconds * 2.3 + phaseOffset) * .18);
            var status = _stationStatuses[stationId];
            status.CurrentCycle = _cycle;
            status.PeakForce = Math.Max(status.PeakForce, force);
            status.PeakDisplacement = Math.Max(status.PeakDisplacement, displacement);
            status.Phase = phase;
            status.Message = "模拟运行正常";
            var sample = new LiveSample
            {
                StationId = stationId,
                StationName = status.StationName,
                Time = now,
                Force = force,
                Current = current,
                Voltage = voltage,
                Displacement = displacement,
                AcquisitionSequence = acquisitionSequence,
                DigitalInputs = 0,
                DataQuality = "演示模拟",
                ControllerFrame = $"18FF{stationId:X2}01  {(_cycle & 0xFF):X2} {(int)(normalized * 255):X2} 00 00 00 00 00 00",
                Cycle = _cycle,
                Phase = phase
            };
            status.LastSample = sample;
            SampleReceived?.Invoke(this, sample);
        }

        if (_simulatedSeconds >= _settings.TargetCycles * cycleDuration)
        {
            _timer.Stop();
            State = TestRunState.Completed;
            foreach (var id in _activeStationIds) _stationStatuses[id].State = TestRunState.Completed;
            StateChanged?.Invoke(this, State);
        }
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
    }

    private string StationName(int id) => _settings.Stations.FirstOrDefault(x => x.StationId == id)?.Name ?? $"工位 {id}";

    private static double Ease(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }

    private static SystemHealthSnapshot CreateDemoHealth(TestSettings settings)
    {
        var devices = new List<DeviceStatus>
        {
            Simulated("can", "CAN 通讯卡", $"{CanHardwareBaseline.Model}（USB）模拟在线 · {settings.CanBusMode} · {settings.CanBaudRate / 1000} kbps"),
            Simulated("analog", $"{AnalogHardwareBaseline.Model} + {AnalogHardwareBaseline.TerminalModel}",
                $"三工位12路模拟采集在线 · {settings.AnalogInputMode} · {settings.AnalogScanRate} 扫描/s"),
            Simulated("motor", "安全带执行机构", $"{settings.Stations.Count(x => x.Enabled)} 个工位模拟就绪"),
            Simulated("safety", "安全联锁", "急停、限位与复位检测模拟正常")
        };
        devices.AddRange(settings.Stations.Where(x => x.Enabled).Select(x =>
            Simulated($"station-{x.StationId}", x.Name, $"节点 {x.CanNodeId} · 力/流/压/位移通道就绪")));
        return new SystemHealthSnapshot
        {
            Mode = RuntimeMode.Demo,
            CanStartTest = true,
            Summary = $"演示模式：{StationTopology.CapacityDescription}，当前启用 {settings.Stations.Count(x => x.Enabled)} 个工位。",
            Devices = devices
        };
    }

    private static DeviceStatus Simulated(string key, string name, string message) => new()
    {
        Key = key,
        Name = name,
        State = DeviceConnectionState.Online,
        Message = message
    };

    public void Dispose() => _timer.Dispose();
}
