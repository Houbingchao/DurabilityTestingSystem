using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

/// <summary>
/// 一期方案编译器。只接受已经过验证的固定六步模板，并将方案冻结为一次试验使用的参数快照。
/// 未被本编译器明确支持的动作一律拒绝，避免界面显示某方案而设备仍按全局参数运行。
/// </summary>
public static partial class TestPlanCompiler
{
    private static readonly string[] ExpectedActions =
    [
        "正向拉伸",
        "负载保持",
        "反向回程",
        "弹簧复位确认",
        "等待",
        "循环计数"
    ];

    public static TestPlanCompilationResult Compile(
        TestPlan plan,
        IReadOnlyList<TestPlanStep> sourceSteps,
        TestSettings baseSettings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sourceSteps);
        ArgumentNullException.ThrowIfNull(baseSettings);

        if (!plan.Enabled)
            return TestPlanCompilationResult.Fail($"方案“{plan.Name}”已停用，禁止应用或启动。");
        if (string.IsNullOrWhiteSpace(plan.Code) || string.IsNullOrWhiteSpace(plan.Name))
            return TestPlanCompilationResult.Fail("方案编号和方案名称不能为空。");
        if (plan.Cycles <= 0)
            return TestPlanCompilationResult.Fail("方案循环次数必须大于 0。");
        if (!double.IsFinite(plan.TargetForce) || plan.TargetForce <= 0)
            return TestPlanCompilationResult.Fail("方案目标拉力必须是大于 0 的有效数值。");

        var steps = sourceSteps.OrderBy(x => x.Sequence).ToArray();
        if (steps.Length != ExpectedActions.Length)
        {
            return TestPlanCompilationResult.Fail(
                $"一期方案必须严格包含 {ExpectedActions.Length} 步：{string.Join(" → ", ExpectedActions)}；当前为 {steps.Length} 步。");
        }

        for (var index = 0; index < steps.Length; index++)
        {
            var step = steps[index];
            if (step.Sequence != index + 1)
                return TestPlanCompilationResult.Fail($"方案步骤序号必须从 1 连续排列；第 {index + 1} 行的序号为 {step.Sequence}。");

            var action = NormalizeAction(step.ActionType);
            if (action is "CAN动作" or "报警判定")
            {
                return TestPlanCompilationResult.Fail(
                    $"第 {step.Sequence} 步“{step.ActionType}”不在一期安全模板内，禁止应用或启动；请等待 DBC/字节协议和判定规则冻结后再开发该动作。");
            }

            var expected = NormalizeAction(ExpectedActions[index]);
            if (!string.Equals(action, expected, StringComparison.Ordinal))
            {
                return TestPlanCompilationResult.Fail(
                    $"第 {step.Sequence} 步必须是“{ExpectedActions[index]}”，当前为“{step.ActionType}”。一期只支持：{string.Join(" → ", ExpectedActions)}。");
            }
            if (!double.IsFinite(step.DurationSeconds) || step.DurationSeconds < 0)
                return TestPlanCompilationResult.Fail($"第 {step.Sequence} 步持续时间必须是大于或等于 0 的有效数值。");
            if (string.IsNullOrWhiteSpace(step.CompletionCondition))
                return TestPlanCompilationResult.Fail($"第 {step.Sequence} 步完成条件不能为空。");
        }

        if (steps[0].DurationSeconds <= 0)
            return TestPlanCompilationResult.Fail("正向拉伸时间必须大于 0 秒。");
        if (steps[2].DurationSeconds <= 0)
            return TestPlanCompilationResult.Fail("反向回程时间必须大于 0 秒。");
        if (steps[3].DurationSeconds != 0)
            return TestPlanCompilationResult.Fail("弹簧复位确认为即时判定步骤，持续时间必须为 0 秒。");
        if (steps[5].DurationSeconds != 0)
            return TestPlanCompilationResult.Fail("循环计数为即时步骤，持续时间必须为 0 秒。");

        if (!ForceTargetPattern().IsMatch(steps[0].TargetValue ?? string.Empty) ||
            !TryReadNumber(steps[0].TargetValue, out var pullForce) ||
            !NearlyEqual(pullForce, plan.TargetForce))
        {
            return TestPlanCompilationResult.Fail(
                $"第 1 步目标值必须与方案目标拉力一致（{plan.TargetForce:0.###} N），当前为“{steps[0].TargetValue}”。");
        }
        if (!ForceTargetPattern().IsMatch(steps[1].TargetValue ?? string.Empty) ||
            !TryReadNumber(steps[1].TargetValue, out var holdForce) ||
            !NearlyEqual(holdForce, plan.TargetForce))
        {
            return TestPlanCompilationResult.Fail(
                $"第 2 步目标值必须与方案目标拉力一致（{plan.TargetForce:0.###} N），当前为“{steps[1].TargetValue}”。");
        }
        if (!DisplacementTargetPattern().IsMatch(steps[2].TargetValue ?? string.Empty) ||
            !TryReadNumber(steps[2].TargetValue, out var returnTarget) || Math.Abs(returnTarget) > 0.001)
            return TestPlanCompilationResult.Fail("第 3 步反向回程目标必须为 0 mm。");
        if (!ResetTolerancePattern().IsMatch(steps[3].TargetValue ?? string.Empty) ||
            !TryReadNumber(steps[3].TargetValue, out var resetTolerance) || resetTolerance <= 0)
            return TestPlanCompilationResult.Fail("第 4 步必须填写大于 0 的复位容差，例如“≤2 mm”。");
        if (!CycleIncrementPattern().IsMatch(steps[5].TargetValue ?? string.Empty) ||
            !TryReadNumber(steps[5].TargetValue, out var increment) || !NearlyEqual(increment, 1))
            return TestPlanCompilationResult.Fail("第 6 步循环计数目标必须为“+1”。");

