using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI.Pages;

public sealed class TestControlPage : UserControl
{
    private readonly AppDatabase _database;
    private readonly ITestEngine _engine;
    private readonly Func<TestSettings> _getSettings;
    private readonly TrendChart _chart;
    private readonly CycleProgress _progress;
    private readonly KpiCard _forceCard;
    private readonly KpiCard _currentCard;
    private readonly KpiCard _voltageCard;
    private readonly KpiCard _positionCard;
    private readonly Label _phaseValue;
    private readonly Label _elapsedValue;
    private readonly Label _peakValue;
    private readonly Label _frequencyValue;
    private readonly StatusPill _runStatus;
    private readonly Button _startButton;
    private readonly Button _pauseButton;
    private readonly Button _stopButton;
    private readonly TextBox _specimenText;
    private readonly ComboBox _planCombo;
    private readonly Dictionary<string, (Label Dot, Label State)> _deviceRows = [];
    private readonly List<TestSampleRecord> _sampleBuffer = [];
    private DateTime _startedAt;
    private DateTime _lastRecordedAt;
    private string _activeTestNo = string.Empty;
    private bool _recordSaved;

    public TestControlPage(AppDatabase database, ITestEngine engine, Func<TestSettings> getSettings)
    {
        _database = database;
        _engine = engine;
        _getSettings = getSettings;
        BackColor = Theme.Window;

        var headerCard = BuildTestHeader(out _specimenText, out _planCombo, out _runStatus);
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Window,
            Padding = new Padding(0, 14, 0, 0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 8, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 204));

