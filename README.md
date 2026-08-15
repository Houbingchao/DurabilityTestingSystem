# 安全带耐久试验系统

基于 .NET 8 WinForms + SQLite 的三工位工控上位机。本期实际建设 2 个标准工位，并预留 1 个默认停用的扩展工位；软件支持在已完成硬件映射和自检的工位中选择 1~3 个并行试验。

系统提供两种运行模式：

- `Demo`：使用内置模拟器生成拉力、电流、电压、位移和循环数据，用于界面、配方、数据与流程验证。
- `Production`：通过现场组合适配器访问真实 CAN 和模拟量硬件。任一关键资料、驱动、标定或安全联锁未通过时，必须保持 `CanStartTest=false` 并禁止试验动作。

## 冻结的硬件基线

| 类别 | 冻结型号 | 接口及用途 | 当前边界 |
|---|---|---|---|
| CAN 接口卡 | 周立功 `USBCANFD-200U` | USB 2.0；CAN0/CAN1；经典 CAN/CAN FD | 原始收发层已封装，电机 DBC/字节协议仍未取得和验证 |
| 模拟量采集卡 | 北京新超仁达 `PCIE-1604` | PCIe x1；32 路单端或 16 路差分；16 bit；最高 250 kHz 异步采集 | x64 SDK 调用层已建立，但厂家 DLL、驱动和实卡尚未完成联调 |
| 接线端子板 | 北京新超仁达 `P-881B` | 直通、滤波、分压、4~20 mA 转 1~5 V | 与 PCIE-1604 的直接配套关系必须取得厂家书面确认并逐针验证 |
| 工位 | 2 个标准工位 + 1 个扩展工位 | 每工位拉力、电流、电压、位移各 1 路 | 扩展工位完成硬件、标定和安全验收前默认禁用 |

> **端子板风险：**PCIE-1604 手册推荐的是 `PCLD-881`，P-881B 手册写明直接配套 `PCI-1632 Ver3.0`；两份手册对 DB37 第 19 脚的定义也不同（外部时钟/外部触发）。其他模拟量针脚看起来基本对应，但这不足以证明可直接连接。正式接线前必须取得厂家书面确认、正式针脚表和线缆型号，并完成断电通断测试。

## 当前软件能力

- 1~3 工位选择、并行循环、监视工位切换和独立结果追溯
- 每工位独立 CAN 节点、四类模拟量通道、限位映射和标定系数
- 实时趋势、循环阶段、启停/暂停/复位与超限状态机
- 固定六步方案闭环：正向拉伸 → 负载保持 → 反向回程 → 弹簧复位确认 → 等待 → 循环计数；未知/CAN自由动作在协议冻结前拒绝应用
- 试验启动时冻结方案版本、六步内容和有效参数；SQLite v4 按工位保存方案ID/编号/版本/JSON快照与终结原因
- 阶段按顺序推进，每完成一次回程与复位确认才把循环加一；UI卡顿不会按墙钟跨阶段补计循环
- 多工位停机采用逐工位尽力停止并汇总结果；任一工位停机未确认时保持报警锁存，不返回“就绪”
- 试验参数、方案步骤、汇总记录和原始采样持久化
- Excel (`.xlsx`) 与 TXT 导出、系统日志、SQLite 完整性检查和在线备份
- Demo/Production 数据库隔离
- `HardwareAdapter.ZlgUsbCanFd`：USBCANFD-200U 原始 CAN/CAN FD 传输层
- `HardwareAdapter.XinChaoRenDaPcie1604`：PCIE-1604 x64 SDK 边界、后台扫描、超时和安全失败；PDF未明确的成功码、量程码、字节序等必须由经验证的`pcie1604-sdk-compatibility.json`提供
- `HardwareAdapter.Site`：组合 CAN、DAQ、端子板确认、标定和安全验收门槛

这些代码只表示已经建立真实硬件接入路径，**不表示 PCIE-1604、P-881B、电机协议或整机已经完成实物联调**。

## 开发运行

