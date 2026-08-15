using DurabilityTestingSystem.Models;

namespace DurabilityTestingSystem.Infrastructure;

public static class SettingsValidator
{
    public static OperationResult Validate(TestSettings settings)
    {
        settings.EnsureStationConfigurations();
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
        if (settings.MaxDisplacementProtection > settings.DisplacementSensorRange)
            errors.Add("位移保护上限不得超过位移传感器量程");
        if (settings.PullDuration <= 0 || settings.ReturnDuration <= 0 || settings.HoldDuration < 0 || settings.ActionInterval < 0)
            errors.Add("动作时间参数无效");
        if (!string.Equals(settings.OverLimitAction, "立即停止并报警", StringComparison.Ordinal))
            errors.Add("当前正式基线仅允许“立即停止并报警”；减速或仅记录策略尚无经验证的安全控制实现");
        if (settings.SampleInterval is < 20 or > 5000) errors.Add("采样周期必须在 20~5000 ms 之间");
        if (!string.Equals(settings.CanDevice, CanHardwareBaseline.DisplayName, StringComparison.Ordinal))
            errors.Add($"CAN 设备必须使用已冻结型号：{CanHardwareBaseline.DisplayName}");
        if (settings.CanDeviceIndex is < 0 or > 31) errors.Add("USBCANFD-200U 设备索引必须在 0~31 范围内");
        if (!CanHardwareBaseline.SupportedArbitrationBaudRates.Contains(settings.CanBaudRate))
            errors.Add("USBCANFD-200U 仲裁域波特率不在官方标准波特率列表中");
        if (settings.CanBusMode is not ("CAN 2.0" or "CAN FD")) errors.Add("CAN 总线模式只能是 CAN 2.0 或 CAN FD");
        if (settings.CanBusMode == "CAN FD" && !CanHardwareBaseline.SupportedDataBaudRates.Contains(settings.CanDataBaudRate))
            errors.Add("USBCANFD-200U 数据域波特率不在官方标准波特率列表中");
        if (settings.CanFdStandard is not ("ISO" or "Non-ISO")) errors.Add("CAN FD 标准只能是 ISO 或 Non-ISO");
        if (settings.CanTransmitTimeout is < 1 or > 4000) errors.Add("CAN 发送超时必须在 1~4000 ms 范围内");
        if (!string.Equals(settings.AnalogDevice, AnalogHardwareBaseline.DisplayName, StringComparison.Ordinal))
            errors.Add($"模拟量采集卡必须使用已冻结型号：{AnalogHardwareBaseline.DisplayName}");
        if (!string.Equals(settings.AnalogTerminalBoard, AnalogHardwareBaseline.TerminalDisplayName, StringComparison.Ordinal))
            errors.Add($"模拟量接线端子必须使用已冻结型号：{AnalogHardwareBaseline.TerminalDisplayName}");
        if (settings.AnalogBoardId is < 0 or > 15) errors.Add("PCIE-1604 板卡拨码 ID 必须在 0~15 范围内");
        if (!AnalogHardwareBaseline.SupportedInputModes.Contains(settings.AnalogInputMode))
            errors.Add("PCIE-1604 输入方式只能为单端32路或差分16路");
        if (settings.AnalogScanRate is < 1 or > AnalogHardwareBaseline.MaximumSoftwareScanRateHz)
            errors.Add($"PCIE-1604 每通道扫描率必须在 1~{AnalogHardwareBaseline.MaximumSoftwareScanRateHz} Hz 范围内");
        if (settings.AnalogScanRate < Math.Ceiling(1000.0 / settings.SampleInterval))
            errors.Add("PCIE-1604 每通道扫描率不得低于软件采样/控制频率");
        if (settings.AnalogReadTimeout is < 50 or > 30000)
            errors.Add("PCIE-1604 数据停滞超时必须在 50~30000 ms 范围内");
        if (settings.FilterWindow is < 1 or > 1000) errors.Add("模拟量滤波窗口必须在 1~1000 点范围内");
        if (!AnalogHardwareBaseline.SupportedSignalTypes.Contains(settings.ForceSignalType) ||
            !AnalogHardwareBaseline.SupportedSignalTypes.Contains(settings.CurrentSignalType) ||
            !AnalogHardwareBaseline.SupportedSignalTypes.Contains(settings.VoltageSignalType) ||
            !AnalogHardwareBaseline.SupportedSignalTypes.Contains(settings.DisplacementSignalType))
        {
            errors.Add("拉力、电流、电压和位移信号类型必须与 P-881B 的实际硬件焊接/直通模式一致");
        }

        var enabledStations = settings.Stations.Where(x => x.Enabled).ToArray();
        if (enabledStations.Length == 0) errors.Add("至少需要启用一个工位");
        if (enabledStations.Length > StationTopology.MaximumStationCount)
            errors.Add($"最多只能启用 {StationTopology.MaximumStationCount} 个工位（{StationTopology.CapacityDescription}）");
        if (enabledStations.Any(x => !StationTopology.IsSupported(x.StationId)))
            errors.Add($"工位编号只能在 1~{StationTopology.MaximumStationCount} 范围内");
        if (enabledStations.Any(x => x.CanChannel is < 0 or >= CanHardwareBaseline.ChannelCount))
            errors.Add($"{CanHardwareBaseline.Model} 只有 CAN0、CAN1 两个通道，工位通道必须为 0 或 1");
        var nodeKeys = enabledStations.Select(x => $"{x.CanChannel}:{x.CanNodeId}").ToArray();
        if (nodeKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != nodeKeys.Length)
            errors.Add("同一 CAN 通道内的工位节点 ID 不能重复");
        if (enabledStations.Any(x => x.CanNodeId is < 1 or > 127)) errors.Add("工位 CAN 节点 ID 必须在 1~127 范围内");

        var stationAnalogChannels = enabledStations
            .SelectMany(x => new[] { x.ForceChannel, x.CurrentChannel, x.VoltageChannel, x.DisplacementChannel })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        if (stationAnalogChannels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != stationAnalogChannels.Length)
            errors.Add("已启用工位的拉力、电流、电压和位移通道不能重复");

        var parsedAnalogChannels = new List<int>();
        foreach (var channelText in stationAnalogChannels)
        {
            if (!AnalogHardwareBaseline.TryParseAnalogChannel(channelText, out var channel))
                errors.Add($"模拟量通道 {channelText} 无效，必须为 AI0~AI31");
            else
                parsedAnalogChannels.Add(channel);
        }
        if (settings.AnalogInputMode == AnalogHardwareBaseline.DifferentialMode)
        {
            if (parsedAnalogChannels.Any(x => x % 2 != 0 || x + 1 >= AnalogHardwareBaseline.SingleEndedChannelCount))
                errors.Add("差分模式必须使用偶数起始通道，例如 AI0 表示 AI0+/AI1- 通道对");
            var physicalChannels = parsedAnalogChannels.SelectMany(x => new[] { x, x + 1 }).ToArray();
            if (physicalChannels.Distinct().Count() != physicalChannels.Length)
                errors.Add("差分模拟量通道对不能重叠");
        }
        if (parsedAnalogChannels.Count > 0)
        {
            var first = parsedAnalogChannels.Min();
            var last = parsedAnalogChannels.Max() + (settings.AnalogInputMode == AnalogHardwareBaseline.DifferentialMode ? 1 : 0);
            var scannedPhysicalChannelCount = last - first + 1;
            var totalConversionRate = settings.AnalogScanRate * scannedPhysicalChannelCount;
            if (totalConversionRate > AnalogHardwareBaseline.MaximumSampleRateHz)
                errors.Add($"PCIE-1604 总转换率 {totalConversionRate} Hz 超过 {AnalogHardwareBaseline.MaximumSampleRateHz} Hz 上限");
        }

        foreach (var station in enabledStations)
        {
            var gains = new[]
            {
                station.ForceCalibrationGain,
                station.CurrentCalibrationGain,
                station.VoltageCalibrationGain,
                station.DisplacementCalibrationGain
            };
            var offsets = new[]
            {
                station.ForceCalibrationOffset,
                station.CurrentCalibrationOffset,
                station.VoltageCalibrationOffset,
                station.DisplacementCalibrationOffset
            };
            if (gains.Any(x => !double.IsFinite(x) || x <= 0)) errors.Add($"{station.Name} 标定增益必须为大于0的有限数值");
            if (offsets.Any(x => !double.IsFinite(x))) errors.Add($"{station.Name} 标定偏置必须为有限数值");
        }

        var safetyInputs = enabledStations.SelectMany(x => new[] { x.PositiveLimitInput, x.NegativeLimitInput })
            .Append(settings.SafetyDoorInput)
            .Where(x => !string.Equals(x, "禁用", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (safetyInputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != safetyInputs.Length)
            errors.Add("正限位、反限位和安全门不能复用同一个数字量输入");
        foreach (var input in safetyInputs)
        {
            if (!AnalogHardwareBaseline.TryParseDigitalInput(input, out _))
                errors.Add($"数字量输入 {input} 无效，必须为 DI0~DI15 或禁用");
        }

        return errors.Count == 0
            ? OperationResult.Ok("参数校验通过。")
            : OperationResult.Fail("参数校验失败：" + string.Join("；", errors) + "。");
    }
}
