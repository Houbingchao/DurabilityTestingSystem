# 周立功 USBCANFD-200U 硬件适配器

本项目是主程序的正式 CAN 传输适配器，固定服务于 **周立功 USBCANFD-200U（USB 2.0、双 CAN/CAN FD 通道）**。

## 已实现

- 从 `system-profile.json` 以反射方式加载；
- 打开指定 USB 设备索引；
- 按参数初始化 CAN0/CAN1、仲裁域/数据域波特率、ISO/Non-ISO 和内部 120Ω 终端电阻；
- 经典 CAN 与 CAN FD 原始报文收发；
- USB 在线状态检查、断线识别和安全释放；
- x64 厂家 DLL、依赖目录和授权文件随程序发布；
- P/Invoke 结构体运行前内存布局检查。

## 尚未开放的功能

当前适配器不会猜测安全带电机报文，因此拉出、保持、回程、暂停、停止和复位仍处于安全锁定。必须取得并评审以下资料后，才能增加电机协议层：

1. 使用经典 CAN 还是 CAN FD；
2. 仲裁域/数据域波特率；
3. 每个执行机构的节点 ID 与 CAN 通道规划；
4. DBC 文件，或完整字节协议（CAN ID、字节序、比例、偏移、单位、周期）；
5. 使能、动作、停止、复位、状态、故障、心跳报文；
6. Alive Counter、Checksum/CRC、超时和掉线后的安全行为。

模拟量采集与急停/限位等安全输入也必须由对应硬件适配器实现并联调，不能用 CAN 在线替代整机可启动条件。

## 现场准备

- 安装周立功官方驱动和 ZCANPRO；
- 工控机使用 64 位 Windows，程序按 `win-x64` 发布；
- 保持 `zlgcan.dll` 与 `kerneldlls` 目录结构完整；
- 使用原装或可靠 USB 线，并做防松固定，关闭 USB 选择性暂停；
- 按说明书连接 DB9：2-CAN_L、7-CAN_H、3/6-GND、5-SHLD；
- 总线采用干线拓扑，两端各 120Ω；软件内部终端电阻只能在适配器确实位于总线端点时启用；
- ZCANPRO 联调完毕后必须关闭，避免独占同一设备。

## 正式配置

`system-profile.json`：

```json
{
  "Mode": "Production",
  "ProfileName": "USBCANFD-200U 现场正式配置",
  "AutoConnectOnStartup": false,
  "HardwareAdapterAssembly": "DurabilityTestingSystem.HardwareAdapter.ZlgUsbCanFd.dll",
  "HardwareAdapterType": "DurabilityTestingSystem.HardwareAdapter.ZlgUsbCanFd.ZlgUsbCanFdHardwarePlatform"
}
```

首次现场调试应保持 `AutoConnectOnStartup=false`，先在“设备诊断”页人工执行连接与自检。只有联锁、协议、动作方向和停机链路全部验收后，才考虑改为自动连接。
