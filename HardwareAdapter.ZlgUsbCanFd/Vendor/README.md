# 周立功 USBCANFD-200U 现场 SDK

本目录仅用于本地放置周立功 USBCANFD-200U 的原厂 x64 SDK，**不会提交到公开仓库**。

现场集成时，从周立功官方渠道取得与设备、驱动匹配的运行库，并按以下路径放置：

```text
Vendor/ZLG/x64/zlgcan.dll
Vendor/ZLG/x64/kerneldlls/...
```

项目检测到这些文件后会自动复制到程序输出目录。未放置 SDK 时，项目仍能用于 Demo 编译；正式硬件连接会故障关闭，不能启动试验。
