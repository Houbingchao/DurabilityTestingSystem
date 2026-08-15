using System.Text;
using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI.Pages;

public sealed class HistoryPage : UserControl, IRefreshablePage
{
    private readonly AppDatabase _database;
    private readonly DataGridView _grid;
    private readonly TextBox _keyword;
    private readonly ComboBox _resultFilter;
    private readonly DateTimePicker _startDate;
    private readonly DateTimePicker _endDate;
    private readonly KpiCard _totalCard;
    private readonly KpiCard _passCard;
    private readonly KpiCard _cycleCard;
    private readonly KpiCard _peakCard;
    private IReadOnlyList<TestRecord> _currentRecords = [];

    public HistoryPage(AppDatabase database)
    {
        _database = database;
        BackColor = Theme.Window;

        var kpis = new TableLayoutPanel { Dock = DockStyle.Top, Height = 122, ColumnCount = 4, RowCount = 1 };
        for (var i = 0; i < 4; i++) kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _totalCard = new KpiCard("累计试验", "0", "次", "本地数据库记录", Theme.Primary) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 7, 4) };
        _passCard = new KpiCard("合格率", "0.0", "%", "按最终试验判定", Theme.Green) { Dock = DockStyle.Fill, Margin = new Padding(7, 0, 7, 4) };
        _cycleCard = new KpiCard("累计循环", "0", "次", "全部历史记录", Theme.Purple) { Dock = DockStyle.Fill, Margin = new Padding(7, 0, 7, 4) };
        _peakCard = new KpiCard("历史峰值", "0.0", "N", "最大记录拉力", Theme.Orange) { Dock = DockStyle.Fill, Margin = new Padding(7, 0, 0, 4) };
        kpis.Controls.Add(_totalCard, 0, 0);
        kpis.Controls.Add(_passCard, 1, 0);
        kpis.Controls.Add(_cycleCard, 2, 0);
        kpis.Controls.Add(_peakCard, 3, 0);

        var filter = new CardPanel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(16, 10, 16, 10) };
        _keyword = UiFactory.TextBox();
        _keyword.PlaceholderText = "输入试验编号、试件编号或工位";
        _keyword.Width = 235;
        _resultFilter = UiFactory.Combo(["全部", "合格", "不合格"], "全部");
        _resultFilter.Width = 110;
        _startDate = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Value = DateTime.Today.AddMonths(-1),
            Font = Theme.Font(8.5f),
            Width = 125,
            Margin = new Padding(3, 7, 3, 7)
        };
        _endDate = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd",
            Value = DateTime.Today,
            Font = Theme.Font(8.5f),
            Width = 125,
            Margin = new Padding(3, 7, 3, 7)
        };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 850, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        flow.Controls.Add(InlineLabel("关键词"));
        flow.Controls.Add(_keyword);
        flow.Controls.Add(InlineLabel("试验日期"));
        flow.Controls.Add(_startDate);
        var toLabel = InlineLabel("至");
        toLabel.Width = 26;
        flow.Controls.Add(toLabel);
        flow.Controls.Add(_endDate);
        var resultLabel = InlineLabel("结果");
        resultLabel.Width = 42;
        flow.Controls.Add(resultLabel);
        flow.Controls.Add(_resultFilter);
        var queryButton = UiFactory.Button("查询", Theme.Primary, Color.White, 82);
        queryButton.Margin = new Padding(12, 7, 0, 0);
        flow.Controls.Add(queryButton);

        var rightActions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 385, FlowDirection = FlowDirection.LeftToRight };
        var reportButton = UiFactory.SecondaryButton("查看报告", 92);
        var rawExportButton = UiFactory.SecondaryButton("导出原始数据", 118);
        var exportButton = UiFactory.SecondaryButton("导出汇总", 100);
        foreach (var button in new[] { reportButton, rawExportButton, exportButton })
        {
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.Margin = new Padding(6, 7, 6, 0);
        }
        rightActions.Controls.AddRange([reportButton, rawExportButton, exportButton]);
        filter.Controls.Add(rightActions);
        filter.Controls.Add(flow);

        var listCard = UiFactory.Card("试验记录", "显示最近 500 条记录，可按条件筛选与导出");
        listCard.Dock = DockStyle.Fill;
        listCard.Margin = new Padding(0, 14, 0, 0);
        _grid = UiFactory.Grid();
        _grid.ReadOnly = true;
        _grid.AutoGenerateColumns = false;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "试验编号", FillWeight = 17 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "试件编号", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "工位", FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "试验方案", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "开始时间", FillWeight = 17 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "时长", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "完成循环", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "峰值拉力", FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "峰值位移", FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "结果", FillWeight = 9 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "操作员", FillWeight = 9 });
        foreach (var index in new[] { 5, 6, 7, 8 })
        {
            _grid.Columns[index].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns[index].DefaultCellStyle.Padding = new Padding(6, 0, 10, 0);
        }
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != 9 || e.Value is null || e.CellStyle is null) return;
            e.CellStyle.ForeColor = Convert.ToString(e.Value) == "合格" ? Theme.Green : Theme.Red;
            e.CellStyle.Font = Theme.Font(8.5f, FontStyle.Bold);
        };
        listCard.Controls.Add(_grid);

        Controls.Add(listCard);
        Controls.Add(filter);
        Controls.Add(kpis);

        queryButton.Click += (_, _) => RefreshData();
        _resultFilter.SelectedIndexChanged += (_, _) => RefreshData();
        _keyword.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RefreshData(); };
        reportButton.Click += (_, _) => ShowSelectedReport();
        rawExportButton.Click += (_, _) => ExportSelectedSamples();
        exportButton.Click += (_, _) => ExportData();
    }

    public void RefreshData()
    {
        if (_startDate.Value.Date > _endDate.Value.Date)
        {
            MessageBox.Show("开始日期不能晚于结束日期。", "查询条件", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _currentRecords = _database.GetTestRecords(
            _keyword.Text,
            _resultFilter.Text,
            _startDate.Value.Date,
            _endDate.Value.Date);
        _grid.Rows.Clear();
        foreach (var record in _currentRecords)
        {
            var index = _grid.Rows.Add(
                record.TestNo,
                record.SpecimenNo,
                record.StationName,
                FormatPlan(record),
                record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                UiFactory.FormatDuration(record.Duration),
                record.Cycles.ToString("N0"),
                $"{record.PeakForce:0.0} N",
                $"{record.PeakDisplacement:0.0} mm",
                record.Result,
                record.Operator);
            _grid.Rows[index].Tag = record;
        }

        var all = _database.GetTestRecords();
        _totalCard.Value = all.Count.ToString("N0");
        _passCard.Value = all.Count == 0 ? "0.0" : (all.Count(x => x.Result == "合格") * 100.0 / all.Count).ToString("0.0");
        _cycleCard.Value = all.Sum(x => (long)x.Cycles).ToString("N0");
        _peakCard.Value = all.Count == 0 ? "0.0" : all.Max(x => x.PeakForce).ToString("0.0");
    }

    private static Label InlineLabel(string text) => new()
    {
        Text = text,
        Font = Theme.Font(8),
        ForeColor = Theme.Muted,
        AutoSize = false,
        Width = 62,
        Height = 42,
        TextAlign = ContentAlignment.MiddleCenter,
        Margin = new Padding(4, 2, 2, 0)
    };

    private void ShowSelectedReport()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not TestRecord record)
        {
            MessageBox.Show("请先选择一条试验记录。", "查看报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        MessageBox.Show(
            $"试验编号：{record.TestNo}\n试件编号：{record.SpecimenNo}\n试验工位：{record.StationName}\n执行方案：{FormatPlan(record)}\n开始时间：{record.StartedAt:yyyy-MM-dd HH:mm:ss}\n完成循环：{record.Cycles:N0} 次\n峰值拉力：{record.PeakForce:0.0} N\n峰值位移：{record.PeakDisplacement:0.0} mm\n最终判定：{record.Result}\n终结原因：{(string.IsNullOrWhiteSpace(record.FailureReason) ? "无" : record.FailureReason)}\n配方快照：{(string.IsNullOrWhiteSpace(record.PlanSnapshotJson) ? "早期记录未保存" : "已冻结并保存")}\n\n原始采样数据包含拉力、电流、电压、位移和控制器反馈报文。",
            "试验记录详情", MessageBoxButtons.OK,
            record.Result == "合格" ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ExportData()
    {
        if (_currentRecords.Count == 0)
        {
            MessageBox.Show("当前筛选结果为空。", "导出数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|制表符文本 (*.txt)|*.txt",
            FileName = $"安全带耐久试验记录_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var headers = new[] { "试验编号", "试件编号", "工位", "方案编号", "方案名称", "方案版本", "开始时间", "时长", "完成循环", "峰值拉力(N)", "峰值位移(mm)", "结果", "终结原因", "操作员" };
        var rows = _currentRecords.Select(record => (IReadOnlyList<string>)new[]
        {
            record.TestNo, record.SpecimenNo, record.StationName, record.PlanCode, record.PlanName,
            record.PlanRevision.ToString(),
            record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"), record.Duration.ToString(),
            record.Cycles.ToString(), record.PeakForce.ToString("0.0"), record.PeakDisplacement.ToString("0.0"),
            record.Result, record.FailureReason, record.Operator
        }).ToArray();
        if (Path.GetExtension(dialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            TabularExport.WriteTxt(dialog.FileName, headers, rows);
        else
            TabularExport.WriteXlsx(dialog.FileName, headers, rows);
        _database.AddLog("信息", "历史数据", $"导出试验记录 {_currentRecords.Count} 条：{dialog.FileName}");
        MessageBox.Show("试验记录导出完成。", "导出数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportSelectedSamples()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not TestRecord record)
        {
            MessageBox.Show("请先选择一条试验记录。", "导出原始数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var samples = _database.GetTestSamples(record.TestNo);
        if (samples.Count == 0)
        {
            MessageBox.Show("该记录没有保存原始采样点（早期 Demo 历史记录可能只有汇总数据）。", "导出原始数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|制表符文本 (*.txt)|*.txt",
            FileName = $"{record.TestNo}_原始采样数据.xlsx"
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var headers = new[]
        {
            "试验编号", "工位", "采样时间", "经过时间(ms)", "采集序号", "循环", "阶段",
            "拉力(N)", "电流(A)", "电压(V)", "位移(mm)",
            "拉力输入(V)", "电流输入(V)", "电压输入(V)", "位移输入(V)",
            "DI位图", "数据质量", "控制器反馈报文"
        };
        var rows = samples.Select(sample => (IReadOnlyList<string>)new[]
        {
            sample.TestNo, sample.StationName, sample.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            sample.ElapsedMilliseconds.ToString(), sample.AcquisitionSequence.ToString(), sample.Cycle.ToString(), sample.Phase,
            sample.Force.ToString("0.###"), sample.Current.ToString("0.###"), sample.Voltage.ToString("0.###"),
            sample.Displacement.ToString("0.###"),
            FormatNullable(sample.ForceInputVoltage), FormatNullable(sample.CurrentInputVoltage),
            FormatNullable(sample.VoltageInputVoltage), FormatNullable(sample.DisplacementInputVoltage),
            $"0x{sample.DigitalInputs:X4}", sample.DataQuality, sample.ControllerFrame
        }).ToArray();
        if (Path.GetExtension(dialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            TabularExport.WriteTxt(dialog.FileName, headers, rows);
        else
            TabularExport.WriteXlsx(dialog.FileName, headers, rows);
        _database.AddLog("信息", "历史数据", $"导出 {record.TestNo} 原始采样点 {samples.Count} 条：{dialog.FileName}");
        MessageBox.Show($"已导出 {samples.Count:N0} 条原始采样数据。", "导出原始数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string FormatNullable(double? value) => value?.ToString("0.000000") ?? string.Empty;

    private static string FormatPlan(TestRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.PlanCode)) return record.PlanName;
        return $"{record.PlanCode} · {record.PlanName} · R{Math.Max(1, record.PlanRevision)}";
    }
}
