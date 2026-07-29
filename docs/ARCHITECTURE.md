# 架构与二次开发说明

## 运行结构

```text
WinForms UI
  └─ ITestEngine
      ├─ DemoTestEngine                     演示模式
      └─ HardwareTestEngine                 正式流程、采样与软件保护
          └─ IHardwarePlatform              现场适配器边界
              ├─ CAN 卡厂家 SDK             电机控制与状态
              ├─ 模拟量采集 SDK/Modbus      拉力、电流、电压
              └─ DI/安全联锁                急停、安全门、正反限位

AppDatabase (SQLite)
  ├─ settings / plans / plan_steps
  ├─ test_records / test_samples
  └─ system_logs
```

UI 和通用试验状态机不引用任何厂家 DLL。硬件型号改变时，只替换实现 `IHardwarePlatform` 的独立适配器程序集。

## 正式启动门槛

`HardwareTestEngine.StartAsync()` 在启动前检查 `IHardwarePlatform.Health.CanStartTest`。适配器必须确认以下条件全部成立后才能返回 `true`：

1. CAN 卡已打开、波特率正确、驱动器节点有有效应答且无报警；
2. 模拟量设备在线，拉力/电流/电压通道无断线、超量程或标定失效；
3. 急停释放、安全门闭合、正反限位状态合理；
4. 电机未运行、允许使能、控制模式与程序预期一致；
5. 配方参数、量程和保护阈值已校验。

任何一项失败都必须返回失败结果，并保持 `CanStartTest=false`。

## 接入顺序

1. 复制 `HardwareAdapter.Template` 为现场适配器项目并引用厂家 x64 DLL。
2. 先实现连接、自检和诊断状态，不实现运动。
3. 实现模拟量原始值读取、两点标定、工程量换算、滤波和断线判断。
4. 用总线分析工具确认 CAN 报文，再实现低速、无负载点动与停止。
5. 实现拉伸、保持、回程、暂停、复位和故障停机。
6. 接入硬件急停、正反限位和安全门，完成故障注入测试。
7. 构建适配器 DLL，复制到发布目录并配置 `system-profile.json`。
8. 在“设备诊断”页执行连接与自检；未全部在线前不得开始带载试验。

## 关键边界

- 软件停止不能替代硬件急停、安全继电器和驱动器 STO/使能切断。
- 若安全带电机为直流或无刷直流，普通交流电流互感器不能测量直流母线电流；应确认测量点后选霍尔电流传感器、隔离变送器或分流器方案。
- 配方步骤目前已持久化，但正式适配器需要把动作类型映射为实际驱动器命令。
- `OverLimitDelay` 已保存为配置；现场适配器应按确定的滤波和延迟策略执行，硬件极限保护不可依赖该软件延迟。
