using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI.Pages;

public sealed class TestControlPage : UserControl
{
    internal bool RequiresFinalization => _engine.IsOperationInProgress ||
                                          _engine.State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm ||
                                          (!_recordSaved && _engine.CurrentCycle > 0) || _sampleBuffer.Count > 0;
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
    private readonly TextBox _operatorText;
    private readonly ComboBox _planCombo;
    private readonly ComboBox _monitorStationCombo;
    private readonly List<CheckBox> _stationChecks = [];
    private readonly Dictionary<string, (Label Dot, Label State)> _deviceRows = [];
    private readonly List<TestSampleRecord> _sampleBuffer = [];
    private readonly List<Label> _processStepDetails = [];
    private DateTime _startedAt;
    private readonly Dictionary<int, DateTime> _lastRecordedAt = [];
    private string _activeTestNo = string.Empty;
    private bool _recordSaved;
    private bool _refreshingPlans;
    private CompiledTestPlan? _preparedPlan;
    private CompiledTestPlan? _runningPlan;
    private TestSettings? _runningSettings;
    private string _runningSpecimenNo = string.Empty;
    private string _runningOperator = string.Empty;

    public TestControlPage(AppDatabase database, ITestEngine engine, Func<TestSettings> getSettings)
    {
        _database = database;
        _engine = engine;
        _getSettings = getSettings;
        BackColor = Theme.Window;

        var headerCard = BuildTestHeader(out _specimenText, out _operatorText, out _planCombo, out _runStatus, out _monitorStationCombo);
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
        settings.EnsureStationConfigurations();
        _chart.ForceMax = Math.Max(700, settings.MaxForceProtection);
        _chart.CurrentMax = Math.Max(50, settings.CurrentSensorRange);
        _chart.VoltageMax = Math.Max(20, settings.VoltageSensorRange);
        _chart.DisplacementMax = Math.Max(100, settings.DisplacementSensorRange);
        _currentCard.Note = $"工况 40 A  ·  保护 {settings.MaxCurrentProtection:0.0} A";
        _voltageCard.Note = $"工况 13.5 V  ·  保护 {settings.MaxVoltageProtection:0.0} V";
        _positionCard.Note = $"量程 {settings.DisplacementSensorRange:0} mm  ·  保护 {settings.MaxDisplacementProtection:0} mm";
        _frequencyValue.Text = $"{1000.0 / Math.Max(1, settings.SampleInterval):0.0} Hz";
        if (_engine.State is not (TestRunState.Running or TestRunState.Paused))
        {
            foreach (var check in _stationChecks)
            {
                var id = Convert.ToInt32(check.Tag);
                var station = settings.Stations.First(x => x.StationId == id);
                check.Enabled = station.Enabled;
                check.Text = station.Name;
                check.Checked = station.Enabled && id <= StationTopology.StandardStationCount;
            }
            RefreshMonitorStations();
        }
        RefreshPlans();
    }

    public void RefreshPlans(long? preferredPlanId = null)
    {
        if (_engine.State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm) return;

        var previousId = preferredPlanId
                         ?? (_planCombo.SelectedItem as PlanChoice)?.Id
                         ?? _preparedPlan?.PlanId;
        var enabledPlans = _database.GetPlans().Where(x => x.Enabled).ToArray();
        _refreshingPlans = true;
        try
        {
            _planCombo.Items.Clear();
            foreach (var plan in enabledPlans) _planCombo.Items.Add(new PlanChoice(plan));

            var selected = enabledPlans.FirstOrDefault(x => x.Id == previousId)
                           ?? enabledPlans.FirstOrDefault(x =>
                               string.Equals(x.Code, _getSettings().PlanCode, StringComparison.OrdinalIgnoreCase))
                           ?? enabledPlans.FirstOrDefault();
            if (selected is not null)
            {
                for (var index = 0; index < _planCombo.Items.Count; index++)
                {
                    if (_planCombo.Items[index] is PlanChoice choice && choice.Id == selected.Id)
                    {
                        _planCombo.SelectedIndex = index;
                        break;
                    }
                }
            }
            else
            {
                _planCombo.SelectedIndex = -1;
                _planCombo.Text = string.Empty;
            }
        }
        finally
        {
            _refreshingPlans = false;
        }

        PrepareSelectedPlan(showMessage: false);
    }

