using DurabilityTestingSystem.Data;
using DurabilityTestingSystem.Infrastructure;
using DurabilityTestingSystem.Models;
using DurabilityTestingSystem.UI.Controls;

namespace DurabilityTestingSystem.UI.Pages;

public sealed class PlansPage : UserControl, IRefreshablePage
{
    private readonly AppDatabase _database;
    private readonly Func<TestSettings> _getSettings;
    private readonly DataGridView _planGrid;
    private readonly DataGridView _stepGrid;
    private readonly TextBox _code;
    private readonly TextBox _name;
    private readonly NumericUpDown _cycles;
    private readonly NumericUpDown _force;
    private readonly CheckBox _enabled;
    private long _selectedId;

    public event EventHandler? PlansChanged;
    public event EventHandler<PlanAppliedEventArgs>? PlanApplied;

    public PlansPage(AppDatabase database, Func<TestSettings> getSettings)
    {
        _database = database;
        _getSettings = getSettings;
        BackColor = Theme.Window;

        var toolbar = new CardPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(16, 11, 16, 10) };
        var title = UiFactory.Label("试验方案库", 11, Theme.Text, FontStyle.Bold);
        title.Location = new Point(16, 12);
        var subtitle = UiFactory.Label("维护可复用的测试参数与循环步骤", 7.5f, Theme.Muted);
        subtitle.Location = new Point(16, 36);
        var actions = new TableLayoutPanel { Dock = DockStyle.Right, Width = 370, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        for (var column = 0; column < 3; column++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var newButton = UiFactory.SecondaryButton("＋ 新建方案", 110);
        var duplicateButton = UiFactory.SecondaryButton("⧉ 复制方案", 110);
        var importButton = UiFactory.SecondaryButton("↓ 导入模板", 110);
        foreach (var button in new[] { newButton, duplicateButton, importButton })
        {
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(5, 2, 5, 2);
        }
        actions.Controls.Add(newButton, 0, 0);
        actions.Controls.Add(duplicateButton, 1, 0);
        actions.Controls.Add(importButton, 2, 0);
        toolbar.Controls.Add(actions);
        toolbar.Controls.Add(title);
        toolbar.Controls.Add(subtitle);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 14, 0, 0)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        var listCard = UiFactory.Card("方案列表", "双击或选择方案后在右侧编辑");
        listCard.Dock = DockStyle.Fill;
        listCard.Margin = new Padding(0, 0, 8, 0);
        _planGrid = UiFactory.Grid();
        _planGrid.ReadOnly = true;
        _planGrid.AutoGenerateColumns = false;
        _planGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "方案编号", FillWeight = 24 });
        _planGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "方案名称", FillWeight = 44 });
        _planGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cycles", HeaderText = "循环次数", FillWeight = 19 });
        _planGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "状态", FillWeight = 13 });
        listCard.Controls.Add(_planGrid);

        var editor = UiFactory.Card("方案编辑", "方案参数将作为试验启动时的默认值");
        editor.Dock = DockStyle.Fill;
        editor.Margin = new Padding(8, 0, 0, 0);

        _code = UiFactory.TextBox();
        _name = UiFactory.TextBox();
        _cycles = UiFactory.Numeric(50000, 1, 10000000, 0, 1000);
        _force = UiFactory.Numeric(450, 1, 100000, 1, 10);
        _enabled = new CheckBox
        {
            Text = "启用该方案，可在试验控制页中选择",
            Checked = true,
            Font = Theme.Font(8.5f),
            ForeColor = Theme.Text,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        var basic = new TableLayoutPanel { Dock = DockStyle.Top, Height = 95, ColumnCount = 4, Padding = new Padding(0, 0, 0, 7) };
        basic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        basic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        basic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        basic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        basic.Controls.Add(UiFactory.Field("方案编号", _code), 0, 0);
        basic.Controls.Add(UiFactory.Field("方案名称", _name), 1, 0);
        basic.Controls.Add(UiFactory.Field("循环次数", _cycles, "次"), 2, 0);
        basic.Controls.Add(UiFactory.Field("目标拉力", _force, "N"), 3, 0);

        var stepHeader = new Panel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(0, 4, 0, 4) };
        var stepTitle = UiFactory.Label("循环步骤", 9, Theme.Text, FontStyle.Bold, DockStyle.Left);
        stepTitle.Width = 120;
        var stepButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 208, FlowDirection = FlowDirection.LeftToRight };
        var addStep = UiFactory.SecondaryButton("＋ 添加步骤", 100);
        var deleteStep = UiFactory.SecondaryButton("－ 删除步骤", 100);
        foreach (var button in new[] { addStep, deleteStep })
        {
            button.Height = 32;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.Margin = new Padding(2);
        }
        stepButtons.Controls.AddRange([addStep, deleteStep]);
        stepHeader.Controls.Add(stepButtons);
        stepHeader.Controls.Add(stepTitle);

        _stepGrid = UiFactory.Grid();
        _stepGrid.AutoGenerateColumns = false;
        _stepGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "序号", FillWeight = 9, ReadOnly = true });
        _stepGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "动作类型",
            FillWeight = 24,
            DataSource = new[] { "CAN 动作", "正向拉伸", "负载保持", "反向回程", "弹簧复位确认", "等待", "报警判定", "循环计数" }
        });
        _stepGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "目标值", FillWeight = 18 });
        _stepGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "持续时间 (s)", FillWeight = 18 });
        _stepGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "完成条件", FillWeight = 31 });
        LoadDefaultSteps();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(0, 10, 0, 0) };
        _enabled.Location = new Point(2, 13);
        var saveButton = UiFactory.Button("✓  保存方案", Theme.Primary, Color.White, 118);
        saveButton.Dock = DockStyle.Right;
        var applyButton = UiFactory.SecondaryButton("应用到当前试验", 145);
        applyButton.Dock = DockStyle.Right;
        applyButton.FlatAppearance.BorderSize = 1;
        applyButton.FlatAppearance.BorderColor = Theme.Border;
        bottom.Controls.Add(saveButton);
        bottom.Controls.Add(applyButton);
        bottom.Controls.Add(_enabled);

        editor.Controls.Add(_stepGrid);
        editor.Controls.Add(stepHeader);
        editor.Controls.Add(basic);
        editor.Controls.Add(bottom);
        body.Controls.Add(listCard, 0, 0);
        body.Controls.Add(editor, 1, 0);

        Controls.Add(body);
        Controls.Add(toolbar);

        newButton.Click += (_, _) => NewPlan();
        duplicateButton.Click += (_, _) => DuplicatePlan();
        importButton.Click += (_, _) => MessageBox.Show("Demo 版本已预留 Excel/JSON 方案模板导入入口。", "导入模板", MessageBoxButtons.OK, MessageBoxIcon.Information);
        saveButton.Click += (_, _) =>
        {
            if (TrySavePlan(out _, out _))
                MessageBox.Show("方案及步骤已保存。", "保存方案", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        applyButton.Click += (_, _) => ApplyCurrentPlan();
        addStep.Click += (_, _) => _stepGrid.Rows.Add(_stepGrid.Rows.Count + 1, "等待", "—", "1.0", "时间到");
        deleteStep.Click += (_, _) =>
        {
            if (_stepGrid.CurrentRow is not null) _stepGrid.Rows.Remove(_stepGrid.CurrentRow);
            RenumberSteps();
        };
        _planGrid.SelectionChanged += (_, _) => LoadSelectedPlan();
        _force.ValueChanged += (_, _) => SynchronizeForceTargets();
    }

    public void RefreshData()
    {
        var selected = _selectedId;
        _planGrid.Rows.Clear();
        foreach (var plan in _database.GetPlans())
        {
            var index = _planGrid.Rows.Add(plan.Code, plan.Name, plan.Cycles.ToString("N0"), plan.Enabled ? "启用" : "停用");
            _planGrid.Rows[index].Tag = plan;
            if (plan.Id == selected) _planGrid.Rows[index].Selected = true;
        }
        if (_planGrid.Rows.Count > 0 && _planGrid.SelectedRows.Count == 0) _planGrid.Rows[0].Selected = true;
        LoadSelectedPlan();
    }

    private void LoadSelectedPlan()
    {
        if (_planGrid.SelectedRows.Count == 0 || _planGrid.SelectedRows[0].Tag is not TestPlan plan) return;
        _selectedId = plan.Id;
        _code.Text = plan.Code;
        _name.Text = plan.Name;
        _cycles.Value = Math.Clamp(plan.Cycles, (int)_cycles.Minimum, (int)_cycles.Maximum);
        _force.Value = Math.Clamp((decimal)plan.TargetForce, _force.Minimum, _force.Maximum);
        _enabled.Checked = plan.Enabled;
        LoadSteps(_database.GetPlanSteps(plan.Id));
    }

    private void NewPlan()
    {
        _selectedId = 0;
        _code.Text = $"SB-DUR-{_database.GetPlans().Count + 1:000}";
        _name.Text = "新建耐久试验方案";
        _cycles.Value = 50000;
        _force.Value = 450;
        _enabled.Checked = true;
        LoadDefaultSteps();
        _name.Focus();
        _name.SelectAll();
    }

    private void DuplicatePlan()
    {
        _selectedId = 0;
        _code.Text += "-COPY";
        _name.Text += "（副本）";
        _name.Focus();
    }

    private void ApplyCurrentPlan()
    {
        if (!TrySavePlan(out var savedPlan, out var savedSteps) || savedPlan is null) return;

        var compilation = TestPlanCompiler.Compile(savedPlan, savedSteps, _getSettings());
        if (!compilation.Success || compilation.Plan is null)
        {
            MessageBox.Show(
                $"方案已经保存，但未应用到当前试验。\n\n{compilation.Message}",
                "方案不允许应用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var args = new PlanAppliedEventArgs(compilation.Plan);
        PlanApplied?.Invoke(this, args);
        if (!args.Result.Success)
        {
            MessageBox.Show(
                $"方案已经保存并通过校验，但当前试验页拒绝应用。\n\n{args.Result.Message}",
                "应用方案失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(
            $"方案“{savedPlan.Name}”已保存并应用到当前试验。\n启动前系统还会从数据库重新读取并冻结一次参数快照。",
            "应用方案",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private bool TrySavePlan(out TestPlan? savedPlan, out IReadOnlyList<TestPlanStep> savedSteps)
    {
        savedPlan = null;
        savedSteps = [];
        if (string.IsNullOrWhiteSpace(_code.Text) || string.IsNullOrWhiteSpace(_name.Text))
        {
            MessageBox.Show("方案编号和方案名称不能为空。", "方案校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var steps = ReadSteps();
        if (steps.Count == 0)
        {
            MessageBox.Show("试验方案至少需要一个循环步骤。", "方案校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var plan = new TestPlan
        {
            Id = _selectedId,
            Code = _code.Text.Trim(),
            Name = _name.Text.Trim(),
            Cycles = (int)_cycles.Value,
            TargetForce = (double)_force.Value,
            Enabled = _enabled.Checked,
            UpdatedAt = DateTime.Now
        };
        long planId;
        try
        {
            planId = _database.UpsertPlan(plan);
            _database.SavePlanSteps(planId, steps);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"方案保存失败：\n\n{ex.Message}", "方案保存", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        _selectedId = planId;
        plan = _database.GetPlans().First(x => x.Id == planId);
        foreach (var step in steps) step.PlanId = planId;
        savedPlan = plan;
        savedSteps = steps;
        RefreshData();
        PlansChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private IReadOnlyList<TestPlanStep> ReadSteps()
    {
        var result = new List<TestPlanStep>();
        for (var i = 0; i < _stepGrid.Rows.Count; i++)
        {
            var row = _stepGrid.Rows[i];
            if (row.IsNewRow) continue;
            var action = Convert.ToString(row.Cells[1].Value)?.Trim();
            if (string.IsNullOrWhiteSpace(action)) continue;
            var durationText = Convert.ToString(row.Cells[3].Value)?.Trim();
            if (!double.TryParse(durationText, out var duration) || duration < 0)
            {
                MessageBox.Show($"第 {i + 1} 步持续时间格式不正确。", "方案校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return [];
            }
            result.Add(new TestPlanStep
            {
                PlanId = _selectedId,
                Sequence = result.Count + 1,
                ActionType = action,
                TargetValue = Convert.ToString(row.Cells[2].Value)?.Trim() ?? "—",
                DurationSeconds = duration,
                CompletionCondition = Convert.ToString(row.Cells[4].Value)?.Trim() ?? "时间到"
            });
        }
        return result;
    }

    private void LoadSteps(IReadOnlyList<TestPlanStep> steps)
    {
        if (steps.Count == 0)
        {
            LoadDefaultSteps();
            return;
        }
        _stepGrid.Rows.Clear();
        foreach (var step in steps)
            _stepGrid.Rows.Add(step.Sequence, step.ActionType, step.TargetValue,
                step.DurationSeconds.ToString("0.###"), step.CompletionCondition);
    }

    private void LoadDefaultSteps()
    {
        _stepGrid.Rows.Clear();
        var target = $"{_force.Value:0.###} N";
        _stepGrid.Rows.Add("1", "正向拉伸", target, "2.0", "达到目标拉力");
        _stepGrid.Rows.Add("2", "负载保持", target, "1.0", "保持时间到");
        _stepGrid.Rows.Add("3", "反向回程", "0 mm", "2.0", "到达原点");
        _stepGrid.Rows.Add("4", "弹簧复位确认", "≤2 mm", "0", "位移回零，否则报警");
        _stepGrid.Rows.Add("5", "等待", "—", "0.5", "动作间隔时间到");
        _stepGrid.Rows.Add("6", "循环计数", "+1", "0", "进入下一循环");
    }

    private void RenumberSteps()
    {
        for (var i = 0; i < _stepGrid.Rows.Count; i++) _stepGrid.Rows[i].Cells[0].Value = i + 1;
    }

    private void SynchronizeForceTargets()
    {
        var target = $"{_force.Value:0.###} N";
        foreach (DataGridViewRow row in _stepGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var action = Convert.ToString(row.Cells[1].Value)?.Trim();
            if (action is "正向拉伸" or "负载保持") row.Cells[2].Value = target;
        }
    }
}

public sealed class PlanAppliedEventArgs(CompiledTestPlan plan) : EventArgs
{
    public CompiledTestPlan Plan { get; } = plan;
    public OperationResult Result { get; set; } = OperationResult.Fail("当前试验页未处理方案应用请求。");
}
