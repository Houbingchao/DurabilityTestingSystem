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

public sealed class LiveSample
{
    public DateTime Time { get; init; } = DateTime.Now;
    public double Force { get; init; }
    public double Current { get; init; }
    public double Voltage { get; init; }
    public double Position { get; init; }
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
    public int SampleInterval { get; set; } = 100;
    public int CanBaudRate { get; set; } = 500000;
    public int CanNodeId { get; set; } = 1;
    public string CanDevice { get; set; } = "USBCAN-2E-U / 通道 0";
    public string AnalogModuleIp { get; set; } = "192.168.10.60";
    public int AnalogModulePort { get; set; } = 502;
    public double SensorRange { get; set; } = 1000;
    public string ForceSignalType { get; set; } = "4~20 mA";
    public int FilterWindow { get; set; } = 5;
    public double MotorSpeed { get; set; } = 120;
    public double MotorAcceleration { get; set; } = 300;
    public double MaxForceProtection { get; set; } = 650;
    public double MaxCurrentProtection { get; set; } = 8;
    public double MaxVoltageProtection { get; set; } = 60;
    public string ControlMode { get; set; } = "位置模式";
    public int CommunicationTimeout { get; set; } = 1000;
    public bool AutoReconnect { get; set; } = true;
    public string ForceChannel { get; set; } = "AI0";
    public string CurrentChannel { get; set; } = "AI1";
    public string VoltageChannel { get; set; } = "AI2";
    public double CurrentSensorRange { get; set; } = 20;
    public double VoltageSensorRange { get; set; } = 100;
    public string PositiveLimitInput { get; set; } = "DI0";
    public string NegativeLimitInput { get; set; } = "DI1";
    public string SafetyDoorInput { get; set; } = "DI2";
    public int OverLimitDelay { get; set; } = 200;
    public string OverLimitAction { get; set; } = "立即停止并报警";
    public int DataRecordInterval { get; set; } = 500;
}

public sealed class TestPlan
{
    public long Id { get; set; }
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
    public DateTime StartedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public int Cycles { get; set; }
    public double PeakForce { get; set; }
    public string Result { get; set; } = "合格";
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
    public DateTime Time { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public double Force { get; set; }
    public double Current { get; set; }
    public double Voltage { get; set; }
    public double Position { get; set; }
    public int Cycle { get; set; }
    public string Phase { get; set; } = string.Empty;
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
    public RuntimeMode Mode { get; set; } = RuntimeMode.Demo;
    public string ProfileName { get; set; } = "内置演示配置";
    public bool AutoConnectOnStartup { get; set; }
    public string HardwareAdapterAssembly { get; set; } = string.Empty;
    public string HardwareAdapterType { get; set; } = string.Empty;
    public string Notes { get; set; } = "硬件型号确定后填写适配器程序集与类型。";
}