    public OperationResult ApplyPlan(CompiledTestPlan plan)
    {
        if (_engine.State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
            return OperationResult.Fail("试验运行、暂停或报警锁存期间不能更换方案。");

        RefreshPlans(plan.PlanId);
        if (_preparedPlan?.PlanId != plan.PlanId)
            return OperationResult.Fail("方案未能从数据库重新加载并通过启动前校验，请检查方案步骤和当前保护参数。");
        return OperationResult.Ok($"方案“{_preparedPlan.PlanName}”已应用到当前试验。");
    }

    private bool PrepareSelectedPlan(bool showMessage)
    {
        if (!TryCompileSelectedPlan(out var compiled, out var message))
        {
            _preparedPlan = null;
            ShowInvalidPlanPreview();
            if (showMessage)
                MessageBox.Show(message, "方案校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _preparedPlan = compiled;
        UpdatePlanPreview(compiled!);
        return true;
    }

    private bool TryCompileSelectedPlan(out CompiledTestPlan? compiled, out string message)
    {
        compiled = null;
        if (_planCombo.SelectedItem is not PlanChoice choice)
        {
            message = "当前没有可用的启用方案，请先在方案库中保存并启用一个固定模板方案。";
            return false;
        }

        var plan = _database.GetPlans().FirstOrDefault(x => x.Id == choice.Id && x.Enabled);
        if (plan is null)
        {
            message = "所选方案已被停用或删除，请刷新方案列表后重新选择。";
            return false;
        }

        var result = TestPlanCompiler.Compile(plan, _database.GetPlanSteps(plan.Id), _getSettings());
        message = result.Message;
        compiled = result.Plan;
        return result.Success && compiled is not null;
    }

    private void UpdatePlanPreview(CompiledTestPlan plan)
    {
        _progress.Maximum = Math.Max(1, plan.TargetCycles);
        _forceCard.Note = $"方案 {plan.PlanCode}  ·  目标 {plan.TargetForce:0.###} N";
        if (_processStepDetails.Count == 5)
        {
            _processStepDetails[0].Text = $"{plan.PullDuration:0.###} s";
            _processStepDetails[1].Text = $"{plan.HoldDuration:0.###} s";
            _processStepDetails[2].Text = $"{plan.ReturnDuration:0.###} s";
            _processStepDetails[3].Text = $"≤ {plan.ResetDisplacementTolerance:0.###} mm";
            _processStepDetails[4].Text = $"{plan.ActionInterval:0.###} s / {plan.TargetCycles:N0} 次";
        }
        if (_engine.State == TestRunState.Ready)
        {
            _runStatus.Caption = "方案就绪";
            _runStatus.StatusColor = Theme.Green;
        }
    }

    private void ShowInvalidPlanPreview()
    {
        _progress.Maximum = 1;
        _progress.Value = 0;
        _forceCard.Note = "未绑定有效方案，禁止启动";
        foreach (var detail in _processStepDetails) detail.Text = "—";
        if (_engine.State == TestRunState.Ready)
        {
            _runStatus.Caption = "方案无效";
            _runStatus.StatusColor = Theme.Red;
        }
    }

    internal async void StartDemoForCapture()
    {
        if (_engine.State != TestRunState.Ready) return;
        if (!TryCompileSelectedPlan(out var compiled, out _) || compiled is null) return;
        _runningPlan = compiled;
        _runningSettings = compiled.CreateSettingsSnapshot();
        _runningSpecimenNo = _specimenText.Text.Trim();
        _runningOperator = string.IsNullOrWhiteSpace(_operatorText.Text) ? "未填写" : _operatorText.Text.Trim();
        _startedAt = DateTime.Now;
        _recordSaved = false;
        // 自动截图只驱动界面，不写入正式的试验采样/汇总表，避免生成孤儿记录。
        _activeTestNo = string.Empty;
        _sampleBuffer.Clear();
        _lastRecordedAt.Clear();
        _engine.ApplySettings(_runningSettings);
        _engine.ConfigureActiveStations(_stationChecks.Where(x => x.Checked && x.Enabled).Select(x => Convert.ToInt32(x.Tag)).ToArray());
        await _engine.StartAsync();
    }

    private CardPanel BuildTestHeader(
        out TextBox specimenText,
        out TextBox operatorText,
        out ComboBox planCombo,
        out StatusPill status,
        out ComboBox monitorStationCombo)
    {
        var card = new CardPanel { Dock = DockStyle.Top, Height = 116, Padding = new Padding(18, 12, 18, 8) };
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
        planCombo = UiFactory.Combo([], string.Empty);
        planCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        planCombo.Width = 235;
        fields.Controls.Add(planCombo);
        fields.Controls.Add(InlineLabel("操作员"));
        operatorText = UiFactory.TextBox(Environment.UserName);
        operatorText.Width = 90;
        fields.Controls.Add(operatorText);
        status = new StatusPill { Caption = "系统就绪", StatusColor = Theme.Green, Margin = new Padding(16, 9, 0, 0), Size = new Size(92, 30) };
        fields.Controls.Add(status);

        var stationBar = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Location = new Point(18, 75),
            Size = new Size(card.Width - 36, 34),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.FromArgb(247, 249, 252),
            Padding = new Padding(8, 3, 8, 2)
        };
        stationBar.Controls.Add(new Label
        {
            Text = "参与试验",
            Font = Theme.Font(8, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = false,
            Size = new Size(72, 25),
            TextAlign = ContentAlignment.MiddleLeft
        });
        for (var stationId = 1; stationId <= StationTopology.MaximumStationCount; stationId++)
        {
            var id = stationId;
            var check = new CheckBox
            {
                Text = $"工位 {id}",
                Tag = id,
                AutoSize = false,
                Size = new Size(StationTopology.IsExpansion(id) ? 118 : 82, 25),
                Font = Theme.Font(8),
                ForeColor = StationTopology.IsExpansion(id) ? Theme.Purple : Theme.Text,
                Checked = id <= StationTopology.StandardStationCount,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(2, 0, 4, 0)
            };
            check.CheckedChanged += (_, _) => RefreshMonitorStations();
            _stationChecks.Add(check);
            stationBar.Controls.Add(check);
        }
        stationBar.Controls.Add(new Label
        {
            Text = "曲线监视",
            Font = Theme.Font(8, FontStyle.Bold),
            ForeColor = Theme.Muted,
            AutoSize = false,
            Size = new Size(75, 25),
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(18, 0, 3, 0)
        });
        monitorStationCombo = UiFactory.Combo(
            Enumerable.Range(1, StationTopology.StandardStationCount).Select(StationTopology.DefaultName),
            StationTopology.DefaultName(1));
        monitorStationCombo.Size = new Size(140, 26);
        monitorStationCombo.Margin = new Padding(2, 0, 0, 0);
        monitorStationCombo.SelectedIndexChanged += (_, _) => _chart?.Clear();
        stationBar.Controls.Add(monitorStationCombo);

        card.Controls.Add(fields);
        card.Controls.Add(stationBar);
        card.Controls.Add(title);
        card.Controls.Add(caption);
        return card;
    }

    private void RefreshMonitorStations()
    {
        if (_monitorStationCombo is null) return;
        var previousId = (_monitorStationCombo.SelectedItem as StationChoice)?.Id;
        var selected = _stationChecks
            .Where(x => x.Checked && x.Enabled)
            .Select(x => new StationChoice(Convert.ToInt32(x.Tag), x.Text))
            .ToArray();
        _monitorStationCombo.Items.Clear();
        _monitorStationCombo.Items.AddRange(selected);
        _monitorStationCombo.SelectedItem = selected.FirstOrDefault(x => x.Id == previousId) ?? selected.FirstOrDefault();
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
            ("05", "间隔 / 计数", "0.5 s / +1", Theme.Purple)
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
            _processStepDetails.Add(time);
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
        _planCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshingPlans || _engine.State is TestRunState.Running or TestRunState.Paused) return;
            PrepareSelectedPlan(showMessage: true);
        };
        _startButton.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_specimenText.Text))
            {
                MessageBox.Show("请先填写试件编号。", "试验提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _specimenText.Focus();
                return;
            }

            if (_engine.State == TestRunState.Paused)
            {
                if (_runningPlan is null)
                {
                    MessageBox.Show("当前暂停试验缺少冻结方案快照，禁止继续；请停止并重新启动试验。", "方案快照缺失", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var resumeResult = await _engine.StartAsync();
                _database.AddLog(resumeResult.Success ? "信息" : "报警", "试验控制", resumeResult.Message);
                if (!resumeResult.Success)
                    MessageBox.Show(resumeResult.Message, "继续试验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryCompileSelectedPlan(out var runPlan, out var planMessage) || runPlan is null)
            {
                _preparedPlan = null;
                ShowInvalidPlanPreview();
                _database.AddLog("报警", "试验方案", $"启动被阻止：{planMessage}");
                MessageBox.Show(
                    $"启动已被阻止，未向 CAN 设备发送动作命令。\n\n{planMessage}",
                    "试验方案无效",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _preparedPlan = runPlan;
            UpdatePlanPreview(runPlan);
            _runningSettings = runPlan.CreateSettingsSnapshot();
            _engine.ApplySettings(_runningSettings);
            var stationIds = _stationChecks.Where(x => x.Checked && x.Enabled).Select(x => Convert.ToInt32(x.Tag)).ToArray();
            var stationResult = _engine.ConfigureActiveStations(stationIds);
            if (!stationResult.Success)
            {
                MessageBox.Show(stationResult.Message, "工位选择", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _runningPlan = runPlan;
            _runningSpecimenNo = _specimenText.Text.Trim();
            _runningOperator = string.IsNullOrWhiteSpace(_operatorText.Text) ? "未填写" : _operatorText.Text.Trim();
            _startedAt = DateTime.Now;
            _recordSaved = false;
            _activeTestNo = $"T{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}"[..26];
            _sampleBuffer.Clear();
            _lastRecordedAt.Clear();
            var result = await _engine.StartAsync();
            _database.AddLog(result.Success ? "信息" : "报警", "试验控制",
                result.Success
                    ? $"启动试验：{_specimenText.Text}，方案 {runPlan.PlanCode}/{runPlan.PlanName}，编号 {_activeTestNo}，工位 {string.Join("、", stationIds)}"
                    : result.Message);
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
        BufferSample(sample);
        if (sample.StationId != GetMonitoredStationId()) return;
        _chart.AddSample(sample);
        _forceCard.Value = sample.Force.ToString("0.0");
        _currentCard.Value = sample.Current.ToString("0.00");
        _voltageCard.Value = sample.Voltage.ToString("0.0");
        _positionCard.Value = sample.Displacement.ToString("0.0");
        _progress.Value = sample.Cycle;
        _phaseValue.Text = sample.Phase;
        _elapsedValue.Text = _engine.Elapsed.ToString(@"hh\:mm\:ss");
        var status = _engine.StationStatuses.GetValueOrDefault(sample.StationId);
        _peakValue.Text = $"{(status?.PeakForce ?? sample.Force):0.0} N";
    }

    private void EngineOnStateChanged(object? sender, TestRunState state)
    {
        var (caption, color) = state switch
        {
            TestRunState.Running => ($"{_engine.ActiveStationIds.Count} 工位运行", Theme.Primary),
            TestRunState.Paused => ($"{_engine.ActiveStationIds.Count} 工位暂停", Theme.Orange),
            TestRunState.Completed => ("试验完成", Theme.Green),
            TestRunState.Alarm => ("设备报警", Theme.Red),
            _ => ("系统就绪", Theme.Green)
        };
        _runStatus.Caption = caption;
        _runStatus.StatusColor = color;
        _startButton.Enabled = state is TestRunState.Ready or TestRunState.Paused;
        _startButton.Text = state == TestRunState.Paused ? "▶  继续试验" : "▶  启动试验";
        _pauseButton.Enabled = state == TestRunState.Running;
        _stopButton.Enabled = state is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm;
        _planCombo.Enabled = state is not (TestRunState.Running or TestRunState.Paused or TestRunState.Alarm);
        _specimenText.Enabled = state is not (TestRunState.Running or TestRunState.Paused or TestRunState.Alarm);
        _operatorText.Enabled = state is not (TestRunState.Running or TestRunState.Paused or TestRunState.Alarm);
        foreach (var check in _stationChecks) check.Enabled = state is not (TestRunState.Running or TestRunState.Paused or TestRunState.Alarm) &&
            _getSettings().Stations.Any(x => x.StationId == Convert.ToInt32(check.Tag) && x.Enabled);
        if (state == TestRunState.Completed)
        {
            SaveRecord("合格");
            _database.AddLog("信息", "试验控制", $"试验自动完成，共 {_engine.CurrentCycle:N0} 次循环");
        }
        else if (state == TestRunState.Alarm)
        {
            SaveRecord("不合格");
            _database.AddLog("报警", "试验控制", "试验因设备或保护报警中止");
        }
    }

    private async Task StopAndSaveAsync()
    {
        if (_engine.State is not (TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)) return;
        var result = await _engine.StopAsync();
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "停止试验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _database.AddLog("报警", "试验控制", $"停机未全部确认：{result.Message}");
            return;
        }
        if (_recordSaved) return;
        SaveRecord("人工终止");
        _database.AddLog("警告", "试验控制", $"试验由操作员停止，完成 {_engine.CurrentCycle:N0} 次循环，结果记为人工终止");
    }

    internal async Task<bool> FinalizeBeforeCloseAsync()
    {
        if (_engine.IsOperationInProgress || _engine.State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
        {
            var stop = await _engine.StopAsync();
            if (!stop.Success)
            {
                _database.AddLog("报警", "程序关闭", $"关闭前停机未确认：{stop.Message}");
                MessageBox.Show(
                    $"软件不能确认所有工位已经停止，因此本次关闭已取消。\n\n请按下硬件急停/STO，检查各工位后重试。\n\n{stop.Message}",
                    "禁止关闭", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (!_recordSaved && _engine.CurrentCycle > 0)
                SaveRecord("窗口关闭终止");
            if (!_recordSaved && _engine.CurrentCycle > 0) return false;
        }

        if (!_recordSaved && _engine.CurrentCycle > 0)
        {
            SaveRecord("窗口关闭终止");
            if (!_recordSaved) return false;
        }

        try
        {
            FlushSamples();
            return true;
        }
        catch (Exception ex)
        {
            _database.AddLog("报警", "程序关闭", $"关闭前刷新采样失败：{ex.Message}");
            MessageBox.Show(
                $"采样数据尚未安全写入数据库，本次关闭已取消。\n\n{ex.Message}",
                "数据保存故障", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void SaveRecord(string result)
    {
        if (_recordSaved || _engine.CurrentCycle == 0) return;
        var settings = _runningSettings ?? _getSettings();
        var planSnapshot = _runningPlan?.CreateAuditSnapshotJson() ?? string.Empty;
        var records = new List<TestRecord>();
        foreach (var stationId in _engine.ActiveStationIds)
        {
            var status = _engine.StationStatuses[stationId];
            var processPassed = status.PeakForce >= settings.ForceLowerLimit &&
                                status.PeakForce <= settings.ForceUpperLimit;
            var protectionPassed = status.PeakForce <= settings.MaxForceProtection &&
                                   status.PeakDisplacement <= settings.MaxDisplacementProtection;
            var stationResult = result == "合格" && (!processPassed || !protectionPassed) ? "不合格" : result;
            var failureReason = stationResult switch
            {
                "合格" => string.Empty,
                "人工终止" or "窗口关闭终止" => stationResult,
                _ when !processPassed =>
                    $"峰值拉力 {status.PeakForce:0.###} N 不在工艺判定范围 {settings.ForceLowerLimit:0.###}~{settings.ForceUpperLimit:0.###} N",
                _ when !protectionPassed =>
                    $"峰值超过硬保护：拉力 {status.PeakForce:0.###} N / 位移 {status.PeakDisplacement:0.###} mm",
                _ => status.Message
            };
            records.Add(new TestRecord
            {
                TestNo = $"{_activeTestNo}-S{stationId}",
                SpecimenNo = _engine.ActiveStationIds.Count == 1
                    ? _runningSpecimenNo
                    : $"{_runningSpecimenNo}-S{stationId}",
                PlanName = _runningPlan?.PlanName ?? "未绑定方案",
                PlanId = _runningPlan?.PlanId ?? 0,
                PlanCode = _runningPlan?.PlanCode ?? string.Empty,
                PlanRevision = _runningPlan?.PlanRevision ?? 1,
                PlanSnapshotJson = planSnapshot,
                StartedAt = _startedAt,
                Duration = _engine.Elapsed,
                Cycles = status.CurrentCycle,
                PeakForce = status.PeakForce,
                PeakDisplacement = status.PeakDisplacement,
                StationId = stationId,
                StationName = status.StationName,
                Result = stationResult,
                FailureReason = failureReason,
                Operator = _runningOperator
            });
        }
        try
        {
            FlushSamples();
            _database.AddTestRecords(records);
        }
        catch (Exception ex)
        {
            _database.AddLog("报警", "数据持久化", $"试验汇总保存失败：{ex.Message}");
            MessageBox.Show(
                $"试验已终止，但数据库保存失败。请勿关闭软件，先备份数据库并联系软件维护人员。\n\n{ex.Message}",
                "数据保存故障", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _recordSaved = true;
    }

    private async void ResetTest()
    {
        if (_engine.State is TestRunState.Running or TestRunState.Paused)
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
        var interval = Math.Max(50, (_runningSettings ?? _getSettings()).DataRecordInterval);
        if (_lastRecordedAt.TryGetValue(sample.StationId, out var lastRecorded) &&
            (sample.Time - lastRecorded).TotalMilliseconds < interval) return;
        _lastRecordedAt[sample.StationId] = sample.Time;
        _sampleBuffer.Add(new TestSampleRecord
        {
            TestNo = $"{_activeTestNo}-S{sample.StationId}",
            StationId = sample.StationId,
            StationName = sample.StationName,
            Time = sample.Time,
            ElapsedMilliseconds = (long)_engine.Elapsed.TotalMilliseconds,
            Force = sample.Force,
            Current = sample.Current,
            Voltage = sample.Voltage,
            Displacement = sample.Displacement,
            AcquisitionSequence = sample.AcquisitionSequence,
            DigitalInputs = sample.DigitalInputs,
            ForceInputVoltage = sample.ForceInputVoltage,
            CurrentInputVoltage = sample.CurrentInputVoltage,
            VoltageInputVoltage = sample.VoltageInputVoltage,
            DisplacementInputVoltage = sample.DisplacementInputVoltage,
            DataQuality = sample.DataQuality,
            ControllerFrame = sample.ControllerFrame,
            Cycle = sample.Cycle,
            Phase = sample.Phase
        });
        if (_sampleBuffer.Count >= 100) FlushSamples();
    }

    private int GetMonitoredStationId()
    {
        if (_monitorStationCombo.SelectedItem is StationChoice station) return station.Id;
        var text = _monitorStationCombo.Text;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var id) ? id : _engine.ActiveStationIds.FirstOrDefault(1);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { FlushSamples(); }
            catch (Exception ex) { _database.AddLog("报警", "页面释放", $"刷新采样数据失败：{ex.Message}"); }
        }
        base.Dispose(disposing);
    }

    private sealed record StationChoice(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record PlanChoice(long Id, string Code, string Name)
    {
        public PlanChoice(TestPlan plan) : this(plan.Id, plan.Code, plan.Name) { }
        public override string ToString() => $"{Code} · {Name}";
    }
}
