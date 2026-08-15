namespace DurabilityTestingSystem.Models;

public enum RuntimeMode
{
    Demo,
    Production
}

public enum DeviceConnectionState
{
    NotConfigured,
    Disconnected,
    Connecting,
    Online,
    Warning,
    Fault
}

public enum TestRunState
{
    Ready,
    Running,
    Paused,
    Completed,
    Alarm
}

/// <summary>
/// 设备工位容量的唯一定义：客户本期配置 2 个标准工位，并预留 1 个扩展工位。
/// UI、校验、运行引擎和硬件适配器都必须引用这里，禁止在各层写死工位数量。
/// </summary>
public static class StationTopology
{
    public const int StandardStationCount = 2;
    public const int ExpansionStationCount = 1;
    public const int MaximumStationCount = StandardStationCount + ExpansionStationCount;
    public const string CapacityDescription = "2 个标准工位 + 1 个扩展工位";

    public static bool IsSupported(int stationId) => stationId is >= 1 and <= MaximumStationCount;
    public static bool IsExpansion(int stationId) => stationId > StandardStationCount && IsSupported(stationId);
    public static string DefaultName(int stationId) => IsExpansion(stationId) ? $"扩展工位 {stationId}" : $"工位 {stationId}";
}

/// <summary>
/// 项目已经冻结的 CAN 接口卡基线。所有页面、校验器和现场适配器都引用这里，
/// 避免再次出现 USB/PCIe 型号混用或厂家设备类型号写错的问题。
/// </summary>
public static class CanHardwareBaseline
{
    public const string Manufacturer = "周立功（ZLG）";
    public const string Model = "USBCANFD-200U";
    public const string PcInterface = "USB 2.0";
    public const string DisplayName = "周立功 USBCANFD-200U（USB）";
    public const string NativeLibraryName = "zlgcan.dll";
    public const uint ZlgDeviceType = 41;
    public const int ChannelCount = 2;

    public static readonly int[] SupportedArbitrationBaudRates =
        [50_000, 100_000, 125_000, 250_000, 500_000, 800_000, 1_000_000];

    public static readonly int[] SupportedDataBaudRates =
        [100_000, 125_000, 250_000, 500_000, 800_000, 1_000_000, 2_000_000, 4_000_000, 5_000_000];
}

/// <summary>
/// 已冻结的模拟量采集硬件。P-881B 与 PCIE-1604 的配套关系仍需厂家书面确认：
/// 两份手册的推荐配套型号及 DB37 第 19 脚定义并不完全一致，因此正式模式必须保留确认门槛。
/// </summary>
public static class AnalogHardwareBaseline
{
    public const string Manufacturer = "北京新超仁达科技有限公司";
    public const string Model = "PCIE-1604";
    public const string PcInterface = "PCI Express x1";
    public const string DisplayName = "新超仁达 PCIE-1604";
    public const string TerminalModel = "P-881B";
    public const string TerminalDisplayName = "新超仁达 P-881B";
    public const string NativeLibraryName = "pcieAPI.dll";
    public const string DriverCompanionLibraryName = "CH365.dll";
    public const int SingleEndedChannelCount = 32;
    public const int DifferentialChannelCount = 16;
    public const int DigitalInputCount = 16;
    public const int DigitalOutputCount = 16;
    public const int ResolutionBits = 16;
    public const int MaximumSampleRateHz = 250_000;
    public const int MaximumSoftwareScanRateHz = 1000;
    public const string SingleEndedMode = "单端 32 路";
    public const string DifferentialMode = "差分 16 路";

    public static readonly string[] SupportedInputModes = [SingleEndedMode, DifferentialMode];

    public static readonly string[] SupportedSignalTypes =
    [
        "4~20 mA（P-881B 转 1~5 V）",
        "0~10 V（P-881B 直通）",
        "±10 V（P-881B 直通）",
        "0~5 V（P-881B 直通）"
    ];

