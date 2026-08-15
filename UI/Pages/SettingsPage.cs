using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI.Pages;

public sealed class SettingsPage : UserControl
{
    private readonly AppDatabase _database;
    private readonly ITestEngine _engine;
    private readonly Label _saveState;
    private bool _loading;

    private readonly TextBox _projectName;
    private readonly TextBox _planCode;
    private readonly NumericUpDown _targetForce;
    private readonly NumericUpDown _forceUpper;
    private readonly NumericUpDown _forceLower;
    private readonly NumericUpDown _cycles;
    private readonly NumericUpDown _pullTime;
    private readonly NumericUpDown _holdTime;
    private readonly NumericUpDown _returnTime;
    private readonly NumericUpDown _actionInterval;
    private readonly NumericUpDown _sampleInterval;
    private readonly ComboBox _canDevice;
    private readonly NumericUpDown _canDeviceIndex;
    private readonly ComboBox _canBusMode;
    private readonly ComboBox _baudRate;
    private readonly ComboBox _canDataBaudRate;
    private readonly ComboBox _canFdStandard;
    private readonly ComboBox _canTermination;
    private readonly NumericUpDown _canTransmitTimeout;
    private readonly NumericUpDown _motorSpeed;
    private readonly NumericUpDown _motorAcceleration;
    private readonly ComboBox _controlMode;
    private readonly NumericUpDown _communicationTimeout;
    private readonly ComboBox _autoReconnect;
    private readonly ComboBox _protocolMode;
    private readonly TextBox _dbcFilePath;
    private readonly ComboBox _analogDevice;
    private readonly ComboBox _terminalBoard;
    private readonly NumericUpDown _analogBoardId;
    private readonly ComboBox _analogInputMode;
    private readonly NumericUpDown _analogScanRate;
    private readonly NumericUpDown _analogReadTimeout;
    private readonly NumericUpDown _sensorRange;
    private readonly ComboBox _signalType;
    private readonly ComboBox _currentSignalType;
    private readonly ComboBox _voltageSignalType;
    private readonly NumericUpDown _filterWindow;
    private readonly ComboBox _displacementSignalType;
    private readonly NumericUpDown _displacementSensorRange;
    private readonly NumericUpDown _currentSensorRange;
    private readonly NumericUpDown _voltageSensorRange;
    private readonly NumericUpDown _maxForce;
    private readonly NumericUpDown _maxCurrent;
    private readonly NumericUpDown _maxVoltage;
    private readonly NumericUpDown _maxDisplacement;
    private readonly NumericUpDown _resetTolerance;
    private readonly NumericUpDown _overLimitDelay;
    private readonly ComboBox _safetyDoorInput;
    private readonly ComboBox _overLimitAction;
    private readonly DataGridView _stationGrid;
    private readonly TableLayoutPanel _content;

    public event EventHandler<TestSettings>? SettingsSaved;

