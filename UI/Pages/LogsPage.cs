using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI.Pages;

public sealed class LogsPage : UserControl, IRefreshablePage
{
    private readonly AppDatabase _database;
    private readonly DataGridView _grid;
    private readonly ComboBox _level;
    private readonly Label _countLabel;

    public LogsPage(AppDatabase database)
    {
        _database = database;
        BackColor = Theme.Window;

        var summary = new CardPanel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(17, 12, 17, 10) };
        var title = UiFactory.Label("运行与操作日志", 11, Theme.Text, FontStyle.Bold);
        title.Location = new Point(17, 12);
        var subtitle = UiFactory.Label("记录系统启动、通讯状态、参数修改、试验操作与报警事件", 7.5f, Theme.Muted);
        subtitle.Location = new Point(17, 39);
        _countLabel = UiFactory.Label("共 0 条", 8.5f, Theme.Muted, FontStyle.Bold);
        _countLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _countLabel.AutoSize = false;
        _countLabel.Size = new Size(110, 30);
        _countLabel.TextAlign = ContentAlignment.MiddleRight;
        _countLabel.Location = new Point(970, 21);
        summary.Controls.AddRange([title, subtitle, _countLabel]);
        summary.Resize += (_, _) => _countLabel.Left = summary.ClientSize.Width - _countLabel.Width - 18;

        var filter = new CardPanel { Dock = DockStyle.Top, Height = 66, Margin = new Padding(0, 14, 0, 0), Padding = new Padding(16, 9, 16, 9) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 570, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var label = UiFactory.Label("日志级别", 8, Theme.Muted);
        label.AutoSize = false;
        label.Size = new Size(70, 40);
        label.TextAlign = ContentAlignment.MiddleLeft;
        _level = UiFactory.Combo(["全部", "信息", "警告", "报警"], "全部");
        _level.Width = 120;
        var refreshButton = UiFactory.Button("↻  刷新", Theme.Primary, Color.White, 90);
        refreshButton.Margin = new Padding(12, 7, 0, 0);
        var note = UiFactory.Label("日志自动保存在 SQLite 数据库，最新事件显示在最上方", 8, Theme.Muted);
        note.AutoSize = false;
        note.Size = new Size(280, 40);
        note.TextAlign = ContentAlignment.MiddleLeft;
        note.Margin = new Padding(18, 2, 0, 0);
        flow.Controls.AddRange([label, _level, refreshButton, note]);
        filter.Controls.Add(flow);

        var listCard = UiFactory.Card("事件列表", "系统日志用于问题追溯与现场调试");
        listCard.Dock = DockStyle.Fill;
        listCard.Margin = new Padding(0, 14, 0, 0);
        _grid = UiFactory.Grid();
        _grid.ReadOnly = true;
        _grid.AutoGenerateColumns = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "时间", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "级别", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "来源", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "事件内容", FillWeight = 56 });
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != 1 || e.Value is null || e.CellStyle is null) return;
            e.CellStyle.ForeColor = Convert.ToString(e.Value) switch
            {
                "报警" => Theme.Red,
                "警告" => Theme.Orange,
                _ => Theme.Green
            };
            e.CellStyle.Font = Theme.Font(8.5f, FontStyle.Bold);
        };
        listCard.Controls.Add(_grid);

        Controls.Add(listCard);
        Controls.Add(filter);
        Controls.Add(summary);

        refreshButton.Click += (_, _) => RefreshData();
        _level.SelectedIndexChanged += (_, _) => RefreshData();
    }

    public void RefreshData()
    {
        var logs = _database.GetLogs(_level.Text);
        _grid.Rows.Clear();
        foreach (SystemLogEntry log in logs)
            _grid.Rows.Add(log.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"), log.Level, log.Source, log.Message);
        _countLabel.Text = $"共 {logs.Count:N0} 条";
    }
}
