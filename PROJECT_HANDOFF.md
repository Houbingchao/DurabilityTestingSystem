# 安全带耐久度测试系统：项目续接说明

> 用途：新建 Codex 开发任务时先阅读本文件，再根据用户的**当前需求**修改代码。
> 项目路径：`D:\1FILE\VibeCoding_project\DurabilityTestingSystem`

## 1. 当前目标

WinForms + SQLite 的安全带耐久度测试上位机。当前是可演示、可继续硬件集成的三工位基线：标准使用 2 工位，并预留第 3 工位扩展。

主要能力：试验配方/步骤、三工位选择和并行试验界面、实时力/电流/电压/位移数据展示、趋势曲线、历史记录、CSV/Excel 导出、诊断与参数配置。

## 2. 技术与代码入口

- .NET 8 WinForms，`Microsoft.Data.Sqlite`。
- 主项目：`DurabilityTestingSystem.csproj`；主界面：`UI/MainForm.cs`。
- 领域模型与配置：`Models/SystemModels.cs`。
- 试验引擎：`Infrastructure/DemoTestEngine.cs`（演示）、`Infrastructure/HardwareTestEngine.cs`（现场基线）。
- 数据库：`Data/AppDatabase.cs`。
- 三工位拓扑：`StationTopology`（模型文件中集中定义）。
- 方案步骤编译：`Infrastructure/TestPlanCompiler.cs`。

## 3. 已冻结硬件与适配器结构

| 功能 | 已冻结型号 | 代码位置 |
|---|---|---|
| CAN | 周立功 USBCANFD-200U（USB） | `HardwareAdapter.ZlgUsbCanFd` |
| 模拟量采集 | 新超仁达 PCIE-1604（PCIe） | `HardwareAdapter.XinChaoRenDaPcie1604` |
| 接线端子 | 新超仁达 P-881B | `P881BSignalConverter.cs` |
| 组合现场平台 | CAN + 采集卡 | `HardwareAdapter.Site` |
| 公共契约 | 硬件接口、样本、命令 | `DurabilityTestingSystem.Contracts` |

每工位的默认测量量：拉力、驱动电流、母线电压、位移，共 4 路；3 工位共 12 个物理量。当前默认按差分方式规划为 AI0~AI23 的相邻通道对。

## 4. 不能忽略的现场限制

1. 当前根目录 `system-profile.json` 为 **Demo**，现场生产模式默认故障关闭，不能直接驱动电机。
2. 未获得电机 DBC/字节协议、节点 ID、命令 ACK、状态反馈和心跳规则前，禁止实现/启用真实电机动作。
3. PCIE-1604 与 P-881B 的正式搭配需取得厂家书面兼容确认，并完成 DB37 针脚连续性测试；不要把它写成“已验证兼容”。
4. PCIE-1604 的 DI 是 0~5V TTL/CMOS，不能直接接 24V 急停、限位或安全门；安全停机必须由独立硬件安全链完成。
5. 原厂 SDK/DLL 不提交到公开 Git 仓库。现场按各适配器 `Vendor/README.md` 放置原厂文件；缺失时应继续保持故障关闭。

## 5. 当前已实现的核心行为

- 固定六步试验流程：拉动 → 保持 → 回程 → 弹簧复位确认 → 等待 → 循环计数。
- 方案保存了修订号和步骤快照；历史记录可追溯方案版本。
- 每工位保存力、电流、电压、位移样本；超限触发样本优先保存。
- 硬保护越限采用停机并锁存报警思路；停止会逐工位尝试。
- 演示数据、参数页、方案页、历史页、设备诊断页与关于页均已具备。

这些属于“硬件集成基线”，不是可以直接验收的最终现场版。

## 6. 常用验证命令

```powershell
dotnet build DurabilityTestingSystem.csproj -c Release --no-restore
dotnet run --project DurabilityTestingSystem.csproj
```

注意：当前 `.sln` 未定义 `Release|x64`，不要执行 `dotnet build DurabilityTestingSystem.sln -c Release -p:Platform=x64`。

最近已推送的源码提交：`3444a57`（分支 `master`）。
GitHub：<https://github.com/Houbingchao/DurabilityTestingSystem>

## 7. 后续协作规则（优先级高）

用户每次提出新功能、界面优化或硬件信息时：

1. 只聚焦**当前需求**，修改必要代码、配置和少量必要 `.md` 说明。
2. 不要主动重审、重写、同步旧的 Word、PPT、历史指导手册、截图、发布包或全量交付文档。
3. 除非用户明确要求，不要重新打完整安装包/发布包。
4. 完成后只说明：改了什么、构建/运行是否成功、用户下一步如何验证。
5. 保留 Production 故障关闭原则；任何实际硬件解锁前，都要提示缺失的协议、安全链和实测证据。

## 8. 本地工作区注意事项

本地仍有未提交的历史文档、截图、临时目录和输出包。这些是用户资产，默认不删除、不提交、不纳入当前需求。除非用户明确点名，不要处理它们。