    public SettingsPage(AppDatabase database, TestSettings settings, ITestEngine engine)
    {
        _database = database;
        _engine = engine;
        BackColor = Theme.Window;

        _projectName = UiFactory.TextBox();
        _planCode = UiFactory.TextBox();
        _targetForce = UiFactory.Numeric(450, 0, 5000, 1, 10);
        _forceUpper = UiFactory.Numeric(520, 0, 5000, 1, 10);
        _forceLower = UiFactory.Numeric(380, 0, 5000, 1, 10);
        _cycles = UiFactory.Numeric(50000, 1, 10000000, 0, 1000);
        _pullTime = UiFactory.Numeric(2, .1m, 120, 1, .1m);
        _holdTime = UiFactory.Numeric(1, 0, 120, 1, .1m);
        _returnTime = UiFactory.Numeric(2, .1m, 120, 1, .1m);
        _actionInterval = UiFactory.Numeric(.5m, 0, 120, 1, .1m);
        _sampleInterval = UiFactory.Numeric(100, 50, 5000, 0, 10);

        _canDevice = UiFactory.Combo([CanHardwareBaseline.DisplayName], CanHardwareBaseline.DisplayName);
        _canDeviceIndex = UiFactory.Numeric(0, 0, 31);
        _canBusMode = UiFactory.Combo(["CAN 2.0", "CAN FD"], "CAN 2.0");
        _baudRate = UiFactory.Combo(CanHardwareBaseline.SupportedArbitrationBaudRates.Select(FormatBaudRate), "500 kbps");
        _canDataBaudRate = UiFactory.Combo(CanHardwareBaseline.SupportedDataBaudRates.Select(FormatBaudRate), "2 Mbps");
        _canFdStandard = UiFactory.Combo(["ISO", "Non-ISO"], "ISO");
        _canTermination = UiFactory.Combo(["禁用（默认）", "启用 120 Ω"], "禁用（默认）");
        _canTransmitTimeout = UiFactory.Numeric(100, 1, 4000, 0, 10);
        _motorSpeed = UiFactory.Numeric(120, 1, 3000, 1, 10);
        _motorAcceleration = UiFactory.Numeric(300, 1, 10000, 1, 10);
        _controlMode = UiFactory.Combo(["位置模式", "速度模式", "力矩模式"], "位置模式");
        _communicationTimeout = UiFactory.Numeric(1000, 100, 30000, 0, 100);
        _autoReconnect = UiFactory.Combo(["启用", "禁用"], "启用");
        _protocolMode = UiFactory.Combo(["DBC 文件", "原始字节协议"], "DBC 文件");
        _dbcFilePath = UiFactory.TextBox();

        _analogDevice = UiFactory.Combo([AnalogHardwareBaseline.DisplayName], AnalogHardwareBaseline.DisplayName);
        _terminalBoard = UiFactory.Combo([AnalogHardwareBaseline.TerminalDisplayName], AnalogHardwareBaseline.TerminalDisplayName);
        _analogBoardId = UiFactory.Numeric(0, 0, 15);
        _analogInputMode = UiFactory.Combo(AnalogHardwareBaseline.SupportedInputModes, AnalogHardwareBaseline.DifferentialMode);
        _analogScanRate = UiFactory.Numeric(100, 1, AnalogHardwareBaseline.MaximumSoftwareScanRateHz, 0, 10);
        _analogReadTimeout = UiFactory.Numeric(500, 50, 30000, 0, 50);
        _sensorRange = UiFactory.Numeric(1000, 10, 100000, 1, 100);
        _signalType = UiFactory.Combo(AnalogHardwareBaseline.SupportedSignalTypes, AnalogHardwareBaseline.SupportedSignalTypes[0]);
        _currentSignalType = UiFactory.Combo(AnalogHardwareBaseline.SupportedSignalTypes, AnalogHardwareBaseline.SupportedSignalTypes[0]);
        _voltageSignalType = UiFactory.Combo(AnalogHardwareBaseline.SupportedSignalTypes, AnalogHardwareBaseline.SupportedSignalTypes[0]);
        _filterWindow = UiFactory.Numeric(5, 1, 100);
        _displacementSignalType = UiFactory.Combo(AnalogHardwareBaseline.SupportedSignalTypes, AnalogHardwareBaseline.SupportedSignalTypes[1]);
        _displacementSensorRange = UiFactory.Numeric(100, 1, 5000, 1, 10);
        _currentSensorRange = UiFactory.Numeric(60, 1, 5000, 1, 5);
        _voltageSensorRange = UiFactory.Numeric(30, 1, 5000, 1, 10);

        _maxForce = UiFactory.Numeric(650, 1, 100000, 1, 10);
        _maxCurrent = UiFactory.Numeric(45, .1m, 1000, 1, .5m);
        _maxVoltage = UiFactory.Numeric(16, 1, 10000, 1, 1);
        _maxDisplacement = UiFactory.Numeric(85, .1m, 5000, 1, 1);
        _resetTolerance = UiFactory.Numeric(2, 0, 100, 1, .5m);
        _overLimitDelay = UiFactory.Numeric(200, 0, 10000, 0, 50);
        _safetyDoorInput = UiFactory.Combo(Enumerable.Range(0, 16).Select(x => $"DI{x}").Append("禁用"), "DI10");
        _overLimitAction = UiFactory.Combo(["立即停止并报警"], "立即停止并报警");
        _stationGrid = BuildStationGrid();

        var tip = new CardPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Theme.PrimarySoft,
            BorderColor = Color.FromArgb(179, 213, 247),
            Padding = new Padding(16, 0, 16, 0)
        };
        var tipText = UiFactory.Label("ℹ  参数修改仅在保存后生效。连接真实设备前，请根据硬件说明书核对量程、地址和保护阈值。", 8.5f, Theme.PrimaryDark, FontStyle.Bold, DockStyle.Fill);
        _saveState = UiFactory.Label("已加载", 8, Theme.Primary, FontStyle.Bold, DockStyle.Right);
        _saveState.Width = 110;
        _saveState.TextAlign = ContentAlignment.MiddleRight;
        tip.Controls.Add(tipText);
        tip.Controls.Add(_saveState);