    public static bool TryParseAnalogChannel(string? text, out int channel)
    {
        channel = -1;
        return !string.IsNullOrWhiteSpace(text) &&
               text.StartsWith("AI", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(text.AsSpan(2), out channel) &&
               channel is >= 0 and < SingleEndedChannelCount;
    }

    public static bool TryParseDigitalInput(string? text, out int channel)
    {
        channel = -1;
        return !string.IsNullOrWhiteSpace(text) &&
               text.StartsWith("DI", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(text.AsSpan(2), out channel) &&
               channel is >= 0 and < DigitalInputCount;
    }
}

public sealed class LiveSample
{
    public int StationId { get; init; } = 1;
    public string StationName { get; init; } = "工位 1";
    public DateTime Time { get; init; } = DateTime.Now;
    public double Force { get; init; }
    public double Current { get; init; }
    public double Voltage { get; init; }
    public double Displacement { get; init; }
    public long AcquisitionSequence { get; init; }
    public ushort DigitalInputs { get; init; }
    public double? ForceInputVoltage { get; init; }
    public double? CurrentInputVoltage { get; init; }
    public double? VoltageInputVoltage { get; init; }
    public double? DisplacementInputVoltage { get; init; }
    public string DataQuality { get; init; } = "未知";
    public string ControllerFrame { get; init; } = string.Empty;
    public int Cycle { get; init; }
    public string Phase { get; init; } = "待机";
}

public sealed class TestSettings
{
    public string ProjectName { get; set; } = "安全带卷收器耐久试验";
    public string PlanCode { get; set; } = "SB-DUR-001";
    public double TargetForce { get; set; } = 450;
    public double ForceUpperLimit { get; set; } = 520;
    public double ForceLowerLimit { get; set; } = 380;
    public int TargetCycles { get; set; } = 50000;
    public double PullDuration { get; set; } = 2.0;
    public double HoldDuration { get; set; } = 1.0;
    public double ReturnDuration { get; set; } = 2.0;
    public double ActionInterval { get; set; } = 0.5;
    public int SampleInterval { get; set; } = 100;
    public int CanBaudRate { get; set; } = 500000;
    public string CanDevice { get; set; } = CanHardwareBaseline.DisplayName;
    public int CanDeviceIndex { get; set; }
    public string CanBusMode { get; set; } = "CAN 2.0";
    public int CanDataBaudRate { get; set; } = 2_000_000;
    public string CanFdStandard { get; set; } = "ISO";
    public bool CanTerminationEnabled { get; set; }
    public int CanTransmitTimeout { get; set; } = 100;
    public string ProtocolMode { get; set; } = "DBC 文件";
    public string DbcFilePath { get; set; } = string.Empty;
    public string AnalogDevice { get; set; } = AnalogHardwareBaseline.DisplayName;
    public string AnalogTerminalBoard { get; set; } = AnalogHardwareBaseline.TerminalDisplayName;
    public int AnalogBoardId { get; set; }
    public string AnalogInputMode { get; set; } = AnalogHardwareBaseline.DifferentialMode;
    /// <summary>每通道完整扫描频率；适配器会乘以物理通道数得到板卡总转换率。</summary>
    public int AnalogScanRate { get; set; } = 100;
    public int AnalogReadTimeout { get; set; } = 500;
    public double SensorRange { get; set; } = 1000;
    public string ForceSignalType { get; set; } = "4~20 mA（P-881B 转 1~5 V）";
    public string CurrentSignalType { get; set; } = "4~20 mA（P-881B 转 1~5 V）";
    public string VoltageSignalType { get; set; } = "4~20 mA（P-881B 转 1~5 V）";
    public int FilterWindow { get; set; } = 5;
    public double MotorSpeed { get; set; } = 120;
    public double MotorAcceleration { get; set; } = 300;
    public double MaxForceProtection { get; set; } = 650;
    public double MaxCurrentProtection { get; set; } = 45;
    public double MaxVoltageProtection { get; set; } = 16;
    public double MaxDisplacementProtection { get; set; } = 85;
    public double ResetDisplacementTolerance { get; set; } = 2;
    public string ControlMode { get; set; } = "位置模式";
    public int CommunicationTimeout { get; set; } = 1000;
    public bool AutoReconnect { get; set; } = true;
    public string DisplacementSignalType { get; set; } = "0~10 V（P-881B 直通）";
    public double DisplacementSensorRange { get; set; } = 100;
    public double CurrentSensorRange { get; set; } = 60;
    public double VoltageSensorRange { get; set; } = 30;
    public string SafetyDoorInput { get; set; } = "DI10";
    public int OverLimitDelay { get; set; } = 200;
    public string OverLimitAction { get; set; } = "立即停止并报警";
    public int DataRecordInterval { get; set; } = 500;
    public List<StationConfiguration> Stations { get; set; } = StationConfiguration.CreateDefaults();

    public void EnsureStationConfigurations()
    {
        if (string.IsNullOrWhiteSpace(CanDevice) ||
            CanDevice.Contains("USCCANFD", StringComparison.OrdinalIgnoreCase) ||
            CanDevice.Contains("PCIe-CAN", StringComparison.OrdinalIgnoreCase))
        {
            CanDevice = CanHardwareBaseline.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(AnalogDevice) ||
            AnalogDevice.Contains("Modbus", StringComparison.OrdinalIgnoreCase))
        {
            AnalogDevice = AnalogHardwareBaseline.DisplayName;
        }
        if (string.IsNullOrWhiteSpace(AnalogTerminalBoard))
            AnalogTerminalBoard = AnalogHardwareBaseline.TerminalDisplayName;
        if (!AnalogHardwareBaseline.SupportedInputModes.Contains(AnalogInputMode))
            AnalogInputMode = AnalogHardwareBaseline.DifferentialMode;
        ForceSignalType = NormalizeSignalType(ForceSignalType, "4~20 mA（P-881B 转 1~5 V）");
        CurrentSignalType = NormalizeSignalType(CurrentSignalType, "4~20 mA（P-881B 转 1~5 V）");
        VoltageSignalType = NormalizeSignalType(VoltageSignalType, "4~20 mA（P-881B 转 1~5 V）");
        DisplacementSignalType = NormalizeSignalType(DisplacementSignalType, "0~10 V（P-881B 直通）");
        // 当前现场协议尚未提供可验证的“减速后停止”动作。安全相关选项
        // 不能只停留在界面文字，因此本基线统一为故障关闭策略。
        OverLimitAction = "立即停止并报警";

        Stations ??= [];
        for (var stationId = 1; stationId <= StationTopology.MaximumStationCount; stationId++)
        {
            if (Stations.All(x => x.StationId != stationId))
                Stations.Add(StationConfiguration.CreateDefault(stationId));
        }
        Stations = Stations
            .Where(x => StationTopology.IsSupported(x.StationId))
            .GroupBy(x => x.StationId)
            .Select(x => x.First())
            .OrderBy(x => x.StationId)
            .ToList();
        foreach (var station in Stations)
        {
            if (string.IsNullOrWhiteSpace(station.Name) ||
                (StationTopology.IsExpansion(station.StationId) && station.Name == $"工位 {station.StationId}"))
            {
                station.Name = StationTopology.DefaultName(station.StationId);
            }
        }
    }

    private static string NormalizeSignalType(string? signalType, string fallback)
    {
        if (string.IsNullOrWhiteSpace(signalType)) return fallback;
        if (AnalogHardwareBaseline.SupportedSignalTypes.Contains(signalType)) return signalType;
        if (signalType.Contains("4~20", StringComparison.OrdinalIgnoreCase)) return AnalogHardwareBaseline.SupportedSignalTypes[0];
        if (signalType.Contains("±10", StringComparison.OrdinalIgnoreCase)) return AnalogHardwareBaseline.SupportedSignalTypes[2];
        if (signalType.Contains("0~10", StringComparison.OrdinalIgnoreCase)) return AnalogHardwareBaseline.SupportedSignalTypes[1];
        if (signalType.Contains("0~5", StringComparison.OrdinalIgnoreCase)) return AnalogHardwareBaseline.SupportedSignalTypes[3];
        return fallback;
    }
}

public sealed class StationConfiguration
{
    public int StationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int CanChannel { get; set; }
    public int CanNodeId { get; set; }
    public string ForceChannel { get; set; } = string.Empty;
    public string CurrentChannel { get; set; } = string.Empty;
    public string VoltageChannel { get; set; } = string.Empty;
    public string DisplacementChannel { get; set; } = string.Empty;
    public string PositiveLimitInput { get; set; } = string.Empty;
    public string NegativeLimitInput { get; set; } = string.Empty;
    public string CalibrationRecordId { get; set; } = "待标定";
    public double ForceCalibrationGain { get; set; } = 1;
    public double ForceCalibrationOffset { get; set; }
    public double CurrentCalibrationGain { get; set; } = 1;
    public double CurrentCalibrationOffset { get; set; }
    public double VoltageCalibrationGain { get; set; } = 1;
    public double VoltageCalibrationOffset { get; set; }
    public double DisplacementCalibrationGain { get; set; } = 1;
    public double DisplacementCalibrationOffset { get; set; }

    public static StationConfiguration CreateDefault(int stationId)
    {
        // 正式基线采用差分采集：每个工程量使用一对相邻物理 AI，提升长线抗共模干扰能力。
        var analogOffset = (stationId - 1) * 8;
        var digitalOffset = (stationId - 1) * 2;
        return new StationConfiguration
        {
            StationId = stationId,
            Name = StationTopology.DefaultName(stationId),
            Enabled = stationId <= StationTopology.StandardStationCount,
            CanChannel = 0,
            CanNodeId = stationId,
            ForceChannel = $"AI{analogOffset}",
            CurrentChannel = $"AI{analogOffset + 2}",
            VoltageChannel = $"AI{analogOffset + 4}",
            DisplacementChannel = $"AI{analogOffset + 6}",
            PositiveLimitInput = $"DI{digitalOffset}",
            NegativeLimitInput = $"DI{digitalOffset + 1}"
        };
    }

    public static List<StationConfiguration> CreateDefaults() =>
        Enumerable.Range(1, StationTopology.MaximumStationCount).Select(CreateDefault).ToList();
}

public sealed class TestPlan
{
    public long Id { get; set; }
    public int Revision { get; set; } = 1;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Cycles { get; set; }
    public double TargetForce { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool Enabled { get; set; }
}

public sealed class TestPlanStep
{
    public long Id { get; set; }
    public long PlanId { get; set; }
    public int Sequence { get; set; }
    public string ActionType { get; set; } = "等待";
    public string TargetValue { get; set; } = "—";
    public double DurationSeconds { get; set; }
    public string CompletionCondition { get; set; } = "时间到";
}

public sealed class TestRecord
{
    public long Id { get; set; }
    public string TestNo { get; set; } = string.Empty;
    public string SpecimenNo { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PlanId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public int PlanRevision { get; set; } = 1;
    public string PlanSnapshotJson { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public int Cycles { get; set; }
    public double PeakForce { get; set; }
    public double PeakDisplacement { get; set; }
    public int StationId { get; set; } = 1;
    public string StationName { get; set; } = "工位 1";
    public string Result { get; set; } = "合格";
    public string FailureReason { get; set; } = string.Empty;
    public string Operator { get; set; } = "管理员";
}

public sealed class SystemLogEntry
{
    public DateTime Time { get; set; }
    public string Level { get; set; } = "信息";
    public string Source { get; set; } = "系统";
    public string Message { get; set; } = string.Empty;
}

public sealed class TestSampleRecord
{
    public string TestNo { get; set; } = string.Empty;
    public int StationId { get; set; } = 1;
    public string StationName { get; set; } = "工位 1";
    public DateTime Time { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public double Force { get; set; }
    public double Current { get; set; }
    public double Voltage { get; set; }
    public double Displacement { get; set; }
    public long AcquisitionSequence { get; set; }
    public ushort DigitalInputs { get; set; }
    public double? ForceInputVoltage { get; set; }
    public double? CurrentInputVoltage { get; set; }
    public double? VoltageInputVoltage { get; set; }
    public double? DisplacementInputVoltage { get; set; }
    public string DataQuality { get; set; } = "未知";
    public string ControllerFrame { get; set; } = string.Empty;
    public int Cycle { get; set; }
    public string Phase { get; set; } = string.Empty;
}

public sealed class StationRuntimeStatus
{
    public int StationId { get; init; }
    public string StationName { get; set; } = string.Empty;
    public TestRunState State { get; set; } = TestRunState.Ready;
    public int CurrentCycle { get; set; }
    public double PeakForce { get; set; }
    public double PeakDisplacement { get; set; }
    public string Phase { get; set; } = "待机";
    public string Message { get; set; } = "就绪";
    public LiveSample? LastSample { get; set; }
}

public sealed class DeviceStatus
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DeviceConnectionState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
}

public sealed class SystemHealthSnapshot
{
    public RuntimeMode Mode { get; init; }
    public IReadOnlyList<DeviceStatus> Devices { get; init; } = [];
    public bool CanStartTest { get; init; }
    public string Summary { get; init; } = string.Empty;

    public DeviceStatus? Find(string key) =>
        Devices.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
}

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message = "操作成功") => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

public sealed class SystemProfile
{
    public int SchemaVersion { get; set; } = 2;
    public RuntimeMode Mode { get; set; } = RuntimeMode.Demo;
    public string ProfileName { get; set; } = "内置演示配置";
    public bool AutoConnectOnStartup { get; set; }
    public string HardwareAdapterAssembly { get; set; } = string.Empty;
    public string HardwareAdapterType { get; set; } = string.Empty;
    public string Notes { get; set; } = "硬件型号确定后填写适配器程序集与类型。";
    public HardwareQualification Qualification { get; set; } = new();
}

/// <summary>
/// 由项目负责人在完成书面确认、实物联调和安全验收后维护的部署门槛。
/// 这些值位于 system-profile.json，不在普通参数页面提供勾选入口。
/// </summary>
public sealed class HardwareQualification
{
    public bool TerminalBoardCompatibilityApproved { get; set; }
    public bool Pcie1604SdkValidated { get; set; }
    public bool SafetySignalConditioningApproved { get; set; }
    public bool MotorProtocolValidated { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime? ApprovedAt { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;

    public IReadOnlyList<string> MissingItems()
    {
        var missing = new List<string>();
        if (!TerminalBoardCompatibilityApproved) missing.Add("PCIE-1604 与 P-881B 书面兼容确认");
        if (!Pcie1604SdkValidated) missing.Add("PCIE-1604 x64 SDK/驱动实机验证");
        if (!SafetySignalConditioningApproved) missing.Add("TTL 安全信号隔离调理与硬件安全回路验收");
        if (!MotorProtocolValidated) missing.Add("电机 DBC/字节协议及停机报文验证");
        if (TerminalBoardCompatibilityApproved && Pcie1604SdkValidated &&
            SafetySignalConditioningApproved && MotorProtocolValidated &&
            (string.IsNullOrWhiteSpace(ApprovedBy) || ApprovedAt is null || string.IsNullOrWhiteSpace(EvidenceReference)))
        {
            missing.Add("部署验收批准人、时间及证据编号");
        }
        return missing;
    }
}
