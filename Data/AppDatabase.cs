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
                started_at TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                cycles INTEGER NOT NULL,
                peak_force REAL NOT NULL,
                result TEXT NOT NULL,
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
                cycle INTEGER NOT NULL,
                phase TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_test_samples_test_time
                ON test_samples(test_no, sample_time);
            """;
        command.ExecuteNonQuery();
        if (seedDemoData) SeedDemoData();
        else AddLog("信息", "系统", "正式运行模式数据库初始化完成，未写入演示记录");
    }

    public TestSettings LoadSettings()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT json FROM settings WHERE key = 'main' LIMIT 1";
        var json = command.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(json)
            ? new TestSettings()
            : JsonSerializer.Deserialize<TestSettings>(json) ?? new TestSettings();
    }

    public void SaveSettings(TestSettings settings)
    {
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
        command.CommandText = "SELECT id, code, name, cycles, target_force, updated_at, enabled FROM plans ORDER BY enabled DESC, id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TestPlan
            {
                Id = reader.GetInt64(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Cycles = reader.GetInt32(3),
                TargetForce = reader.GetDouble(4),
                UpdatedAt = DateTime.Parse(reader.GetString(5)),
                Enabled = reader.GetInt32(6) == 1
            });
        }
        return result;
    }

    public long UpsertPlan(TestPlan plan)
    {
        using var command = _connection.CreateCommand();
        if (plan.Id == 0)
        {
            command.CommandText = """
                INSERT INTO plans(code, name, cycles, target_force, enabled, updated_at)
                VALUES($code, $name, $cycles, $force, $enabled, $updatedAt)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE plans SET code=$code, name=$name, cycles=$cycles,
                target_force=$force, enabled=$enabled, updated_at=$updatedAt WHERE id=$id
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
                   cycles, peak_force, result, operator_name
            FROM test_records
            WHERE ($keyword = '' OR test_no LIKE $likeKeyword OR specimen_no LIKE $likeKeyword)
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
                Operator = reader.GetString(9)
            });
        }
        return result;
    }

    public void AddTestRecord(TestRecord record)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO test_records(test_no, specimen_no, plan_name, started_at,
              duration_seconds, cycles, peak_force, result, operator_name)
            VALUES($testNo, $specimenNo, $planName, $startedAt, $duration,
              $cycles, $peakForce, $result, $operator)
            """;
        command.Parameters.AddWithValue("$testNo", record.TestNo);
        command.Parameters.AddWithValue("$specimenNo", record.SpecimenNo);
        command.Parameters.AddWithValue("$planName", record.PlanName);
        command.Parameters.AddWithValue("$startedAt", record.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$duration", (long)record.Duration.TotalSeconds);
        command.Parameters.AddWithValue("$cycles", record.Cycles);
        command.Parameters.AddWithValue("$peakForce", record.PeakForce);
        command.Parameters.AddWithValue("$result", record.Result);
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
                voltage, position, cycle, phase)
            VALUES($testNo, $time, $elapsed, $force, $current, $voltage, $position, $cycle, $phase)
            """;
        var testNo = command.Parameters.Add("$testNo", SqliteType.Text);
        var time = command.Parameters.Add("$time", SqliteType.Text);
        var elapsed = command.Parameters.Add("$elapsed", SqliteType.Integer);
        var force = command.Parameters.Add("$force", SqliteType.Real);
        var current = command.Parameters.Add("$current", SqliteType.Real);
        var voltage = command.Parameters.Add("$voltage", SqliteType.Real);
        var position = command.Parameters.Add("$position", SqliteType.Real);
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
            position.Value = sample.Position;
            cycle.Value = sample.Cycle;
            phase.Value = sample.Phase;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
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
                    Result = passed ? "合格" : "不合格",
                    Operator = i % 3 == 0 ? "张工" : "管理员"
                });
            }
        }

        countCommand.CommandText = "SELECT COUNT(*) FROM system_logs";
        if (Convert.ToInt32(countCommand.ExecuteScalar()) < 8)
        {
            AddLog("信息", "系统", "安全带耐久试验系统启动");
            AddLog("信息", "模拟量模块", "Modbus TCP 模块连接正常：192.168.10.60");
            AddLog("信息", "CAN 通讯", "CAN 通道初始化成功，波特率 500 kbps");
            AddLog("警告", "试验控制", "演示模式已启用，当前数据由软件模拟器生成");
            AddLog("信息", "安全联锁", "安全门、急停与正反限位回路自检正常");
            AddLog("信息", "数据库", "SQLite 数据库连接成功，历史记录已加载");
            AddLog("警告", "传感器校准", "拉力传感器距下次校准日期还有 30 天");
            AddLog("报警", "试验控制", "历史报警演示：拉力瞬时超限，保护动作已执行并复位");
        }
    }

    public void Dispose() => _connection.Dispose();
}
