# 现场冻结硬件组合适配器

`SiteHardwarePlatform` 是主程序在 Production 模式加载的唯一整机适配器，组合：

- 周立功 USBCANFD-200U；
- 新超仁达 PCIE-1604；
- 新超仁达 P-881B；
- 三工位硬件映射（2个标准工位+1个预留扩展工位）。

CAN和DAQ可独立连接、诊断。电机协议、端子兼容性、SDK实机验证及TTL安全信号隔离验收全部完成前，`Health.CanStartTest`始终保持`false`。这不是Demo模拟，而是现场硬件尚未满足安全启动条件时的真实失败状态。
