using System.Diagnostics;
using System.Text;
using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI.Pages;

public sealed class DiagnosticsPage : UserControl, IRefreshablePage
{
    private readonly AppDatabase _database;
    private readonly ITestEngine _engine;
    private readonly SystemProfile _profile;
    private readonly DataGridView _grid;
    private readonly Label _summary;
    private readonly Label _profileValue;
    private readonly Label _databaseValue;
    private readonly Label _integrityValue;

    public DiagnosticsPage(AppDatabase database, ITestEngine engine, SystemProfile profile)
    {
        _database = database;
        _engine = engine;
        _profile = profile;
        BackColor = Theme.Window;

        var header = new CardPanel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(18, 13, 18, 12) };
        var title = UiFactory.Label("设备诊断与交付检查", 11, Theme.Text, FontStyle.Bold);
        title.Location = new Point(18, 12);
        _summary = UiFactory.Label("等待检查", 8.5f, Theme.Muted);
        _summary.Location = new Point(18, 42);
        _summary.AutoSize = false;
        _summary.Size = new Size(850, 42);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 470, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 11, 0, 0) };
        var checkButton = UiFactory.Button("连接与自检", Theme.Primary, Color.White, 120);
        var backupButton = UiFactory.SecondaryButton("备份数据库", 120);
        var exportButton = UiFactory.SecondaryButton("导出诊断信息", 130);
        foreach (var button in new[] { checkButton, backupButton, exportButton })
        {
            button.Height = 36;
            button.Margin = new Padding(5);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Theme.Border;
        }
        actions.Controls.AddRange([checkButton, backupButton, exportButton]);
        header.Controls.AddRange([title, _summary]);
        header.Controls.Add(actions);
        actions.BringToFront();

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0, 14, 0, 0) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var environmentCard = UiFactory.Card("运行环境", "正式交付前请核对模式、配置文件和数据库完整性");
        environmentCard.Dock = DockStyle.Fill;
        environmentCard.Margin = new Padding(0, 0, 0, 8);
        var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(0, 4, 0, 0) };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _profileValue = AddInfoRow(info, 0, "运行配置", string.Empty);
        AddInfoRow(info, 1, "配置文件", RuntimeProfileLoader.ProfilePath);
        _databaseValue = AddInfoRow(info, 2, "数据库文件", _database.DatabasePath);
        _integrityValue = AddInfoRow(info, 3, "数据库完整性", "未检查");
        environmentCard.Controls.Add(info);

        var deviceCard = UiFactory.Card("硬件状态", "状态来自当前硬件适配器；正式模式下任一关键设备异常都会阻止启动");
        deviceCard.Dock = DockStyle.Fill;
        deviceCard.Margin = new Padding(0, 8, 0, 0);
        _grid = UiFactory.Grid();
        _grid.ReadOnly = true;
        _grid.AutoGenerateColumns = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "设备", FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "诊断信息", FillWeight = 49 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "更新时间", FillWeight = 15 });
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != 1 || e.Value is null || e.CellStyle is null) return;
            e.CellStyle.ForeColor = Convert.ToString(e.Value) switch
            {
                "在线" => Theme.Green,
                "警告" or "连接中" => Theme.Orange,
                "故障" => Theme.Red,
                _ => Theme.Muted
            };
            e.CellStyle.Font = Theme.Font(8.5f, FontStyle.Bold);
        };
        deviceCard.Controls.Add(_grid);
        body.Controls.Add(environmentCard, 0, 0);
        body.Controls.Add(deviceCard, 0, 1);

        Controls.Add(body);
        Controls.Add(header);

        checkButton.Click += async (_, _) =>
        {
            checkButton.Enabled = false;
            try
            {
                var result = await _engine.ConnectAndSelfCheckAsync();
                _database.AddLog(result.Success ? "信息" : "报警", "设备诊断", result.Message);
                RefreshData();
                MessageBox.Show(result.Message, "设备自检", MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally { checkButton.Enabled = true; }
        };
        backupButton.Click += (_, _) =>
        {
            var path = _database.CreateBackup();
            MessageBox.Show($"数据库备份完成：\n{path}", "数据库备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        exportButton.Click += (_, _) => ExportDiagnostics();
        _engine.HealthChanged += (_, _) =>
        {
            if (IsHandleCreated) BeginInvoke(new Action(RefreshData));
        };
    }

    public void RefreshData()
    {
        var health = _engine.Health;
        _profileValue.Text = $"{_profile.ProfileName}  ·  {(_profile.Mode == RuntimeMode.Demo ? "Demo 演示模式" : "Production 正式模式")}";
        _databaseValue.Text = _database.DatabasePath;
        _integrityValue.Text = _database.CheckIntegrity();
        _integrityValue.ForeColor = _integrityValue.Text == "ok" ? Theme.Green : Theme.Red;
        _summary.Text = health.Summary;
        _summary.ForeColor = health.CanStartTest ? Theme.Green : Theme.Red;
        _grid.Rows.Clear();
        foreach (var device in health.Devices)
            _grid.Rows.Add(device.Name, StateText(device.State), device.Message, device.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private void ExportDiagnostics()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt",
            FileName = $"安全带耐久试验系统_诊断信息_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var text = new StringBuilder()
            .AppendLine("安全带耐久试验系统 - 诊断信息")
            .AppendLine($"导出时间：{DateTime.Now:O}")
            .AppendLine($"软件版本：{Application.ProductVersion}")
            .AppendLine($"运行模式：{_profile.Mode}")
            .AppendLine($"配置文件：{RuntimeProfileLoader.ProfilePath}")
            .AppendLine($"数据库：{_database.DatabasePath}")
            .AppendLine($"数据库完整性：{_database.CheckIntegrity()}")
            .AppendLine($"系统摘要：{_engine.Health.Summary}");
        foreach (var device in _engine.Health.Devices)
            text.AppendLine($"{device.Name}：{device.State}；{device.Message}");
        File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true));
        _database.AddLog("信息", "设备诊断", $"导出诊断信息：{dialog.FileName}");
    }

    private static Label AddInfoRow(TableLayoutPanel table, int row, string keyText, string valueText)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        var key = UiFactory.Label(keyText, 8.5f, Theme.Muted, FontStyle.Bold, DockStyle.Fill);
        key.TextAlign = ContentAlignment.MiddleLeft;
        var value = UiFactory.Label(valueText, 8.5f, Theme.Text, FontStyle.Bold, DockStyle.Fill);
        value.TextAlign = ContentAlignment.MiddleLeft;
        table.Controls.Add(key, 0, row);
        table.Controls.Add(value, 1, row);
        return value;
    }

    private static string StateText(DeviceConnectionState state) => state switch
    {
        DeviceConnectionState.Online => "在线",
        DeviceConnectionState.Warning => "警告",
        DeviceConnectionState.Fault => "故障",
        DeviceConnectionState.Connecting => "连接中",
        DeviceConnectionState.Disconnected => "离线",
        _ => "未配置"
    };
}