        _content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(0, 14, 0, 10),
            BackColor = Theme.Window,
            AutoScroll = true,
            AutoScrollMinSize = new Size(0, 1120)
        };
        _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _content.RowStyles.Add(new RowStyle(SizeType.Absolute, 430));
        _content.RowStyles.Add(new RowStyle(SizeType.Absolute, 420));
        _content.RowStyles.Add(new RowStyle(SizeType.Absolute, 252));

        _content.Controls.Add(BuildCard("试验参数", "定义耐久循环目标、判定范围和动作时序",
        [
            UiFactory.Field("项目名称", _projectName),
            UiFactory.Field("方案编号", _planCode),
            UiFactory.Field("目标拉力", _targetForce, "N"),
            UiFactory.Field("目标循环次数", _cycles, "次"),
            UiFactory.Field("拉力上限", _forceUpper, "N"),
            UiFactory.Field("拉力下限", _forceLower, "N"),
            UiFactory.Field("正向拉伸时间", _pullTime, "s"),
            UiFactory.Field("负载保持时间", _holdTime, "s"),
            UiFactory.Field("反向回程时间", _returnTime, "s"),
            UiFactory.Field("动作间隔时间", _actionInterval, "s"),
            UiFactory.Field("采样周期", _sampleInterval, "ms")
        ]), 0, 0);

        _content.Controls.Add(BuildCard("CAN 与电机", "已冻结：周立功 USBCANFD-200U · USB 2.0 · 双通道",
        [
            UiFactory.Field("CAN 设备", _canDevice),
            UiFactory.Field("USB 设备索引", _canDeviceIndex),
            UiFactory.Field("总线模式", _canBusMode),
            UiFactory.Field("仲裁域波特率", _baudRate),
            UiFactory.Field("数据域波特率", _canDataBaudRate),
            UiFactory.Field("CAN FD 标准", _canFdStandard),
            UiFactory.Field("内置终端电阻", _canTermination),
            UiFactory.Field("卡发送超时", _canTransmitTimeout, "ms"),
            UiFactory.Field("电机速度", _motorSpeed, "rpm"),
            UiFactory.Field("加减速度", _motorAcceleration, "rpm/s"),
            UiFactory.Field("控制模式", _controlMode),
            UiFactory.Field("通讯超时", _communicationTimeout, "ms"),
            UiFactory.Field("自动重连", _autoReconnect),
            UiFactory.Field("协议解析方式", _protocolMode),
            UiFactory.Field("DBC 文件路径", _dbcFilePath)
        ]), 1, 0);

        _content.Controls.Add(BuildCard("模拟量采集", "已冻结：PCIE-1604 + P-881B；端子模式由焊接元件决定，软件只核对配置",
        [
            UiFactory.Field("模拟量采集卡", _analogDevice),
            UiFactory.Field("数据接线端子", _terminalBoard),
            UiFactory.Field("板卡拨码 ID", _analogBoardId),
            UiFactory.Field("AI 输入方式", _analogInputMode),
            UiFactory.Field("每通道扫描率", _analogScanRate, "Hz"),
            UiFactory.Field("数据停滞超时", _analogReadTimeout, "ms"),
            UiFactory.Field("拉力信号类型", _signalType),
            UiFactory.Field("传感器满量程", _sensorRange, "N"),
            UiFactory.Field("滤波窗口", _filterWindow, "点"),
            UiFactory.Field("电流信号类型", _currentSignalType),
            UiFactory.Field("电流传感器量程", _currentSensorRange, "A"),
            UiFactory.Field("电压信号类型", _voltageSignalType),
            UiFactory.Field("电压传感器量程", _voltageSensorRange, "V"),
            UiFactory.Field("位移信号类型", _displacementSignalType),
            UiFactory.Field("位移传感器量程", _displacementSensorRange, "mm")
        ]), 0, 1);

        _content.Controls.Add(BuildCard("安全保护", "软件阈值用于联锁判断，不能替代硬件急停回路",
        [
            UiFactory.Field("拉力硬保护上限", _maxForce, "N"),
            UiFactory.Field("驱动电流上限", _maxCurrent, "A"),
            UiFactory.Field("母线电压上限", _maxVoltage, "V"),
            UiFactory.Field("位移保护上限", _maxDisplacement, "mm"),
            UiFactory.Field("复位位移容差", _resetTolerance, "mm"),
            UiFactory.Field("连续超限延时", _overLimitDelay, "ms"),
            UiFactory.Field("安全门输入", _safetyDoorInput),
            UiFactory.Field("超限动作（安全固定）", _overLimitAction)
        ]), 1, 1);

        var stationCard = UiFactory.Card("工位硬件映射（2 个标准 + 1 个扩展）", "工位 1、2 为本期标准配置；扩展工位 3 默认停用，安装并完成标定、自检后方可启用");
        stationCard.Dock = DockStyle.Fill;
        stationCard.Margin = new Padding(0, 0, 8, 8);
        stationCard.Controls.Add(_stationGrid);
        _content.Controls.Add(stationCard, 0, 2);
        _content.SetColumnSpan(stationCard, 2);

        var toolbar = new CardPanel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(14, 8, 14, 8) };
        var buttonFlow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (var column = 0; column < 4; column++)
            buttonFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttonFlow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var testButton = UiFactory.SecondaryButton("连接测试", 110);
        testButton.FlatAppearance.BorderColor = Theme.Border;
        testButton.FlatAppearance.BorderSize = 1;
        var defaultButton = UiFactory.SecondaryButton("恢复推荐值", 120);
        defaultButton.FlatAppearance.BorderColor = Theme.Border;
        defaultButton.FlatAppearance.BorderSize = 1;
        var reloadButton = UiFactory.SecondaryButton("放弃修改", 105);
        reloadButton.FlatAppearance.BorderColor = Theme.Border;
        reloadButton.FlatAppearance.BorderSize = 1;
        var saveButton = UiFactory.Button("✓  保存参数", Theme.Primary, Color.White, 120);
        var toolbarButtons = new[] { testButton, defaultButton, reloadButton, saveButton };
        for (var column = 0; column < toolbarButtons.Length; column++)
        {
            toolbarButtons[column].Dock = DockStyle.Fill;
            toolbarButtons[column].Margin = new Padding(5, 4, 5, 4);
            buttonFlow.Controls.Add(toolbarButtons[column], column, 0);
        }
        var toolbarNote = UiFactory.Label("参数保存位置：SQLite 本地数据库  ·  修改记录将写入系统日志", 8, Theme.Muted, dock: DockStyle.Fill);
        var toolbarLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
        toolbarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbarNote.Margin = new Padding(0);
        toolbarLayout.Controls.Add(toolbarNote, 0, 0);
        toolbarLayout.Controls.Add(buttonFlow, 1, 0);
        toolbar.Controls.Add(toolbarLayout);

        Controls.Add(_content);
        Controls.Add(toolbar);
        Controls.Add(tip);

        saveButton.Click += (_, _) => Save();
        reloadButton.Click += (_, _) => LoadValues(_database.LoadSettings());
        defaultButton.Click += (_, _) => LoadValues(new TestSettings(), markDirty: true);
        testButton.Click += async (_, _) =>
        {
            if (_engine.State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
            {
                MessageBox.Show("试验运行、暂停或报警锁存期间不能重新连接硬件。请先安全停机并复位。", "连接测试",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_saveState.Text == "有未保存修改")
            {
                MessageBox.Show("当前页面还有未保存修改。请先保存参数，再执行连接测试；连接测试只使用已经保存并生效的配置。",
                    "连接测试", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            testButton.Enabled = false;
            _saveState.Text = "正在连接测试...";
            try
            {
                var result = await _engine.ConnectAndSelfCheckAsync();
                _saveState.Text = result.Success ? "连接测试成功" : "连接测试失败";
                _saveState.ForeColor = result.Success ? Theme.Green : Theme.Red;
                _database.AddLog(result.Success ? "信息" : "报警", "参数设置", result.Message);
                MessageBox.Show(result.Message, "连接测试", MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                testButton.Enabled = true;
            }
        };

        _canBusMode.SelectedIndexChanged += (_, _) => UpdateCanFdFieldState();

        WireDirtyTracking(this);
        LoadValues(settings);
    }

    internal void ShowStationGridForCapture()
    {
        _content.AutoScrollPosition = new Point(0, _content.AutoScrollMinSize.Height);
        _content.PerformLayout();
        _stationGrid.ClearSelection();
    }

    private static CardPanel BuildCard(string title, string subtitle, IReadOnlyList<Panel> fields)
    {
        var card = UiFactory.Card(title, subtitle);
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 8, 8);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = (fields.Count + 1) / 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < grid.RowCount; row++)
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / grid.RowCount));
        for (var i = 0; i < fields.Count; i++)
        {
            fields[i].Dock = DockStyle.Fill;
            fields[i].Margin = new Padding(i % 2 == 0 ? 0 : 8, 0, i % 2 == 0 ? 8 : 0, 0);
            grid.Controls.Add(fields[i], i % 2, i / 2);
        }
        card.Controls.Add(grid);
        return card;
    }

    private static DataGridView BuildStationGrid()
    {
        var grid = UiFactory.Grid();
        grid.Dock = DockStyle.Fill;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ReadOnly = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", Width = 52 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StationId", HeaderText = "编号", Width = 52, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Role", HeaderText = "类型", Width = 78, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "工位名称", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CanChannel", HeaderText = "CAN通道", Width = 75 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CanNodeId", HeaderText = "节点ID", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ForceChannel", HeaderText = "拉力AI+", Width = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CurrentChannel", HeaderText = "电流AI+", Width = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VoltageChannel", HeaderText = "电压AI+", Width = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DisplacementChannel", HeaderText = "位移AI+", Width = 72 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PositiveLimit", HeaderText = "正限位DI", Width = 78 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "NegativeLimit", HeaderText = "反限位DI", Width = 78 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CalibrationRecordId", HeaderText = "标定记录", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ForceGain", HeaderText = "拉力K", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ForceOffset", HeaderText = "拉力B", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CurrentGain", HeaderText = "电流K", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CurrentOffset", HeaderText = "电流B", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VoltageGain", HeaderText = "电压K", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VoltageOffset", HeaderText = "电压B", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DisplacementGain", HeaderText = "位移K", Width = 68 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DisplacementOffset", HeaderText = "位移B", Width = 68 });
        return grid;
    }

    private void LoadValues(TestSettings settings, bool markDirty = false)
    {
        settings.EnsureStationConfigurations();
        _loading = true;
        _projectName.Text = settings.ProjectName;
        _planCode.Text = settings.PlanCode;
        SetNumeric(_targetForce, settings.TargetForce);
        SetNumeric(_forceUpper, settings.ForceUpperLimit);
        SetNumeric(_forceLower, settings.ForceLowerLimit);
        SetNumeric(_cycles, settings.TargetCycles);
        SetNumeric(_pullTime, settings.PullDuration);
        SetNumeric(_holdTime, settings.HoldDuration);
        SetNumeric(_returnTime, settings.ReturnDuration);
        SetNumeric(_actionInterval, settings.ActionInterval);
        SetNumeric(_sampleInterval, settings.SampleInterval);
        _canDevice.SelectedItem = settings.CanDevice;
        SetNumeric(_canDeviceIndex, settings.CanDeviceIndex);
        _canBusMode.SelectedItem = settings.CanBusMode;
        _baudRate.SelectedItem = FormatBaudRate(settings.CanBaudRate);
        _canDataBaudRate.SelectedItem = FormatBaudRate(settings.CanDataBaudRate);
        _canFdStandard.SelectedItem = settings.CanFdStandard;
        _canTermination.SelectedItem = settings.CanTerminationEnabled ? "启用 120 Ω" : "禁用（默认）";
        SetNumeric(_canTransmitTimeout, settings.CanTransmitTimeout);
        UpdateCanFdFieldState();
        SetNumeric(_motorSpeed, settings.MotorSpeed);
        SetNumeric(_motorAcceleration, settings.MotorAcceleration);
        _controlMode.SelectedItem = settings.ControlMode;
        SetNumeric(_communicationTimeout, settings.CommunicationTimeout);
        _autoReconnect.SelectedItem = settings.AutoReconnect ? "启用" : "禁用";
        _protocolMode.SelectedItem = settings.ProtocolMode;
        _dbcFilePath.Text = settings.DbcFilePath;
        _analogDevice.SelectedItem = settings.AnalogDevice;
        _terminalBoard.SelectedItem = settings.AnalogTerminalBoard;
        SetNumeric(_analogBoardId, settings.AnalogBoardId);
        _analogInputMode.SelectedItem = settings.AnalogInputMode;
        SetNumeric(_analogScanRate, settings.AnalogScanRate);
        SetNumeric(_analogReadTimeout, settings.AnalogReadTimeout);
        SetNumeric(_sensorRange, settings.SensorRange);
        _signalType.SelectedItem = settings.ForceSignalType;
        _currentSignalType.SelectedItem = settings.CurrentSignalType;
        _voltageSignalType.SelectedItem = settings.VoltageSignalType;
        SetNumeric(_filterWindow, settings.FilterWindow);
        _displacementSignalType.SelectedItem = settings.DisplacementSignalType;
        SetNumeric(_displacementSensorRange, settings.DisplacementSensorRange);
        SetNumeric(_currentSensorRange, settings.CurrentSensorRange);
        SetNumeric(_voltageSensorRange, settings.VoltageSensorRange);
        SetNumeric(_maxForce, settings.MaxForceProtection);
        SetNumeric(_maxCurrent, settings.MaxCurrentProtection);
        SetNumeric(_maxVoltage, settings.MaxVoltageProtection);
        SetNumeric(_maxDisplacement, settings.MaxDisplacementProtection);
        SetNumeric(_resetTolerance, settings.ResetDisplacementTolerance);
        SetNumeric(_overLimitDelay, settings.OverLimitDelay);
        _safetyDoorInput.SelectedItem = settings.SafetyDoorInput;
        _overLimitAction.SelectedItem = settings.OverLimitAction;
        _stationGrid.Rows.Clear();
        foreach (var station in settings.Stations.OrderBy(x => x.StationId))
        {
            var rowIndex = _stationGrid.Rows.Add(station.Enabled, station.StationId,
                StationTopology.IsExpansion(station.StationId) ? "预留扩展" : "标准",
                station.Name, station.CanChannel,
                station.CanNodeId, station.ForceChannel, station.CurrentChannel, station.VoltageChannel,
                station.DisplacementChannel, station.PositiveLimitInput, station.NegativeLimitInput,
                station.CalibrationRecordId,
                station.ForceCalibrationGain, station.ForceCalibrationOffset,
                station.CurrentCalibrationGain, station.CurrentCalibrationOffset,
                station.VoltageCalibrationGain, station.VoltageCalibrationOffset,
                station.DisplacementCalibrationGain, station.DisplacementCalibrationOffset);
            if (StationTopology.IsExpansion(station.StationId))
            {
                _stationGrid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.FromArgb(248, 245, 255);
                _stationGrid.Rows[rowIndex].DefaultCellStyle.ForeColor = Theme.Purple;
            }
        }
        _loading = false;
        _saveState.Text = markDirty ? "有未保存修改" : "参数已加载";
        _saveState.ForeColor = markDirty ? Theme.Orange : Theme.Primary;
    }

    private static void SetNumeric(NumericUpDown control, double value) =>
        control.Value = Math.Clamp((decimal)value, control.Minimum, control.Maximum);

    private void Save()
    {
        if (_engine.State is TestRunState.Running or TestRunState.Paused or TestRunState.Alarm)
        {
            MessageBox.Show("试验运行、暂停或报警锁存期间不能修改生效参数。请先安全停机并复位。", "参数校验",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_forceLower.Value >= _forceUpper.Value)
        {
            MessageBox.Show("拉力下限必须小于拉力上限。", "参数校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_targetForce.Value < _forceLower.Value || _targetForce.Value > _forceUpper.Value)
        {
            MessageBox.Show("目标拉力应位于拉力下限与上限之间。", "参数校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var settings = new TestSettings
        {
            ProjectName = _projectName.Text.Trim(),
            PlanCode = _planCode.Text.Trim(),
            TargetForce = (double)_targetForce.Value,
            ForceUpperLimit = (double)_forceUpper.Value,
            ForceLowerLimit = (double)_forceLower.Value,
            TargetCycles = (int)_cycles.Value,
            PullDuration = (double)_pullTime.Value,
            HoldDuration = (double)_holdTime.Value,
            ReturnDuration = (double)_returnTime.Value,
            ActionInterval = (double)_actionInterval.Value,
            SampleInterval = (int)_sampleInterval.Value,
            CanDevice = _canDevice.Text,
            CanDeviceIndex = (int)_canDeviceIndex.Value,
            CanBusMode = _canBusMode.Text,
            CanBaudRate = ParseBaudRate(_baudRate.Text),
            CanDataBaudRate = ParseBaudRate(_canDataBaudRate.Text),
            CanFdStandard = _canFdStandard.Text,
            CanTerminationEnabled = _canTermination.Text.StartsWith("启用", StringComparison.Ordinal),
            CanTransmitTimeout = (int)_canTransmitTimeout.Value,
            MotorSpeed = (double)_motorSpeed.Value,
            MotorAcceleration = (double)_motorAcceleration.Value,
            ControlMode = _controlMode.Text,
            CommunicationTimeout = (int)_communicationTimeout.Value,
            AutoReconnect = _autoReconnect.Text == "启用",
            ProtocolMode = _protocolMode.Text,
            DbcFilePath = _dbcFilePath.Text.Trim(),
            AnalogDevice = _analogDevice.Text,
            AnalogTerminalBoard = _terminalBoard.Text,
            AnalogBoardId = (int)_analogBoardId.Value,
            AnalogInputMode = _analogInputMode.Text,
            AnalogScanRate = (int)_analogScanRate.Value,
            AnalogReadTimeout = (int)_analogReadTimeout.Value,
            SensorRange = (double)_sensorRange.Value,
            ForceSignalType = _signalType.Text,
            CurrentSignalType = _currentSignalType.Text,
            VoltageSignalType = _voltageSignalType.Text,
            FilterWindow = (int)_filterWindow.Value,
            DisplacementSignalType = _displacementSignalType.Text,
            DisplacementSensorRange = (double)_displacementSensorRange.Value,
            CurrentSensorRange = (double)_currentSensorRange.Value,
            VoltageSensorRange = (double)_voltageSensorRange.Value,
            MaxForceProtection = (double)_maxForce.Value,
            MaxCurrentProtection = (double)_maxCurrent.Value,
            MaxVoltageProtection = (double)_maxVoltage.Value,
            MaxDisplacementProtection = (double)_maxDisplacement.Value,
            ResetDisplacementTolerance = (double)_resetTolerance.Value,
            OverLimitDelay = (int)_overLimitDelay.Value,
            SafetyDoorInput = _safetyDoorInput.Text,
            OverLimitAction = _overLimitAction.Text,
            DataRecordInterval = 500,
            Stations = ReadStations()
        };
        var validation = SettingsValidator.Validate(settings);
        if (!validation.Success)
        {
            MessageBox.Show(validation.Message, "参数校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _database.SaveSettings(settings);
        _saveState.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
        _saveState.ForeColor = Theme.Green;
        SettingsSaved?.Invoke(this, settings);
    }

    private List<StationConfiguration> ReadStations()
    {
        var stations = new List<StationConfiguration>();
        foreach (DataGridViewRow row in _stationGrid.Rows)
        {
            if (!int.TryParse(Convert.ToString(row.Cells["StationId"].Value), out var stationId) ||
                !StationTopology.IsSupported(stationId)) continue;
            stations.Add(new StationConfiguration
            {
                StationId = stationId,
                Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false),
                Name = Convert.ToString(row.Cells["Name"].Value)?.Trim() ?? $"工位 {stationId}",
                CanChannel = int.TryParse(Convert.ToString(row.Cells["CanChannel"].Value), out var canChannel) ? canChannel : 0,
                CanNodeId = int.TryParse(Convert.ToString(row.Cells["CanNodeId"].Value), out var nodeId) ? nodeId : stationId,
                ForceChannel = Convert.ToString(row.Cells["ForceChannel"].Value)?.Trim() ?? string.Empty,
                CurrentChannel = Convert.ToString(row.Cells["CurrentChannel"].Value)?.Trim() ?? string.Empty,
                VoltageChannel = Convert.ToString(row.Cells["VoltageChannel"].Value)?.Trim() ?? string.Empty,
                DisplacementChannel = Convert.ToString(row.Cells["DisplacementChannel"].Value)?.Trim() ?? string.Empty,
                PositiveLimitInput = Convert.ToString(row.Cells["PositiveLimit"].Value)?.Trim() ?? string.Empty,
                NegativeLimitInput = Convert.ToString(row.Cells["NegativeLimit"].Value)?.Trim() ?? string.Empty,
                CalibrationRecordId = Convert.ToString(row.Cells["CalibrationRecordId"].Value)?.Trim() ?? "待标定",
                ForceCalibrationGain = ReadDouble(row, "ForceGain", 1),
                ForceCalibrationOffset = ReadDouble(row, "ForceOffset", 0),
                CurrentCalibrationGain = ReadDouble(row, "CurrentGain", 1),
                CurrentCalibrationOffset = ReadDouble(row, "CurrentOffset", 0),
                VoltageCalibrationGain = ReadDouble(row, "VoltageGain", 1),
                VoltageCalibrationOffset = ReadDouble(row, "VoltageOffset", 0),
                DisplacementCalibrationGain = ReadDouble(row, "DisplacementGain", 1),
                DisplacementCalibrationOffset = ReadDouble(row, "DisplacementOffset", 0)
            });
        }
        return stations;
    }

    private static double ReadDouble(DataGridViewRow row, string columnName, double fallback) =>
        double.TryParse(Convert.ToString(row.Cells[columnName].Value), out var value) ? value : fallback;

    private static string FormatBaudRate(int baudRate) =>
        baudRate >= 1_000_000 && baudRate % 1_000_000 == 0
            ? $"{baudRate / 1_000_000} Mbps"
            : $"{baudRate / 1000} kbps";

    private static int ParseBaudRate(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var value)) return 0;
        return parts[1].StartsWith("M", StringComparison.OrdinalIgnoreCase) ? value * 1_000_000 : value * 1000;
    }

    private void UpdateCanFdFieldState()
    {
        var canFd = string.Equals(_canBusMode.Text, "CAN FD", StringComparison.Ordinal);
        _canDataBaudRate.Enabled = canFd;
        _canFdStandard.Enabled = canFd;
    }

    private void WireDirtyTracking(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case TextBox text:
                    text.TextChanged += (_, _) => MarkDirty();
                    break;
                case NumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => MarkDirty();
                    break;
                case ComboBox combo:
                    combo.SelectedIndexChanged += (_, _) => MarkDirty();
                    break;
                case DataGridView grid:
                    grid.CellValueChanged += (_, _) => MarkDirty();
                    grid.CurrentCellDirtyStateChanged += (_, _) =>
                    {
                        if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    };
                    break;
            }
            if (control.HasChildren) WireDirtyTracking(control);
        }
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _saveState.Text = "有未保存修改";
        _saveState.ForeColor = Theme.Orange;
    }
}
