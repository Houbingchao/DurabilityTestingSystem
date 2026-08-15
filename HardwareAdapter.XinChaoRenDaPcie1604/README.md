# 新超仁达 PCIE-1604 / P-881B 采集适配器

本项目封装 PCIE-1604 的 x64 `pcieAPI.dll`，主程序和 UI 不直接引用厂家 API。

当前代码提供板卡打开/关闭、AD 参数设置、后台单点扫描、原始码转电压、移动平均、DI 读取、超时与断线诊断框架。连接动作不会擅自调用整板复位，避免在尚未确认现场安全输出状态时改变 DA/DO。

## 故障关闭门槛

PCIE-1604 V1.0.2 PDF 没有给出以下信息，因此代码不会用经验值冒充厂家定义：

- 通用 API 的成功返回码；
- 四个 AD 量程对应的数字代码；
- 差分模式下 `u8ChCount` 的准确计数语义；
- `pcie_AD_GetSingleData` 的返回布局、长度单位和字节序；
- `dfADFreq` 表示总转换率还是完整扫描率；
- 原生结构体最终对齐方式。

取得 `pcieAPI.h`、厂家 C# 例程并完成信号源台架验证后，需要在最终程序目录放置
`pcie1604-sdk-compatibility.json`。适配器会校验该文件、结构体大小和 `pcieAPI.dll` SHA-256；
文件缺失或 DLL 哈希不一致时保持故障关闭，不访问板卡。

兼容性文件字段包括：验证资料编号、`pcieAPI.dll` SHA-256、整数类型的API成功码、
两个原生结构体的整数大小、四个量程的整数代码，以及以下经过验证的语义字符串：

- `DifferentialChannelCountSemantics`: 当前解析器支持`PhysicalChannels`；
- `SingleDataOutputLayout`: 当前解析器支持`PhysicalChannelAscending`；
- `SingleDataBufferSizeUnit`: 当前解析器支持`Bytes`；
- `SingleDataByteOrder`: `HighByteFirst`或`LowByteFirst`；
- `SampleFrequencySemantics`: `AggregateConversionsPerSecond`或`ScansPerSecond`；
- `RangeCodes`: 必须包含`PlusMinus1V`、`PlusMinus2V`、`PlusMinus5V`和`PlusMinus10V`四个键。

不要从这段说明自行填写数值；数值必须来自实际`pcieAPI.h`、厂家C#例程和信号源测试记录。

上述固定字符串描述的是当前解析器已实现的布局；如果厂家资料显示不同，必须修改解析代码，不能只改配置强行通过。

正式联调前必须从厂家 SDK 的 `dll64` 目录取得并放入 `Vendor/XCR/x64`：

- `pcieAPI.dll`
- `CH365.dll`

驱动应使用厂家 `DPInst64.exe` 安装。厂家 DLL 不属于本仓库，本项目不会用空文件或模拟 DLL 冒充真实 SDK。

## P-881B 注意事项

- 4~20 mA 模式依靠板上 250 Ω 精密电阻把信号转换成 1~5 V；
- 直通、低通、分压和 I/V 模式由每个通道的 RA/RB/C 焊接配置决定，软件无法切换；
- P-881B 手册写明配套 PCI-1632，而 PCIE-1604 手册推荐 PCLD-881，且 DB37 第 19 脚定义不同；正式接线前必须取得厂家对“PCIE-1604 + P-881B + 指定 DB37 线缆”的书面确认并做连续性测试；
- P-881B 只承接模拟量。PCIE-1604 的 DI/DO 是 0~5 V TTL/CMOS，24 V 限位、安全门和急停信号不得直接接入，也不能替代硬件急停/STO/安全继电器。