```powershell
dotnet restore
dotnet build DurabilityTestingSystem.sln -c Release
dotnet run
```

正式硬件 SDK 固定使用 x64，开发和发布都不得切换为 32 位。调试时可以临时覆盖运行模式，但未通过门槛仍不得启动动作：

```powershell
dotnet run -- --mode=Production
```

用于界面验收的截图命令：

```powershell
DurabilityTestingSystem.exe --capture=preview.png --page=control --width=1600 --height=900
```

页面名支持：`control`、`settings`、`plans`、`history`、`logs`、`diagnostics`、`about`。

## Production 配置与安全锁

现场组合适配器应配置为：

```json
{
  "SchemaVersion": 2,
  "Mode": "Production",
  "ProfileName": "USBCANFD-200U + PCIE-1604 三工位现场配置",
  "AutoConnectOnStartup": false,
  "HardwareAdapterAssembly": "DurabilityTestingSystem.HardwareAdapter.Site.dll",
  "HardwareAdapterType": "DurabilityTestingSystem.HardwareAdapter.Site.SiteHardwarePlatform",
  "Qualification": {
    "TerminalBoardCompatibilityApproved": false,
    "Pcie1604SdkValidated": false,
    "SafetySignalConditioningApproved": false,
    "MotorProtocolValidated": false,
    "ApprovedBy": "",
    "ApprovedAt": null,
    "EvidenceReference": ""
  },
  "Notes": "所有验收证据完成前保持启动锁定"
}
```

首次联调必须保持 `AutoConnectOnStartup=false`。只有以下项目均有证据并完成复核后，才能修改对应资格字段：

1. PCIE-1604 与 P-881B 的厂家书面兼容确认、DB37 针脚表、线缆确认和逐针测试；
2. PCIE-1604 x64 驱动、`pcieAPI.dll`、`CH365.dll`、结构体布局及实卡采样验证；
3. 0~5 V TTL 数字输入已通过隔离/电平转换，且独立急停、安全继电器、STO/使能切断回路验收通过；
4. 电机 DBC 或字节协议、心跳、CRC/计数器、应答、故障码和停机报文完成逐帧验证；
5. 所有启用工位完成通道核对、标准源验证和可追溯标定。

PCIE-1604 的 DI/DO 是 0~5 V TTL/CMOS，不能直接连接 24 V 限位、安全门或急停，也不能替代安全继电器和硬件急停链。

`pcie1604-sdk-compatibility.json`不能根据经验填写。它必须记录实际`pcieAPI.dll`的SHA-256，以及由厂家头文件/C#例程和标准源台架确认的API成功码、量程代码、结构体尺寸、返回字节序、差分通道计数与采样频率语义；文件缺失或哈希不匹配时适配器会拒绝打开板卡。

当前基线对 4~20 mA 经 250 Ω 得到的 1~5 V 信号使用 ±10 V ADC 档位，为电阻误差、噪声和传感器过量程保留削顶裕量；最终档位和精度必须用标准源实测确认。周立功接收层会保留设备微秒时间戳，但在电机 DBC/字节协议尚未冻结时，共享 CAN 通道上的帧只标记为“未归属”，不得伪装成某一工位的反馈证据。

## 驱动与数据位置

- USBCANFD-200U：安装周立功官方驱动，用 ZCANPRO 验证后关闭 ZCANPRO，再启动本程序。
- PCIE-1604：关机安装板卡；优先使用厂家 `DPInst64.exe` 安装 x64 驱动；先用厂家 C#/VC 测试程序验证，再部署正式程序。
- Demo 数据库：`%LocalAppData%\SeatbeltDurabilitySystem\durability-demo.db`
- Production 数据库：`%LocalAppData%\SeatbeltDurabilitySystem\durability.db`
- 备份目录：数据库同级的 `Backups` 文件夹

详细流程见：

- `docs/PCIE1604_P881B_USBCANFD200U_三工位集成与联调指南_20260811.md`
- `docs/ARCHITECTURE.md`
- `docs/安全带耐久试验系统_软件使用调试与落地指导手册.docx`
