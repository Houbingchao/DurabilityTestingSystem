using System.Globalization;
using System.Text.Json;
using DurabilityTestingSystem.Models;
using Microsoft.Data.Sqlite;

namespace DurabilityTestingSystem.Data;

public sealed class AppDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string DatabasePath { get; }

    public AppDatabase(string databaseFileName = "durability.db")
    {
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeatbeltDurabilitySystem");
        Directory.CreateDirectory(dataFolder);
        DatabasePath = Path.Combine(dataFolder, databaseFileName);
        _connection = new SqliteConnection($"Data Source={DatabasePath}");
    }

    public void Initialize(bool seedDemoData)
    {
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS plans (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                revision INTEGER NOT NULL DEFAULT 1,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                cycles INTEGER NOT NULL,
                target_force REAL NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS test_records (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                test_no TEXT NOT NULL,
                specimen_no TEXT NOT NULL,
                plan_name TEXT NOT NULL,
                plan_id INTEGER NOT NULL DEFAULT 0,
                plan_code TEXT NOT NULL DEFAULT '',
                plan_revision INTEGER NOT NULL DEFAULT 1,
                plan_snapshot_json TEXT NOT NULL DEFAULT '',
                started_at TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                cycles INTEGER NOT NULL,
                peak_force REAL NOT NULL,
                peak_displacement REAL NOT NULL DEFAULT 0,
                station_id INTEGER NOT NULL DEFAULT 1,
                station_name TEXT NOT NULL DEFAULT '工位 1',
                result TEXT NOT NULL,
                failure_reason TEXT NOT NULL DEFAULT '',
                operator_name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS system_logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                time TEXT NOT NULL,
                level TEXT NOT NULL,
                source TEXT NOT NULL,
                message TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS plan_steps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plan_id INTEGER NOT NULL,
                sequence_no INTEGER NOT NULL,
                action_type TEXT NOT NULL,
                target_value TEXT NOT NULL,
                duration_seconds REAL NOT NULL,
                completion_condition TEXT NOT NULL,
                FOREIGN KEY(plan_id) REFERENCES plans(id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_plan_steps_sequence
                ON plan_steps(plan_id, sequence_no);
            CREATE TABLE IF NOT EXISTS test_samples (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                test_no TEXT NOT NULL,
                sample_time TEXT NOT NULL,
                elapsed_ms INTEGER NOT NULL,
                force REAL NOT NULL,
                current REAL NOT NULL,
                voltage REAL NOT NULL,
                position REAL NOT NULL,
                displacement REAL NOT NULL DEFAULT 0,
                station_id INTEGER NOT NULL DEFAULT 1,
                station_name TEXT NOT NULL DEFAULT '工位 1',
                controller_frame TEXT NOT NULL DEFAULT '',
                acquisition_sequence INTEGER NOT NULL DEFAULT 0,
                digital_inputs INTEGER NOT NULL DEFAULT 0,
                force_input_voltage REAL,
                current_input_voltage REAL,
                voltage_input_voltage REAL,
                displacement_input_voltage REAL,
                data_quality TEXT NOT NULL DEFAULT '未知',
                cycle INTEGER NOT NULL,
                phase TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_test_samples_test_time
                ON test_samples(test_no, sample_time);
            PRAGMA user_version=4;
            """;
        command.ExecuteNonQuery();
        EnsureColumn("plans", "revision", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn("test_records", "peak_displacement", "REAL NOT NULL DEFAULT 0");
        EnsureColumn("test_records", "station_id", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn("test_records", "station_name", "TEXT NOT NULL DEFAULT '工位 1'");
        EnsureColumn("test_records", "plan_id", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("test_records", "plan_code", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("test_records", "plan_revision", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn("test_records", "plan_snapshot_json", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("test_records", "failure_reason", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("test_samples", "displacement", "REAL NOT NULL DEFAULT 0");
        EnsureColumn("test_samples", "station_id", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn("test_samples", "station_name", "TEXT NOT NULL DEFAULT '工位 1'");
        EnsureColumn("test_samples", "controller_frame", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("test_samples", "acquisition_sequence", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("test_samples", "digital_inputs", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("test_samples", "force_input_voltage", "REAL");
        EnsureColumn("test_samples", "current_input_voltage", "REAL");
        EnsureColumn("test_samples", "voltage_input_voltage", "REAL");
        EnsureColumn("test_samples", "displacement_input_voltage", "REAL");
        EnsureColumn("test_samples", "data_quality", "TEXT NOT NULL DEFAULT '未知'");
        if (seedDemoData) SeedDemoData();
        else AddLog("信息", "系统", "正式运行模式数据库初始化完成，未写入演示记录");
        EnsureLegacyPlansHaveFixedTemplateSteps();
    }

    public TestSettings LoadSettings()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT json FROM settings WHERE key = 'main' LIMIT 1";
        var json = command.ExecuteScalar() as string;
        var settings = string.IsNullOrWhiteSpace(json)
            ? new TestSettings()
            : JsonSerializer.Deserialize<TestSettings>(json) ?? new TestSettings();
        var hadLegacyFiveStationLayout = settings.Stations?.Any(x => x.StationId > StationTopology.MaximumStationCount) == true;
        var hadLegacyCanSelection = !string.Equals(settings.CanDevice, CanHardwareBaseline.DisplayName, StringComparison.Ordinal);
        var hadLegacyAnalogSelection = !string.IsNullOrWhiteSpace(json) &&
            (!json.Contains("\"AnalogDevice\"", StringComparison.OrdinalIgnoreCase) ||
             json.Contains("\"AnalogModuleIp\"", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(json) && !json.Contains("\"Stations\"", StringComparison.OrdinalIgnoreCase))
        {
            // 从早期单工位 Demo 平滑迁移到当前确定的“两标准工位 + 一扩展工位”结构。
            settings.CanDevice = CanHardwareBaseline.DisplayName;
            settings.MaxForceProtection = settings.MaxForceProtection == 650 ? 620 : settings.MaxForceProtection;
            settings.CurrentSensorRange = settings.CurrentSensorRange == 20 ? 60 : settings.CurrentSensorRange;
            settings.MaxCurrentProtection = settings.MaxCurrentProtection == 8 ? 45 : settings.MaxCurrentProtection;
            settings.VoltageSensorRange = settings.VoltageSensorRange == 100 ? 30 : settings.VoltageSensorRange;
            settings.MaxVoltageProtection = settings.MaxVoltageProtection == 60 ? 16 : settings.MaxVoltageProtection;
            settings.SafetyDoorInput = settings.SafetyDoorInput == "DI2" ? "DI10" : settings.SafetyDoorInput;
        }
        settings.EnsureStationConfigurations();
        if (hadLegacyAnalogSelection)
        {
            // 旧版按Modbus单端AI0~AI11顺排；冻结PCIE-1604后，正式基线改为12组差分输入。
            // 只迁移当前配置，原始JSON会先保存在settings表的migration_backup键中。
            settings.AnalogDevice = AnalogHardwareBaseline.DisplayName;
            settings.AnalogTerminalBoard = AnalogHardwareBaseline.TerminalDisplayName;
            settings.AnalogInputMode = AnalogHardwareBaseline.DifferentialMode;
            settings.AnalogBoardId = 0;
            settings.AnalogScanRate = 100;
            settings.AnalogReadTimeout = 500;
            foreach (var station in settings.Stations ?? [])
            {
                var baseline = StationConfiguration.CreateDefault(station.StationId);
                station.ForceChannel = baseline.ForceChannel;
                station.CurrentChannel = baseline.CurrentChannel;
                station.VoltageChannel = baseline.VoltageChannel;
                station.DisplacementChannel = baseline.DisplacementChannel;
                station.CalibrationRecordId = "迁移后待标定";
            }
        }
        if (hadLegacyFiveStationLayout || hadLegacyCanSelection || hadLegacyAnalogSelection)
        {
            if (!string.IsNullOrWhiteSpace(json)) SaveMigrationBackup(json);
            SaveSettings(settings);
            if (hadLegacyFiveStationLayout)
            {
                AddLog("信息", "配置迁移",
                    $"已将旧版五工位配置迁移为 {StationTopology.CapacityDescription}；工位 4、5 仅从当前配置移除，历史试验记录未删除");
            }
            if (hadLegacyCanSelection)
            {
                AddLog("信息", "配置迁移",
                    $"CAN 硬件基线已冻结为 {CanHardwareBaseline.DisplayName}，旧 PCIe/临时型号选择已自动替换");
            }
            if (hadLegacyAnalogSelection)
            {
                AddLog("警告", "配置迁移",
                    $"旧Modbus模拟量配置已迁移为 {AnalogHardwareBaseline.DisplayName} + {AnalogHardwareBaseline.TerminalDisplayName} 差分通道基线；所有通道仍须按实物接线重新标定");
            }
        }
        return settings;
    }

    public void SaveSettings(TestSettings settings)
    {
        settings.EnsureStationConfigurations();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, json, updated_at)
            VALUES('main', $json, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings, _jsonOptions));
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O"));
        command.ExecuteNonQuery();
        AddLog("信息", "参数设置", "试验与设备参数已保存");
    }

    public IReadOnlyList<TestPlan> GetPlans()
    {
        var result = new List<TestPlan>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, revision, code, name, cycles, target_force, updated_at, enabled FROM plans ORDER BY enabled DESC, id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TestPlan
            {
                Id = reader.GetInt64(0),
                Revision = reader.GetInt32(1),
                Code = reader.GetString(2),
                Name = reader.GetString(3),
                Cycles = reader.GetInt32(4),
                TargetForce = reader.GetDouble(5),
                UpdatedAt = DateTime.Parse(reader.GetString(6)),
                Enabled = reader.GetInt32(7) == 1
            });
        }
        return result;
    }

    public long UpsertPlan(TestPlan plan)
    {
        using (var duplicate = _connection.CreateCommand())
        {
            duplicate.CommandText = "SELECT COUNT(*) FROM plans WHERE code=$code COLLATE NOCASE AND id<>$id";
            duplicate.Parameters.AddWithValue("$code", plan.Code.Trim());
            duplicate.Parameters.AddWithValue("$id", plan.Id);
            if (Convert.ToInt32(duplicate.ExecuteScalar()) > 0)
                throw new InvalidOperationException($"方案编号“{plan.Code.Trim()}”已存在，请使用唯一编号。");
        }

        using var command = _connection.CreateCommand();
        if (plan.Id == 0)
        {
            command.CommandText = """
                INSERT INTO plans(revision, code, name, cycles, target_force, enabled, updated_at)
                VALUES(1, $code, $name, $cycles, $force, $enabled, $updatedAt)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE plans SET code=$code, name=$name, cycles=$cycles,
                target_force=$force, enabled=$enabled, updated_at=$updatedAt,
                revision=revision+1 WHERE id=$id
                """;
            command.Parameters.AddWithValue("$id", plan.Id);
        }
        command.Parameters.AddWithValue("$code", plan.Code);
        command.Parameters.AddWithValue("$name", plan.Name);
        command.Parameters.AddWithValue("$cycles", plan.Cycles);
        command.Parameters.AddWithValue("$force", plan.TargetForce);
        command.Parameters.AddWithValue("$enabled", plan.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O"));
        command.ExecuteNonQuery();
        var planId = plan.Id;
        if (planId == 0)
        {
            using var idCommand = _connection.CreateCommand();
            idCommand.CommandText = "SELECT last_insert_rowid()";
            planId = Convert.ToInt64(idCommand.ExecuteScalar());
        }
        AddLog("信息", "试验方案", $"方案“{plan.Name}”已保存");
        return planId;
    }

    public IReadOnlyList<TestPlanStep> GetPlanSteps(long planId)
    {
        var result = new List<TestPlanStep>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, plan_id, sequence_no, action_type, target_value,
                   duration_seconds, completion_condition
            FROM plan_steps WHERE plan_id=$planId ORDER BY sequence_no
            """;
        command.Parameters.AddWithValue("$planId", planId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TestPlanStep
            {
                Id = reader.GetInt64(0),
                PlanId = reader.GetInt64(1),
                Sequence = reader.GetInt32(2),
                ActionType = reader.GetString(3),
                TargetValue = reader.GetString(4),
                DurationSeconds = reader.GetDouble(5),
                CompletionCondition = reader.GetString(6)
            });
        }
        return result;
    }

    public void SavePlanSteps(long planId, IReadOnlyList<TestPlanStep> steps)
    {
        using var transaction = _connection.BeginTransaction();
        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM plan_steps WHERE plan_id=$planId";
            delete.Parameters.AddWithValue("$planId", planId);
            delete.ExecuteNonQuery();
        }

        foreach (var step in steps.OrderBy(x => x.Sequence))
        {
            using var insert = _connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO plan_steps(plan_id, sequence_no, action_type, target_value,
                    duration_seconds, completion_condition)
                VALUES($planId, $sequence, $action, $target, $duration, $condition)
                """;
            insert.Parameters.AddWithValue("$planId", planId);
            insert.Parameters.AddWithValue("$sequence", step.Sequence);
            insert.Parameters.AddWithValue("$action", step.ActionType);
            insert.Parameters.AddWithValue("$target", step.TargetValue);
            insert.Parameters.AddWithValue("$duration", step.DurationSeconds);
            insert.Parameters.AddWithValue("$condition", step.CompletionCondition);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
        AddLog("信息", "试验方案", $"方案步骤已保存，共 {steps.Count} 步");
    }

    public IReadOnlyList<TestRecord> GetTestRecords(
        string? keyword = null,
        string? resultFilter = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var result = new List<TestRecord>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, test_no, specimen_no, plan_name, started_at, duration_seconds,
                   cycles, peak_force, result, operator_name,
                   peak_displacement, station_id, station_name,
                   plan_id, plan_code, plan_revision, plan_snapshot_json, failure_reason
            FROM test_records
            WHERE ($keyword = '' OR test_no LIKE $likeKeyword OR specimen_no LIKE $likeKeyword OR station_name LIKE $likeKeyword)
              AND ($result = '' OR result = $result)
              AND ($start = '' OR started_at >= $start)
              AND ($end = '' OR started_at < $end)
            ORDER BY started_at DESC LIMIT 500
            """;
        var actualKeyword = keyword?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$keyword", actualKeyword);
        command.Parameters.AddWithValue("$likeKeyword", $"%{actualKeyword}%");
        command.Parameters.AddWithValue("$result", resultFilter is "全部" or null ? string.Empty : resultFilter);
        command.Parameters.AddWithValue("$start", startDate?.Date.ToString("O") ?? string.Empty);
        command.Parameters.AddWithValue("$end", endDate?.Date.AddDays(1).ToString("O") ?? string.Empty);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TestRecord
            {
                Id = reader.GetInt64(0),
                TestNo = reader.GetString(1),
                SpecimenNo = reader.GetString(2),
                PlanName = reader.GetString(3),
                StartedAt = DateTime.Parse(reader.GetString(4)),
                Duration = TimeSpan.FromSeconds(reader.GetInt64(5)),
                Cycles = reader.GetInt32(6),
                PeakForce = reader.GetDouble(7),
                Result = reader.GetString(8),
                Operator = reader.GetString(9),
                PeakDisplacement = reader.GetDouble(10),
                StationId = reader.GetInt32(11),
                StationName = reader.GetString(12),
                PlanId = reader.GetInt64(13),
                PlanCode = reader.GetString(14),
                PlanRevision = reader.GetInt32(15),
                PlanSnapshotJson = reader.GetString(16),
                FailureReason = reader.GetString(17)
            });
        }
        return result;
    }

    public void AddTestRecord(TestRecord record)
    {
        AddTestRecords([record]);
    }

    public void AddTestRecords(IReadOnlyCollection<TestRecord> records)
    {
        if (records.Count == 0) return;
        using var transaction = _connection.BeginTransaction();
        foreach (var record in records)
            InsertTestRecord(record, transaction);
        transaction.Commit();
    }

    private void InsertTestRecord(TestRecord record, SqliteTransaction transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO test_records(test_no, specimen_no, plan_name, plan_id, plan_code,
              plan_revision, plan_snapshot_json, started_at,
              duration_seconds, cycles, peak_force, peak_displacement, station_id, station_name, result, failure_reason, operator_name)
            VALUES($testNo, $specimenNo, $planName, $planId, $planCode,
              $planRevision, $planSnapshotJson, $startedAt, $duration,
              $cycles, $peakForce, $peakDisplacement, $stationId, $stationName, $result, $failureReason, $operator)
            """;
        command.Parameters.AddWithValue("$testNo", record.TestNo);
        command.Parameters.AddWithValue("$specimenNo", record.SpecimenNo);
        command.Parameters.AddWithValue("$planName", record.PlanName);
        command.Parameters.AddWithValue("$planId", record.PlanId);
        command.Parameters.AddWithValue("$planCode", record.PlanCode);
        command.Parameters.AddWithValue("$planRevision", record.PlanRevision);
        command.Parameters.AddWithValue("$planSnapshotJson", record.PlanSnapshotJson);
        command.Parameters.AddWithValue("$startedAt", record.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$duration", (long)record.Duration.TotalSeconds);
        command.Parameters.AddWithValue("$cycles", record.Cycles);
        command.Parameters.AddWithValue("$peakForce", record.PeakForce);
        command.Parameters.AddWithValue("$peakDisplacement", record.PeakDisplacement);
        command.Parameters.AddWithValue("$stationId", record.StationId);
        command.Parameters.AddWithValue("$stationName", record.StationName);
        command.Parameters.AddWithValue("$result", record.Result);
        command.Parameters.AddWithValue("$failureReason", record.FailureReason);
        command.Parameters.AddWithValue("$operator", record.Operator);
        command.ExecuteNonQuery();
    }

    public void AddTestSamples(IReadOnlyList<TestSampleRecord> samples)
    {
        if (samples.Count == 0) return;
        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO test_samples(test_no, sample_time, elapsed_ms, force, current,
                voltage, position, displacement, station_id, station_name, controller_frame,
                acquisition_sequence, digital_inputs, force_input_voltage, current_input_voltage,
                voltage_input_voltage, displacement_input_voltage, data_quality, cycle, phase)
            VALUES($testNo, $time, $elapsed, $force, $current, $voltage, $displacement,
                $displacement, $stationId, $stationName, $controllerFrame,
                $acquisitionSequence, $digitalInputs, $forceInputVoltage, $currentInputVoltage,
                $voltageInputVoltage, $displacementInputVoltage, $dataQuality, $cycle, $phase)
            """;
        var testNo = command.Parameters.Add("$testNo", SqliteType.Text);
        var time = command.Parameters.Add("$time", SqliteType.Text);
        var elapsed = command.Parameters.Add("$elapsed", SqliteType.Integer);
        var force = command.Parameters.Add("$force", SqliteType.Real);
        var current = command.Parameters.Add("$current", SqliteType.Real);
        var voltage = command.Parameters.Add("$voltage", SqliteType.Real);
        var displacement = command.Parameters.Add("$displacement", SqliteType.Real);
        var stationId = command.Parameters.Add("$stationId", SqliteType.Integer);
        var stationName = command.Parameters.Add("$stationName", SqliteType.Text);
        var controllerFrame = command.Parameters.Add("$controllerFrame", SqliteType.Text);
        var acquisitionSequence = command.Parameters.Add("$acquisitionSequence", SqliteType.Integer);
        var digitalInputs = command.Parameters.Add("$digitalInputs", SqliteType.Integer);
        var forceInputVoltage = command.Parameters.Add("$forceInputVoltage", SqliteType.Real);
        var currentInputVoltage = command.Parameters.Add("$currentInputVoltage", SqliteType.Real);
        var voltageInputVoltage = command.Parameters.Add("$voltageInputVoltage", SqliteType.Real);
        var displacementInputVoltage = command.Parameters.Add("$displacementInputVoltage", SqliteType.Real);
        var dataQuality = command.Parameters.Add("$dataQuality", SqliteType.Text);
        var cycle = command.Parameters.Add("$cycle", SqliteType.Integer);
        var phase = command.Parameters.Add("$phase", SqliteType.Text);
        foreach (var sample in samples)
        {
            testNo.Value = sample.TestNo;
            time.Value = sample.Time.ToString("O");
            elapsed.Value = sample.ElapsedMilliseconds;
            force.Value = sample.Force;
            current.Value = sample.Current;
            voltage.Value = sample.Voltage;
            displacement.Value = sample.Displacement;
            stationId.Value = sample.StationId;
            stationName.Value = sample.StationName;
            controllerFrame.Value = sample.ControllerFrame;
            acquisitionSequence.Value = sample.AcquisitionSequence;
            digitalInputs.Value = sample.DigitalInputs;
            forceInputVoltage.Value = sample.ForceInputVoltage is double forceRaw ? forceRaw : DBNull.Value;
            currentInputVoltage.Value = sample.CurrentInputVoltage is double currentRaw ? currentRaw : DBNull.Value;
            voltageInputVoltage.Value = sample.VoltageInputVoltage is double voltageRaw ? voltageRaw : DBNull.Value;
            displacementInputVoltage.Value = sample.DisplacementInputVoltage is double displacementRaw ? displacementRaw : DBNull.Value;
            dataQuality.Value = sample.DataQuality;
            cycle.Value = sample.Cycle;
            phase.Value = sample.Phase;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public IReadOnlyList<TestSampleRecord> GetTestSamples(string testNo)
    {
        var result = new List<TestSampleRecord>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT test_no, sample_time, elapsed_ms, force, current, voltage,
                   displacement, station_id, station_name, controller_frame, cycle, phase,
                   acquisition_sequence, digital_inputs, force_input_voltage, current_input_voltage,
                   voltage_input_voltage, displacement_input_voltage, data_quality
            FROM test_samples WHERE test_no=$testNo ORDER BY sample_time
            """;
        command.Parameters.AddWithValue("$testNo", testNo);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TestSampleRecord
            {
                TestNo = reader.GetString(0),
                Time = DateTime.Parse(reader.GetString(1)),
                ElapsedMilliseconds = reader.GetInt64(2),
                Force = reader.GetDouble(3),
                Current = reader.GetDouble(4),
                Voltage = reader.GetDouble(5),
                Displacement = reader.GetDouble(6),
                StationId = reader.GetInt32(7),
                StationName = reader.GetString(8),
                ControllerFrame = reader.GetString(9),
                Cycle = reader.GetInt32(10),
                Phase = reader.GetString(11),
                AcquisitionSequence = reader.GetInt64(12),
                DigitalInputs = checked((ushort)reader.GetInt32(13)),
                ForceInputVoltage = reader.IsDBNull(14) ? null : reader.GetDouble(14),
                CurrentInputVoltage = reader.IsDBNull(15) ? null : reader.GetDouble(15),
                VoltageInputVoltage = reader.IsDBNull(16) ? null : reader.GetDouble(16),
                DisplacementInputVoltage = reader.IsDBNull(17) ? null : reader.GetDouble(17),
                DataQuality = reader.GetString(18)
            });
        }
        return result;
    }

    public string CheckIntegrity()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        return Convert.ToString(command.ExecuteScalar()) ?? "unknown";
    }

    public string CreateBackup(string? targetFolder = null)
    {
        targetFolder ??= Path.Combine(Path.GetDirectoryName(DatabasePath)!, "Backups");
        Directory.CreateDirectory(targetFolder);
        var backupPath = Path.Combine(targetFolder, $"durability_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        using var destination = new SqliteConnection($"Data Source={backupPath}");
        destination.Open();
        _connection.BackupDatabase(destination);
        AddLog("信息", "数据库", $"数据库备份完成：{backupPath}");
        return backupPath;
    }

    public IReadOnlyList<SystemLogEntry> GetLogs(string? level = null)
    {
        var result = new List<SystemLogEntry>();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT time, level, source, message FROM system_logs
            WHERE ($level = '' OR level = $level)
            ORDER BY id DESC LIMIT 1000
            """;
        command.Parameters.AddWithValue("$level", level is "全部" or null ? string.Empty : level);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SystemLogEntry
            {
                Time = DateTime.Parse(reader.GetString(0)),
                Level = reader.GetString(1),
                Source = reader.GetString(2),
                Message = reader.GetString(3)
            });
        }
        return result;
    }

    public void AddLog(string level, string source, string message)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO system_logs(time, level, source, message) VALUES($time, $level, $source, $message)";
        command.Parameters.AddWithValue("$time", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$level", level);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$message", message);
        command.ExecuteNonQuery();
    }

    private void SeedDemoData()
    {
        using var countCommand = _connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM plans";
        if (Convert.ToInt32(countCommand.ExecuteScalar()) == 0)
        {
            var plans = new[]
            {
                new TestPlan { Code="SB-DUR-001", Name="安全带卷收器标准耐久试验", Cycles=50000, TargetForce=450, Enabled=true },
                new TestPlan { Code="SB-DUR-002", Name="高负载往复耐久试验", Cycles=20000, TargetForce=600, Enabled=true },
                new TestPlan { Code="SB-FAT-003", Name="低速疲劳验证试验", Cycles=100000, TargetForce=280, Enabled=false }
            };
            foreach (var plan in plans) UpsertPlan(plan);
        }

        countCommand.CommandText = "SELECT COUNT(*) FROM test_records";
        if (Convert.ToInt32(countCommand.ExecuteScalar()) == 0)
        {
            var random = new Random(2026);
            for (var i = 0; i < 18; i++)
            {
                var started = DateTime.Today.AddDays(-i).AddHours(8 + i % 7).AddMinutes(i * 3);
                var passed = i is not 4 and not 13;
                AddTestRecord(new TestRecord
                {
                    TestNo = $"T{started:yyyyMMdd}-{i + 1:000}",
                    SpecimenNo = $"SB26-{1024 + i:0000}",
                    PlanName = i % 4 == 0 ? "高负载往复耐久试验" : "安全带卷收器标准耐久试验",
                    StartedAt = started,
                    Duration = TimeSpan.FromMinutes(38 + i * 7),
                    Cycles = passed ? 50000 : random.Next(8000, 32000),
                    PeakForce = Math.Round(470 + random.NextDouble() * 130, 1),
                    PeakDisplacement = Math.Round(62 + random.NextDouble() * 8, 1),
                    StationId = i % 2 + 1,
                    StationName = $"工位 {i % 2 + 1}",
                    Result = passed ? "合格" : "不合格",
                    Operator = i % 3 == 0 ? "张工" : "管理员"
                });
            }
        }

        countCommand.CommandText = "SELECT COUNT(*) FROM system_logs";
        if (Convert.ToInt32(countCommand.ExecuteScalar()) < 8)
        {
            AddLog("信息", "系统", "安全带耐久试验系统启动");
            AddLog("信息", "模拟量采集", "演示：PCIE-1604 + P-881B 三工位12路采集链路由软件模拟器生成");
            AddLog("信息", "CAN 通讯", "CAN 通道初始化成功，波特率 500 kbps");
            AddLog("警告", "试验控制", "演示模式已启用，当前数据由软件模拟器生成");
            AddLog("信息", "安全联锁", "安全门、急停与正反限位回路自检正常");
            AddLog("信息", "数据库", "SQLite 数据库连接成功，历史记录已加载");
            AddLog("警告", "传感器校准", "拉力传感器距下次校准日期还有 30 天");
            AddLog("报警", "试验控制", "历史报警演示：拉力瞬时超限，保护动作已执行并复位");
        }
    }

    private void EnsureLegacyPlansHaveFixedTemplateSteps()
    {
        var legacyPlans = new List<(long Id, double TargetForce)>();
        using (var query = _connection.CreateCommand())
        {
            query.CommandText = """
                SELECT p.id, p.target_force
                FROM plans p
                WHERE NOT EXISTS (SELECT 1 FROM plan_steps s WHERE s.plan_id=p.id)
                ORDER BY p.id
                """;
            using var reader = query.ExecuteReader();
            while (reader.Read()) legacyPlans.Add((reader.GetInt64(0), reader.GetDouble(1)));
        }
        if (legacyPlans.Count == 0) return;

        using var transaction = _connection.BeginTransaction();
        foreach (var (planId, targetForce) in legacyPlans)
        {
            var target = targetForce.ToString("0.###", CultureInfo.InvariantCulture) + " N";
            var steps = new (string Action, string Target, double Duration, string Condition)[]
            {
                ("正向拉伸", target, 2.0, "达到目标拉力"),
                ("负载保持", target, 1.0, "保持时间到"),
                ("反向回程", "0 mm", 2.0, "到达原点"),
                ("弹簧复位确认", "≤2 mm", 0, "位移回零，否则报警"),
                ("等待", "—", 0.5, "动作间隔时间到"),
                ("循环计数", "+1", 0, "进入下一循环")
            };
            for (var index = 0; index < steps.Length; index++)
            {
                using var insert = _connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO plan_steps(plan_id, sequence_no, action_type, target_value,
                        duration_seconds, completion_condition)
                    VALUES($planId, $sequence, $action, $target, $duration, $condition)
                    """;
                insert.Parameters.AddWithValue("$planId", planId);
                insert.Parameters.AddWithValue("$sequence", index + 1);
                insert.Parameters.AddWithValue("$action", steps[index].Action);
                insert.Parameters.AddWithValue("$target", steps[index].Target);
                insert.Parameters.AddWithValue("$duration", steps[index].Duration);
                insert.Parameters.AddWithValue("$condition", steps[index].Condition);
                insert.ExecuteNonQuery();
            }
        }
        transaction.Commit();
        AddLog("信息", "数据库迁移", $"已为 {legacyPlans.Count} 个旧方案补齐受支持的固定六步模板；启动前仍会重新校验。");
    }

    public void Dispose() => _connection.Dispose();

    private void SaveMigrationBackup(string json)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO settings(key, json, updated_at)
            VALUES($key, $json, $updatedAt)
            """;
        command.Parameters.AddWithValue("$key", $"migration_backup_v1_{DateTime.Now:yyyyMMddHHmmss}");
        command.Parameters.AddWithValue("$json", json);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O"));
        command.ExecuteNonQuery();
    }

    private void EnsureColumn(string tableName, string columnName, string definition)
    {
        using var check = _connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase)) return;
        }
        reader.Close();
        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        try
        {
            alter.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                                         ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // 另一个同时启动的进程已完成相同迁移，当前连接可继续使用。
        }
    }
}