        var kpis = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        for (var i = 0; i < 4; i++) kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _forceCard = new KpiCard("实时拉力", "0.0", "N", "目标 450 N", Theme.Primary) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 7, 4) };
        _currentCard = new KpiCard("驱动电流", "0.00", "A", "保护上限 8.0 A", Theme.Orange) { Dock = DockStyle.Fill, Margin = new Padding(7, 0, 7, 4) };
        _voltageCard = new KpiCard("母线电压", "0.0", "V", "额定 48 VDC", Theme.Green) { Dock = DockStyle.Fill, Margin = new Padding(7, 0, 7, 4) };
        _positionCard = new KpiCard("执行器位移", "0.0", "mm", "有效行程 320 mm", Theme.Purple) { Dock = DockStyle.Fill, Margin = new Padding(7, 0, 0, 4) };
        kpis.Controls.Add(_forceCard, 0, 0);
        kpis.Controls.Add(_currentCard, 1, 0);
        kpis.Controls.Add(_voltageCard, 2, 0);
        kpis.Controls.Add(_positionCard, 3, 0);

        var chartCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8), Padding = new Padding(5) };
        _chart = new TrendChart { Dock = DockStyle.Fill };
        chartCard.Controls.Add(_chart);

        var processCard = BuildProcessCard(out _phaseValue, out _elapsedValue);
        left.Controls.Add(kpis, 0, 0);
        left.Controls.Add(chartCard, 0, 1);
        left.Controls.Add(processCard, 0, 2);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Margin = new Padding(8, 0, 0, 0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 238));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var progressCard = BuildProgressCard(out _progress, out _peakValue, out _frequencyValue);
        var controlCard = BuildControlCard(out _startButton, out _pauseButton, out _stopButton);
        var deviceCard = BuildDeviceCard();
        right.Controls.Add(progressCard, 0, 0);
        right.Controls.Add(controlCard, 0, 1);
        right.Controls.Add(deviceCard, 0, 2);

        body.Controls.Add(left, 0, 0);
        body.Controls.Add(right, 1, 0);
        Controls.Add(body);
        Controls.Add(headerCard);

        WireEvents();
        RefreshSettings();
        UpdateDeviceStatus(_engine.Health);
    }

    public void RefreshSettings()
    {
        var settings = _getSettings();
        _progress.Maximum = settings.TargetCycles;
        _chart.ForceMax = Math.Max(700, settings.MaxForceProtection);
        _forceCard.Note = $"目标 {settings.TargetForce:0} N  ·  上限 {settings.ForceUpperLimit:0} N";
        _frequencyValue.Text = $"{1000.0 / Math.Max(1, settings.SampleInterval):0.0} Hz";
    }

    internal async void StartDemoForCapture()
    {
        if (_engine.State != TestRunState.Ready) return;
        _startedAt = DateTime.Now;
        _recordSaved = false;
        _engine.ApplySettings(_getSettings());
        await _engine.StartAsync();
    }

    private CardPanel BuildTestHeader(out TextBox specimenText, out ComboBox planCombo, out StatusPill status)
    {
        var card = new CardPanel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(18, 12, 18, 10) };
        var title = UiFactory.Label("当前试验", 11, Theme.Text, FontStyle.Bold);
        title.Location = new Point(18, 14);
        var caption = UiFactory.Label("请确认试件信息与执行方案后启动", 7.5f, Theme.Muted);
        caption.Location = new Point(18, 42);

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 850,
            Height = 64,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
            BackColor = Color.Transparent
        };
        fields.Controls.Add(InlineLabel("试件编号"));
        specimenText = UiFactory.TextBox($"SB26-{DateTime.Now:MMdd}-001");
        specimenText.Width = 170;
        fields.Controls.Add(specimenText);
        fields.Controls.Add(InlineLabel("试验方案"));
        planCombo = UiFactory.Combo(_database.GetPlans().Select(p => p.Name), "安全带卷收器标准耐久试验");
        planCombo.Width = 235;
        fields.Controls.Add(planCombo);
        fields.Controls.Add(InlineLabel("操作员"));
        var operatorText = UiFactory.TextBox("管理员");
        operatorText.Width = 90;
        operatorText.ReadOnly = true;
        operatorText.BackColor = Color.FromArgb(247, 249, 252);
        fields.Controls.Add(operatorText);
        status = new StatusPill { Caption = "系统就绪", StatusColor = Theme.Green, Margin = new Padding(16, 9, 0, 0), Size = new Size(92, 30) };
        fields.Controls.Add(status);

        card.Controls.Add(fields);
        card.Controls.Add(title);
        card.Controls.Add(caption);
        return card;
    }

    private static Label InlineLabel(string text) => new()
    {
        Text = text,
        Font = Theme.Font(8),
        ForeColor = Theme.Muted,
        AutoSize = false,
        Width = 65,
        Height = 40,
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(8, 2, 2, 0)
    };

    private CardPanel BuildProcessCard(out Label phaseValue, out Label elapsedValue)
    {
        var card = UiFactory.Card("循环流程监控", "当前执行步骤与耐久试验时序");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 7, 0, 0);
        card.Padding = new Padding(18, 58, 18, 14);

        var info = new Panel { Dock = DockStyle.Right, Width = 260, Padding = new Padding(16, 4, 0, 0) };
        var phaseTitle = UiFactory.Label("当前阶段", 8, Theme.Muted);
        phaseTitle.Location = new Point(18, 6);
        phaseValue = UiFactory.Label("待机", 12, Theme.Primary, FontStyle.Bold);
        phaseValue.Location = new Point(18, 28);
        var elapsedTitle = UiFactory.Label("运行时间", 8, Theme.Muted);
        elapsedTitle.Location = new Point(140, 6);
        elapsedValue = UiFactory.Label("00:00:00", 12, Theme.Text, FontStyle.Bold);
        elapsedValue.Location = new Point(140, 28);
        info.Controls.AddRange([phaseTitle, phaseValue, elapsedTitle, elapsedValue]);

        var steps = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Padding = new Padding(0, 3, 8, 0) };
        for (var i = 0; i < 5; i++) steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        var items = new[]
        {
            ("01", "正向拉伸", "2.0 s", Theme.Primary),
            ("02", "负载保持", "1.0 s", Theme.Orange),
            ("03", "反向回程", "2.0 s", Theme.Cyan),
            ("04", "原点确认", "自动", Theme.Green),
            ("05", "循环计数", "+1 次", Theme.Purple)
        };
        for (var i = 0; i < items.Length; i++)
        {
            var item = new Panel { Dock = DockStyle.Fill, Margin = new Padding(i == 0 ? 0 : 6, 3, 6, 1), BackColor = Color.FromArgb(247, 249, 252) };
            var number = UiFactory.Label(items[i].Item1, 8, items[i].Item4, FontStyle.Bold);
            number.Location = new Point(12, 10);
            var name = UiFactory.Label(items[i].Item2, 9, Theme.Text, FontStyle.Bold);
            name.Location = new Point(12, 35);
            var time = UiFactory.Label(items[i].Item3, 7.5f, Theme.Muted);
            time.Location = new Point(12, 60);
            item.Controls.AddRange([number, name, time]);
            steps.Controls.Add(item, i, 0);
        }
        card.Controls.Add(steps);
        card.Controls.Add(info);
        return card;
    }

    private CardPanel BuildProgressCard(out CycleProgress progress, out Label peakValue, out Label frequencyValue)
    {
        var card = UiFactory.Card("循环进度", "耐久循环完成情况");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, 7);
        progress = new CycleProgress
        {
            Location = new Point(9, 57),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Size = new Size(148, 166)
        };
        card.Controls.Add(progress);

        var separator = new Panel { BackColor = Theme.Border, Width = 1, Height = 132, Location = new Point(163, 72) };
        card.Controls.Add(separator);
        var peakTitle = UiFactory.Label("峰值拉力", 8, Theme.Muted);
        peakTitle.Location = new Point(180, 78);
        peakValue = UiFactory.Label("0.0 N", 13, Theme.Text, FontStyle.Bold);
        peakValue.Location = new Point(180, 101);
        var freqTitle = UiFactory.Label("采样频率", 8, Theme.Muted);
        freqTitle.Location = new Point(180, 145);
        frequencyValue = UiFactory.Label("10.0 Hz", 11, Theme.Primary, FontStyle.Bold);
        frequencyValue.Location = new Point(180, 168);
        card.Controls.AddRange([peakTitle, peakValue, freqTitle, frequencyValue]);
        return card;
    }

    private CardPanel BuildControlCard(out Button start, out Button pause, out Button stop)
    {
        var card = UiFactory.Card("试验操作", "设备动作前请确认防护门与急停状态");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 7, 0, 7);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        start = UiFactory.Button("▶  启动试验", Theme.Primary, Color.White);
        start.Dock = DockStyle.Fill;
        start.Margin = new Padding(0, 0, 5, 5);
        pause = UiFactory.Button("Ⅱ  暂停", Theme.Orange, Color.White);
        pause.Dock = DockStyle.Fill;
        pause.Margin = new Padding(5, 0, 0, 5);
        stop = UiFactory.Button("■  停止", Theme.Red, Color.White);
        stop.Dock = DockStyle.Fill;
        stop.Margin = new Padding(0, 5, 5, 0);
        var reset = UiFactory.SecondaryButton("↻  复位");
        reset.Dock = DockStyle.Fill;
        reset.Margin = new Padding(5, 5, 0, 0);
        reset.FlatAppearance.BorderSize = 1;
        reset.FlatAppearance.BorderColor = Theme.Border;
        reset.Click += (_, _) => ResetTest();
        buttons.Controls.Add(start, 0, 0);
        buttons.Controls.Add(pause, 1, 0);
        buttons.Controls.Add(stop, 0, 1);
        buttons.Controls.Add(reset, 1, 1);
        card.Controls.Add(buttons);
        return card;
    }

    private CardPanel BuildDeviceCard()
    {
        var card = UiFactory.Card("设备状态", "实时通讯与安全联锁");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 7, 0, 0);
        var rows = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        for (var i = 0; i < 5; i++) rows.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        var devices = new[]
        {
            ("mode", "运行模式", _engine.Mode == RuntimeMode.Demo ? "演示模拟" : "正式硬件", _engine.Mode == RuntimeMode.Demo ? Theme.Orange : Theme.Primary),
            ("can", "CAN 通讯卡", "状态未知", Theme.Muted),
            ("analog", "模拟量采集", "状态未知", Theme.Muted),
            ("motor", "安全带电机", "状态未知", Theme.Muted),
            ("safety", "安全联锁", "状态未知", Theme.Muted)
        };
        foreach (var (key, name, state, color) in devices)
        {
            var row = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var dot = UiFactory.Label("●", 8, color);
            dot.Location = new Point(2, 3);
            var nameLabel = UiFactory.Label(name, 8.5f, Theme.Text);
            nameLabel.Location = new Point(24, 2);
            var stateLabel = UiFactory.Label(state, 7.5f, Theme.Muted);
            stateLabel.AutoSize = false;
            stateLabel.Width = 160;
            stateLabel.Height = 20;
            stateLabel.TextAlign = ContentAlignment.MiddleRight;
            stateLabel.Dock = DockStyle.Right;
            stateLabel.Margin = new Padding(0);
            row.Controls.AddRange([dot, nameLabel, stateLabel]);
            rows.Controls.Add(row);
            _deviceRows[key] = (dot, stateLabel);
        }
        card.Controls.Add(rows);
        return card;
    }

    private void WireEvents()
    {
        _startButton.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_specimenText.Text))
            {
                MessageBox.Show("请先填写试件编号。", "试验提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _specimenText.Focus();
                return;
            }
            if (_engine.State == TestRunState.Ready && _engine.CurrentCycle == 0)
            {
                _startedAt = DateTime.Now;
                _recordSaved = false;
                _activeTestNo = $"T{DateTime.Now:yyyyMMdd-HHmmss}";
                _sampleBuffer.Clear();
                _lastRecordedAt = DateTime.MinValue;
            }
            _engine.ApplySettings(_getSettings());
            var result = await _engine.StartAsync();
            _database.AddLog(result.Success ? "信息" : "报警", "试验控制",
                result.Success ? $"启动试验：{_specimenText.Text}，编号 {_activeTestNo}" : result.Message);
            if (!result.Success)
                MessageBox.Show(result.Message, "试验启动失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        _pauseButton.Click += async (_, _) =>
        {
            var result = await _engine.PauseAsync();
            _database.AddLog(result.Success ? "警告" : "报警", "试验控制", result.Message);
        };
        _stopButton.Click += async (_, _) => await StopAndSaveAsync();
        _engine.SampleReceived += EngineOnSampleReceived;
        _engine.StateChanged += EngineOnStateChanged;
        _engine.HealthChanged += (_, health) => UpdateDeviceStatus(health);
    }

    private void EngineOnSampleReceived(object? sender, LiveSample sample)
    {
        _chart.AddSample(sample);
        _forceCard.Value = sample.Force.ToString("0.0");
        _currentCard.Value = sample.Current.ToString("0.00");
        _voltageCard.Value = sample.Voltage.ToString("0.0");
        _positionCard.Value = sample.Position.ToString("0.0");
        _progress.Value = sample.Cycle;
        _phaseValue.Text = sample.Phase;
        _elapsedValue.Text = _engine.Elapsed.ToString(@"hh\:mm\:ss");
        _peakValue.Text = $"{_engine.PeakForce:0.0} N";
        BufferSample(sample);
    }

    private async void EngineOnStateChanged(object? sender, TestRunState state)
    {
        var (caption, color) = state switch
        {
            TestRunState.Running => ("试验运行", Theme.Primary),
            TestRunState.Paused => ("试验暂停", Theme.Orange),
            TestRunState.Completed => ("试验完成", Theme.Green),
            TestRunState.Alarm => ("设备报警", Theme.Red),
            _ => ("系统就绪", Theme.Green)
        };
        _runStatus.Caption = caption;
        _runStatus.StatusColor = color;
        _startButton.Enabled = state != TestRunState.Running;
        _startButton.Text = state == TestRunState.Paused ? "▶  继续试验" : "▶  启动试验";
        _pauseButton.Enabled = state == TestRunState.Running;
        _stopButton.Enabled = state is TestRunState.Running or TestRunState.Paused;
        if (state == TestRunState.Completed)
        {
            SaveRecord("合格");
            _database.AddLog("信息", "试验控制", $"试验自动完成，共 {_engine.CurrentCycle:N0} 次循环");
        }
        else if (state == TestRunState.Alarm)
        {
            await _engine.StopAsync();
            SaveRecord("不合格");
            _database.AddLog("报警", "试验控制", "试验因设备或保护报警中止");
        }
    }

    private async Task StopAndSaveAsync()
    {
        if (_engine.State is not (TestRunState.Running or TestRunState.Paused)) return;
        var result = await _engine.StopAsync();
        if (!result.Success)
            MessageBox.Show(result.Message, "停止试验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        SaveRecord(_engine.PeakForce <= _getSettings().MaxForceProtection ? "合格" : "不合格");
        _database.AddLog("信息", "试验控制", $"试验停止，完成 {_engine.CurrentCycle:N0} 次循环");
    }

    private void SaveRecord(string result)
    {
        if (_recordSaved || _engine.CurrentCycle == 0) return;
        FlushSamples();
        _database.AddTestRecord(new TestRecord
        {
            TestNo = _activeTestNo,
            SpecimenNo = _specimenText.Text.Trim(),
            PlanName = _planCombo.Text,
            StartedAt = _startedAt,
            Duration = _engine.Elapsed,
            Cycles = _engine.CurrentCycle,
            PeakForce = _engine.PeakForce,
            Result = result,
            Operator = "管理员"
        });
        _recordSaved = true;
    }

    private async void ResetTest()
    {
        if (_engine.State == TestRunState.Running)
        {
            MessageBox.Show("请先停止试验，再执行复位。", "复位提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var result = await _engine.ResetAsync();
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "复位失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _chart.Clear();
        _progress.Value = 0;
        _phaseValue.Text = "待机";
        _elapsedValue.Text = "00:00:00";
        _peakValue.Text = "0.0 N";
        _database.AddLog("信息", "试验控制", "试验状态已复位");
    }

    private void BufferSample(LiveSample sample)
    {
        if (_engine.State != TestRunState.Running || string.IsNullOrWhiteSpace(_activeTestNo)) return;
        var interval = Math.Max(50, _getSettings().DataRecordInterval);
        if (_lastRecordedAt != DateTime.MinValue &&
            (sample.Time - _lastRecordedAt).TotalMilliseconds < interval) return;
        _lastRecordedAt = sample.Time;
        _sampleBuffer.Add(new TestSampleRecord
        {
            TestNo = _activeTestNo,
            Time = sample.Time,
            ElapsedMilliseconds = (long)_engine.Elapsed.TotalMilliseconds,
            Force = sample.Force,
            Current = sample.Current,
            Voltage = sample.Voltage,
            Position = sample.Position,
            Cycle = sample.Cycle,
            Phase = sample.Phase
        });
        if (_sampleBuffer.Count >= 100) FlushSamples();
    }

    private void FlushSamples()
    {
        if (_sampleBuffer.Count == 0) return;
        _database.AddTestSamples(_sampleBuffer.ToArray());
        _sampleBuffer.Clear();
    }

    private void UpdateDeviceStatus(SystemHealthSnapshot health)
    {
        if (_deviceRows.TryGetValue("mode", out var mode))
        {
            mode.State.Text = health.Mode == RuntimeMode.Demo ? "演示模拟" : "正式硬件";
            mode.Dot.ForeColor = health.Mode == RuntimeMode.Demo ? Theme.Orange : Theme.Primary;
        }
        foreach (var status in health.Devices)
        {
            if (!_deviceRows.TryGetValue(status.Key, out var row)) continue;
            row.State.Text = status.Message;
            row.Dot.ForeColor = status.State switch
            {
                DeviceConnectionState.Online => Theme.Green,
                DeviceConnectionState.Warning or DeviceConnectionState.Connecting => Theme.Orange,
                DeviceConnectionState.Fault => Theme.Red,
                _ => Theme.Muted
            };
        }
    }
}
