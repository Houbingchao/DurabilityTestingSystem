using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

public static class SettingsValidator
{
    public static OperationResult Validate(TestSettings settings)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.ProjectName)) errors.Add("项目名称不能为空");
        if (string.IsNullOrWhiteSpace(settings.PlanCode)) errors.Add("方案编号不能为空");
        if (settings.TargetCycles <= 0) errors.Add("目标循环次数必须大于 0");
        if (settings.ForceLowerLimit >= settings.ForceUpperLimit) errors.Add("拉力下限必须小于拉力上限");
        if (settings.TargetForce < settings.ForceLowerLimit || settings.TargetForce > settings.ForceUpperLimit)
            errors.Add("目标拉力必须位于判定下限与上限之间");
        if (settings.MaxForceProtection < settings.ForceUpperLimit)
            errors.Add("拉力硬保护上限不得低于判定上限");
        if (settings.SensorRange < settings.MaxForceProtection)
            errors.Add("拉力传感器量程不得小于硬保护上限");
        if (settings.MaxCurrentProtection > settings.CurrentSensorRange)
            errors.Add("电流保护上限不得超过电流传感器量程");
        if (settings.MaxVoltageProtection > settings.VoltageSensorRange)
            errors.Add("电压保护上限不得超过电压传感器量程");
        if (settings.PullDuration <= 0 || settings.ReturnDuration <= 0 || settings.HoldDuration < 0)
            errors.Add("动作时间参数无效");
        if (settings.SampleInterval is < 20 or > 5000) errors.Add("采样周期必须在 20~5000 ms 之间");
        if (settings.CanBaudRate <= 0 || settings.CanNodeId is < 1 or > 127) errors.Add("CAN 波特率或节点 ID 无效");

        var analogChannels = new[] { settings.ForceChannel, settings.CurrentChannel, settings.VoltageChannel };
        if (analogChannels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != analogChannels.Length)
            errors.Add("拉力、电流和电压不能使用同一个模拟量通道");

        var safetyInputs = new[] { settings.PositiveLimitInput, settings.NegativeLimitInput, settings.SafetyDoorInput }
            .Where(x => !string.Equals(x, "禁用", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (safetyInputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != safetyInputs.Length)
            errors.Add("正限位、反限位和安全门不能复用同一个数字量输入");

        return errors.Count == 0
            ? OperationResult.Ok("参数校验通过。")
            : OperationResult.Fail("参数校验失败：" + string.Join("；", errors) + "。");
    }
}
