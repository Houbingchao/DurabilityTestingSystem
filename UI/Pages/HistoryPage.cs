using System.Text;
using DurabilityTestingSystem.Data;
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
        _keyword.PlaceholderText = "输入试验编号或试件编号";
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

        var rightActions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 270, FlowDirection = FlowDirection.LeftToRight };
        var reportButton = UiFactory.SecondaryButton("查看报告", 105);
        var exportButton = UiFactory.SecondaryButton("导出 CSV", 105);
        foreach (var button in new[] { reportButton, exportButton })
        {
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.Margin = new Padding(6, 7, 6, 0);
        }
        rightActions.Controls.AddRange([reportButton, exportButton]);
        filter.Controls.Add(rightActions);
        filter.Controls.Add(flow);

        var listCard = UiFactory.Card("试验记录", "显示最近 500 条记录，可按条件筛选与导出");
        listCard.Dock = DockStyle.Fill;
        listCard.Margin = new Padding(0, 14, 0, 0);
        _grid = UiFactory.Grid();
        _grid.ReadOnly = true;
        _grid.AutoGenerateColumns = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "试验编号", FillWeight = 17 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "试件编号", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "试验方案", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "开始时间", FillWeight = 17 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "时长", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "完成循环", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "峰值拉力", FillWeight = 11 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "结果", FillWeight = 9 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "操作员", FillWeight = 9 });
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != 7 || e.Value is null || e.CellStyle is null) return;
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
        exportButton.Click += (_, _) => ExportCsv();
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
                record.PlanName,
                record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                UiFactory.FormatDuration(record.Duration),
                record.Cycles.ToString("N0"),
                $"{record.PeakForce:0.0} N",
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
            $"试验编号：{record.TestNo}\n试件编号：{record.SpecimenNo}\n执行方案：{record.PlanName}\n开始时间：{record.StartedAt:yyyy-MM-dd HH:mm:ss}\n完成循环：{record.Cycles:N0} 次\n峰值拉力：{record.PeakForce:0.0} N\n最终判定：{record.Result}\n\nDemo 版本已预留 PDF 试验报告生成接口。",
            "试验记录详情", MessageBoxButtons.OK,
            record.Result == "合格" ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ExportCsv()
    {
        if (_currentRecords.Count == 0)
        {
            MessageBox.Show("当前筛选结果为空。", "导出 CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"安全带耐久试验记录_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
        writer.WriteLine("试验编号,试件编号,试验方案,开始时间,时长,完成循环,峰值拉力(N),结果,操作员");
        foreach (var record in _currentRecords)
        {
            writer.WriteLine(string.Join(",",
                Csv(record.TestNo), Csv(record.SpecimenNo), Csv(record.PlanName),
                Csv(record.StartedAt.ToString("yyyy-MM-dd HH:mm:ss")), Csv(record.Duration.ToString()),
                record.Cycles, record.PeakForce.ToString("0.0"), Csv(record.Result), Csv(record.Operator)));
        }
        _database.AddLog("信息", "历史数据", $"导出试验记录 {_currentRecords.Count} 条");
        MessageBox.Show("CSV 文件导出完成。", "导出 CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
