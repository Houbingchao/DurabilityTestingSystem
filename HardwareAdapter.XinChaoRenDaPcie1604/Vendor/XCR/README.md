将新超仁达官方 SDK 的 x64 文件放在 `x64` 子目录：`pcieAPI.dll`、`CH365.dll`。不要提交来源不明或32位版本的 DLL。

取得`pcieAPI.h`、厂家C#例程并完成台架确认后，把审核形成的
`pcie1604-sdk-compatibility.json`一并放入`x64`子目录。构建会将这三个运行文件复制到程序目录；
适配器会核对JSON中记录的`pcieAPI.dll` SHA-256和未在PDF中公开的SDK语义。不要在没有验证记录时创建该文件来绕过故障关闭。
