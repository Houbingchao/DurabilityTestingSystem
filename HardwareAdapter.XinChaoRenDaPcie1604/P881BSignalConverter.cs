using DurabilityTestingSystem.Infrastructure;

namespace DurabilityTestingSystem.HardwareAdapter.XinChaoRenDaPcie1604;

public static class P881BSignalConverter
{
    public static AnalogVoltageRange SelectInputRange(string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (signalType.Contains("0~10", StringComparison.OrdinalIgnoreCase) ||
            signalType.Contains("±10", StringComparison.OrdinalIgnoreCase) ||
            signalType.Contains("4~20", StringComparison.OrdinalIgnoreCase) ||
            signalType.Contains("0~5", StringComparison.OrdinalIgnoreCase))
        {
            // 4~20 mA 经 250 Ω 后的理论上限正好是 5 V。若选择 ±5 V，
            // 取样电阻误差、传感器过量程或瞬态噪声都会在 ADC 满量程处削顶，
            // 导致软件无法区分“合法满量程”和“已经过量程”。本项目基线统一
            // 留出硬件裕量使用 ±10 V；最终量程仍需结合实物噪声和标定报告复核。
            return AnalogVoltageRange.PlusMinus10V;
        }

        throw new ArgumentException($"不支持的 P-881B 信号类型：{signalType}", nameof(signalType));
    }

    public static double ToEngineeringValue(
        double measuredVoltage,
        string signalType,
        double engineeringFullScale,
        double calibrationGain = 1,
        double calibrationOffset = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (!double.IsFinite(measuredVoltage))
            throw new ArgumentOutOfRangeException(nameof(measuredVoltage), "测量电压必须是有限数值。");
        if (engineeringFullScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(engineeringFullScale), "传感器工程量满量程必须大于 0。");
        if (!double.IsFinite(engineeringFullScale) || !double.IsFinite(calibrationGain) || !double.IsFinite(calibrationOffset))
            throw new ArgumentOutOfRangeException(nameof(engineeringFullScale), "量程和标定参数必须是有限数值。");

        double normalized;
        if (signalType.Contains("4~20", StringComparison.OrdinalIgnoreCase))
        {
            // P-881B 的 250 Ω 取样电阻把 4~20 mA 转换为 1~5 V。
            normalized = (measuredVoltage - 1.0) / 4.0;
        }
        else if (signalType.Contains("±10", StringComparison.OrdinalIgnoreCase))
        {
            normalized = measuredVoltage / 10.0;
        }
        else if (signalType.Contains("0~10", StringComparison.OrdinalIgnoreCase))
        {
            normalized = measuredVoltage / 10.0;
        }
        else if (signalType.Contains("0~5", StringComparison.OrdinalIgnoreCase))
        {
            normalized = measuredVoltage / 5.0;
        }
        else
        {
            throw new ArgumentException($"不支持的 P-881B 信号类型：{signalType}", nameof(signalType));
        }

        return normalized * engineeringFullScale * calibrationGain + calibrationOffset;
    }

    public static bool IsElectricalSignalPlausible(double measuredVoltage, string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (!double.IsFinite(measuredVoltage)) return false;

        // These are the electrical spans documented by the two manuals, not commissioning margins.
        // Cable-loss, noise and disconnect margins must become explicit, calibrated site settings.
        if (signalType.Contains("4~20", StringComparison.OrdinalIgnoreCase))
            return measuredVoltage is >= 1.0 and <= 5.0;
        if (signalType.Contains("±10", StringComparison.OrdinalIgnoreCase))
            return measuredVoltage is >= -10.0 and <= 10.0;
        if (signalType.Contains("0~10", StringComparison.OrdinalIgnoreCase))
            return measuredVoltage is >= 0.0 and <= 10.0;
        if (signalType.Contains("0~5", StringComparison.OrdinalIgnoreCase))
            return measuredVoltage is >= 0.0 and <= 5.0;
        throw new ArgumentException($"不支持的 P-881B 信号类型：{signalType}", nameof(signalType));
    }
}
