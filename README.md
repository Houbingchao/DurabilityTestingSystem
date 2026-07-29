# 安全带耐久试验系统

基于 .NET 8 WinForms + SQLite 的工控上位机。项目现在同时具备两种运行模式：

- `Demo`：内置模拟器生成拉力、电流、电压和循环数据，用于界面演示与工艺确认。
- `Production`：通过外部硬件适配器接入 CAN 卡、模拟量采集卡、电机和安全联锁。关键设备未通过自检时，软件会阻止试验启动。

## 已实现

- 实时趋势、循环阶段、启停/暂停/复位和超限报警状态机
- 试验参数与硬件参数持久化
- 试验方案及循环步骤持久化
- 试验汇总记录和原始采样点分表保存
- 历史记录按日期、关键词和结果筛选，CSV 导出
- 系统日志、SQLite 完整性检查、在线备份、诊断信息导出
- Demo/Production 数据库隔离
- 厂家硬件适配器动态加载边界与安全失败模板

## 开发运行

```powershell
dotnet restore
dotnet run
```

调试时可临时覆盖运行模式，不修改配置文件：

```powershell
dotnet run -- --mode=Production
```

用于界面验收的截图命令：

```powershell
DurabilityTestingSystem.exe --capture=preview.png --page=control --width=1600 --height=900
```

页面名支持：`control`、`settings`、`plans`、`history`、`logs`、`diagnostics`、`about`。

## 正式模式

发布目录中的 `system-profile.json` 是运行配置。硬件适配器完成并复制到发布目录后，将配置改为：

```json
{
  "Mode": "Production",
  "ProfileName": "现场正式配置",
  "AutoConnectOnStartup": true,
  "HardwareAdapterAssembly": "DurabilityTestingSystem.HardwareAdapter.Site.dll",
  "HardwareAdapterType": "DurabilityTestingSystem.HardwareAdapter.Site.SiteHardwarePlatform",
  "Notes": "经现场联调与验收后启用"
}
```

适配器必须实现 `Infrastructure/IHardwarePlatform`。可从 `HardwareAdapter.Template` 项目开始，不要让 UI 直接引用厂家 DLL。

数据文件：

- Demo：`%LocalAppData%\SeatbeltDurabilitySystem\durability-demo.db`
- Production：`%LocalAppData%\SeatbeltDurabilitySystem\durability.db`
- 备份：同目录下 `Backups` 文件夹

完整现场接入流程见 `docs/安全带耐久试验系统_软件使用调试与落地指导手册.docx`。