        TestSettings snapshot;
        try
        {
            snapshot = CloneSettings(baseSettings);
        }
        catch (Exception ex)
        {
            return TestPlanCompilationResult.Fail($"冻结试验参数失败：{ex.Message}");
        }

        snapshot.PlanCode = plan.Code.Trim();
        snapshot.TargetForce = plan.TargetForce;
        snapshot.TargetCycles = plan.Cycles;
        snapshot.PullDuration = steps[0].DurationSeconds;
        snapshot.HoldDuration = steps[1].DurationSeconds;
        snapshot.ReturnDuration = steps[2].DurationSeconds;
        snapshot.ResetDisplacementTolerance = resetTolerance;
        snapshot.ActionInterval = steps[4].DurationSeconds;

        var settingsValidation = SettingsValidator.Validate(snapshot);
        if (!settingsValidation.Success)
        {
            return TestPlanCompilationResult.Fail(
                $"方案与当前设备/保护参数不兼容：{settingsValidation.Message}");
        }

        return TestPlanCompilationResult.Ok(new CompiledTestPlan(plan, steps, snapshot));
    }

    internal static TestSettings CloneSettings(TestSettings settings)
    {
        var json = JsonSerializer.Serialize(settings);
        var clone = JsonSerializer.Deserialize<TestSettings>(json)
                    ?? throw new InvalidOperationException("参数快照反序列化结果为空。");
        clone.EnsureStationConfigurations();
        return clone;
    }

    private static string NormalizeAction(string? action) =>
        string.Concat((action ?? string.Empty).Where(character => !char.IsWhiteSpace(character)));

    private static bool TryReadNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var match = NumberPattern().Match(text);
        return match.Success &&
               double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(0.001, Math.Abs(right) * 0.0001);

    [GeneratedRegex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^\s*[-+]?\d+(?:\.\d+)?\s*N\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForceTargetPattern();

    [GeneratedRegex(@"^\s*[-+]?\d+(?:\.\d+)?\s*mm\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisplacementTargetPattern();

    [GeneratedRegex(@"^\s*(?:≤|<=)?\s*[-+]?\d+(?:\.\d+)?\s*mm\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResetTolerancePattern();

    [GeneratedRegex(@"^\s*\+1\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CycleIncrementPattern();
}

public sealed class CompiledTestPlan
{
    private readonly TestSettings _settingsSnapshot;

    internal CompiledTestPlan(TestPlan plan, IReadOnlyList<TestPlanStep> steps, TestSettings settingsSnapshot)
    {
        PlanId = plan.Id;
        PlanRevision = Math.Max(1, plan.Revision);
        PlanCode = plan.Code;
        PlanName = plan.Name;
        PlanUpdatedAt = plan.UpdatedAt;
        Steps = steps.Select(CloneStep).ToArray();
        _settingsSnapshot = TestPlanCompiler.CloneSettings(settingsSnapshot);
    }

    public long PlanId { get; }
    public int PlanRevision { get; }
    public string PlanCode { get; }
    public string PlanName { get; }
    public DateTime PlanUpdatedAt { get; }
    public IReadOnlyList<TestPlanStep> Steps { get; }
    public int TargetCycles => _settingsSnapshot.TargetCycles;
    public double TargetForce => _settingsSnapshot.TargetForce;
    public double PullDuration => _settingsSnapshot.PullDuration;
    public double HoldDuration => _settingsSnapshot.HoldDuration;
    public double ReturnDuration => _settingsSnapshot.ReturnDuration;
    public double ActionInterval => _settingsSnapshot.ActionInterval;
    public double ResetDisplacementTolerance => _settingsSnapshot.ResetDisplacementTolerance;

    public TestSettings CreateSettingsSnapshot() => TestPlanCompiler.CloneSettings(_settingsSnapshot);

    public string CreateAuditSnapshotJson()
    {
        var snapshot = new
        {
            schemaVersion = 1,
            plan = new
            {
                id = PlanId,
                code = PlanCode,
                name = PlanName,
                revision = PlanRevision,
                updatedAt = PlanUpdatedAt
            },
            steps = Steps,
            effectiveSettings = _settingsSnapshot
        };
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
    }

    private static TestPlanStep CloneStep(TestPlanStep step) => new()
    {
        Id = step.Id,
        PlanId = step.PlanId,
        Sequence = step.Sequence,
        ActionType = step.ActionType,
        TargetValue = step.TargetValue,
        DurationSeconds = step.DurationSeconds,
        CompletionCondition = step.CompletionCondition
    };
}

public sealed record TestPlanCompilationResult(bool Success, string Message, CompiledTestPlan? Plan)
{
    public static TestPlanCompilationResult Ok(CompiledTestPlan plan) =>
        new(true, $"方案“{plan.PlanName}”已通过固定模板校验。", plan);

    public static TestPlanCompilationResult Fail(string message) => new(false, message, null);
}
