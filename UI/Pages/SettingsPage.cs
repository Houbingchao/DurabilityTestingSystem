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
    private readonly NumericUpDown _sampleInterval;
    private readonly ComboBox _canDevice;
    private readonly ComboBox _baudRate;
    private readonly NumericUpDown _nodeId;
    private readonly NumericUpDown _motorSpeed;
    private readonly NumericUpDown _motorAcceleration;
    private readonly ComboBox _controlMode;
    private readonly NumericUpDown _communicationTimeout;
    private readonly ComboBox _autoReconnect;
    private readonly TextBox _moduleIp;
    private readonly NumericUpDown _modulePort;
    private readonly NumericUpDown _sensorRange;
    private readonly ComboBox _signalType;
    private readonly NumericUpDown _filterWindow;
    private readonly ComboBox _forceChannel;
    private readonly ComboBox _currentChannel;
    private readonly ComboBox _voltageChannel;
    private readonly NumericUpDown _currentSensorRange;
    private readonly NumericUpDown _voltageSensorRange;
    private readonly NumericUpDown _maxForce;
    private readonly NumericUpDown _maxCurrent;
    private readonly NumericUpDown _maxVoltage;
    private readonly NumericUpDown _overLimitDelay;
    private readonly ComboBox _positiveLimitInput;
    private readonly ComboBox _negativeLimitInput;
    private readonly ComboBox _safetyDoorInput;
    private readonly ComboBox _overLimitAction;

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
        _sampleInterval = UiFactory.Numeric(100, 50, 5000, 0, 10);

        _canDevice = UiFactory.Combo(["USBCAN-2E-U / 通道 0", "PCIe-CAN / 通道 0", "虚拟 CAN / Demo"], "USBCAN-2E-U / 通道 0");
        _baudRate = UiFactory.Combo(["125 kbps", "250 kbps", "500 kbps", "1000 kbps"], "500 kbps");
        _nodeId = UiFactory.Numeric(1, 1, 127);
        _motorSpeed = UiFactory.Numeric(120, 1, 3000, 1, 10);
        _motorAcceleration = UiFactory.Numeric(300, 1, 10000, 1, 10);
        _controlMode = UiFactory.Combo(["位置模式", "速度模式", "力矩模式"], "位置模式");
        _communicationTimeout = UiFactory.Numeric(1000, 100, 30000, 0, 100);
        _autoReconnect = UiFactory.Combo(["启用", "禁用"], "启用");

        _moduleIp = UiFactory.TextBox();
        _modulePort = UiFactory.Numeric(502, 1, 65535);
        _sensorRange = UiFactory.Numeric(1000, 10, 100000, 1, 100);
        _signalType = UiFactory.Combo(["4~20 mA", "0~10 V", "±10 V", "0~5 V"], "4~20 mA");
        _filterWindow = UiFactory.Numeric(5, 1, 100);
        _forceChannel = UiFactory.Combo(["AI0", "AI1", "AI2", "AI3", "AI4", "AI5", "AI6", "AI7"], "AI0");
        _currentChannel = UiFactory.Combo(["AI0", "AI1", "AI2", "AI3", "AI4", "AI5", "AI6", "AI7"], "AI1");
        _voltageChannel = UiFactory.Combo(["AI0", "AI1", "AI2", "AI3", "AI4", "AI5", "AI6", "AI7"], "AI2");
        _currentSensorRange = UiFactory.Numeric(20, 1, 5000, 1, 5);
        _voltageSensorRange = UiFactory.Numeric(100, 1, 5000, 1, 10);

        _maxForce = UiFactory.Numeric(650, 1, 100000, 1, 10);
        _maxCurrent = UiFactory.Numeric(8, .1m, 1000, 1, .5m);
        _maxVoltage = UiFactory.Numeric(60, 1, 10000, 1, 5);
        _overLimitDelay = UiFactory.Numeric(200, 0, 10000, 0, 50);
        _positiveLimitInput = UiFactory.Combo(["DI0", "DI1", "DI2", "DI3", "禁用"], "DI0");
        _negativeLimitInput = UiFactory.Combo(["DI0", "DI1", "DI2", "DI3", "禁用"], "DI1");
        _safetyDoorInput = UiFactory.Combo(["DI0", "DI1", "DI2", "DI3", "禁用"], "DI2");
        _overLimitAction = UiFactory.Combo(["立即停止并报警", "减速后停止", "仅记录"], "立即停止并报警");

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

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(0, 14, 0, 10),
            BackColor = Theme.Window,
            AutoScroll = true,
            AutoScrollMinSize = new Size(0, 594)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 318));

        content.Controls.Add(BuildCard("试验参数", "定义耐久循环目标、判定范围和动作时序",
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
            UiFactory.Field("采样周期", _sampleInterval, "ms")
        ]), 0, 0);

        content.Controls.Add(BuildCard("CAN 与电机", "配置 CAN 接口、驱动器节点及运动参数",
        [
            UiFactory.Field("CAN 设备", _canDevice),
            UiFactory.Field("通讯波特率", _baudRate),
            UiFactory.Field("驱动器节点 ID", _nodeId),
            UiFactory.Field("电机速度", _motorSpeed, "rpm"),
            UiFactory.Field("加减速度", _motorAcceleration, "rpm/s"),
            UiFactory.Field("控制模式", _controlMode),
            UiFactory.Field("通讯超时", _communicationTimeout, "ms"),
            UiFactory.Field("自动重连", _autoReconnect)
        ]), 1, 0);

        content.Controls.Add(BuildCard("模拟量采集", "配置拉力传感器与 Modbus TCP 采集模块",
        [
            UiFactory.Field("采集模块 IP", _moduleIp),
            UiFactory.Field("Modbus 端口", _modulePort),
            UiFactory.Field("拉力信号类型", _signalType),
            UiFactory.Field("传感器满量程", _sensorRange, "N"),
            UiFactory.Field("拉力通道", _forceChannel),
            UiFactory.Field("滤波窗口", _filterWindow, "点"),
            UiFactory.Field("电流通道", _currentChannel),
            UiFactory.Field("电流传感器量程", _currentSensorRange, "A"),
            UiFactory.Field("电压通道", _voltageChannel),
            UiFactory.Field("电压传感器量程", _voltageSensorRange, "V")
        ]), 0, 1);

        content.Controls.Add(BuildCard("安全保护", "软件阈值用于联锁判断，不能替代硬件急停回路",
        [
            UiFactory.Field("拉力硬保护上限", _maxForce, "N"),
            UiFactory.Field("驱动电流上限", _maxCurrent, "A"),
            UiFactory.Field("母线电压上限", _maxVoltage, "V"),
            UiFactory.Field("连续超限延时", _overLimitDelay, "ms"),
            UiFactory.Field("正向限位输入", _positiveLimitInput),
            UiFactory.Field("反向限位输入", _negativeLimitInput),
            UiFactory.Field("安全门输入", _safetyDoorInput),
            UiFactory.Field("超限动作", _overLimitAction)
        ]), 1, 1);

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

        Controls.Add(content);
        Controls.Add(toolbar);
        Controls.Add(tip);

        saveButton.Click += (_, _) => Save();
        reloadButton.Click += (_, _) => LoadValues(_database.LoadSettings());
        defaultButton.Click += (_, _) => LoadValues(new TestSettings(), markDirty: true);
        testButton.Click += async (_, _) =>
        {
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

        WireDirtyTracking(this);
        LoadValues(settings);
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

    private void LoadValues(TestSettings settings, bool markDirty = false)
    {
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
        SetNumeric(_sampleInterval, settings.SampleInterval);
        _canDevice.SelectedItem = settings.CanDevice;
        _baudRate.SelectedItem = $"{settings.CanBaudRate / 1000} kbps";
        SetNumeric(_nodeId, settings.CanNodeId);
        SetNumeric(_motorSpeed, settings.MotorSpeed);
        SetNumeric(_motorAcceleration, settings.MotorAcceleration);
        _controlMode.SelectedItem = settings.ControlMode;
        SetNumeric(_communicationTimeout, settings.CommunicationTimeout);
        _autoReconnect.SelectedItem = settings.AutoReconnect ? "启用" : "禁用";
        _moduleIp.Text = settings.AnalogModuleIp;
        SetNumeric(_modulePort, settings.AnalogModulePort);
        SetNumeric(_sensorRange, settings.SensorRange);
        _signalType.SelectedItem = settings.ForceSignalType;
        SetNumeric(_filterWindow, settings.FilterWindow);
        _forceChannel.SelectedItem = settings.ForceChannel;
        _currentChannel.SelectedItem = settings.CurrentChannel;
        _voltageChannel.SelectedItem = settings.VoltageChannel;
        SetNumeric(_currentSensorRange, settings.CurrentSensorRange);
        SetNumeric(_voltageSensorRange, settings.VoltageSensorRange);
        SetNumeric(_maxForce, settings.MaxForceProtection);
        SetNumeric(_maxCurrent, settings.MaxCurrentProtection);
        SetNumeric(_maxVoltage, settings.MaxVoltageProtection);
        SetNumeric(_overLimitDelay, settings.OverLimitDelay);
        _positiveLimitInput.SelectedItem = settings.PositiveLimitInput;
        _negativeLimitInput.SelectedItem = settings.NegativeLimitInput;
        _safetyDoorInput.SelectedItem = settings.SafetyDoorInput;
        _overLimitAction.SelectedItem = settings.OverLimitAction;
        _loading = false;
        _saveState.Text = markDirty ? "有未保存修改" : "参数已加载";
        _saveState.ForeColor = markDirty ? Theme.Orange : Theme.Primary;
    }

    private static void SetNumeric(NumericUpDown control, double value) =>
        control.Value = Math.Clamp((decimal)value, control.Minimum, control.Maximum);

    private void Save()
    {
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
        if (!System.Net.IPAddress.TryParse(_moduleIp.Text.Trim(), out _))
        {
            MessageBox.Show("采集模块 IP 地址格式不正确。", "参数校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            SampleInterval = (int)_sampleInterval.Value,
            CanDevice = _canDevice.Text,
            CanBaudRate = int.Parse(_baudRate.Text.Split(' ')[0]) * 1000,
            CanNodeId = (int)_nodeId.Value,
            MotorSpeed = (double)_motorSpeed.Value,
            MotorAcceleration = (double)_motorAcceleration.Value,
            ControlMode = _controlMode.Text,
            CommunicationTimeout = (int)_communicationTimeout.Value,
            AutoReconnect = _autoReconnect.Text == "启用",
            AnalogModuleIp = _moduleIp.Text.Trim(),
            AnalogModulePort = (int)_modulePort.Value,
            SensorRange = (double)_sensorRange.Value,
            ForceSignalType = _signalType.Text,
            FilterWindow = (int)_filterWindow.Value,
            ForceChannel = _forceChannel.Text,
            CurrentChannel = _currentChannel.Text,
            VoltageChannel = _voltageChannel.Text,
            CurrentSensorRange = (double)_currentSensorRange.Value,
            VoltageSensorRange = (double)_voltageSensorRange.Value,
            MaxForceProtection = (double)_maxForce.Value,
            MaxCurrentProtection = (double)_maxCurrent.Value,
            MaxVoltageProtection = (double)_maxVoltage.Value,
            OverLimitDelay = (int)_overLimitDelay.Value,
            PositiveLimitInput = _positiveLimitInput.Text,
            NegativeLimitInput = _negativeLimitInput.Text,
            SafetyDoorInput = _safetyDoorInput.Text,
            OverLimitAction = _overLimitAction.Text,
            DataRecordInterval = 500
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
