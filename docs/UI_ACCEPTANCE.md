# PhoneBridge NG v0.2.0 UI 验收入口

状态：已通过。P2-004 原定的配对、手机存储、停止共享后的记录、两端移除、旧凭据失效和全新配对，以及 r18 新增的两端“设备备注”均已由用户验收通过。文件位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r18.zip`
- SHA-256：`A5FDF38872919EABB5080329F44863678CEEDD706CC564D6BB1319C2093A0BC2`
- 大小：111,983,778 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。
- 隔离数据根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.18`

当前本机已打开 r18 验收窗口。该窗口不读取正式 `%LOCALAPPDATA%\PhoneBridge-NG` 或旧候选数据；隔离配对数为 0、没有 rclone 或候选盘符，正式配对目录不受影响。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r18.apk`
- SHA-256：`AF6E2827997AFF0167C9EC1A85EDEB11C7E8A2241634E5B31A3E381F1068E14E`
- 大小：4,139,107 bytes
- 包名：`org.phonebridge.ng.uipreviewr18`
- 版本：`0.2.0-ui-preview-r18`，versionCode 2

r18 已用新包名全新安装并打开到 Samsung；私有目录没有 PhoneBridge 身份或配对文件，不继承 r17 的身份、设置或配对记录。旧 r15-r17 候选及临时 Debug/测试包已卸载。

## 验收结论

- 2026-09-26，用户确认 r18 两端“设备备注”真实界面验收通过；P2-004 至此完成。

- Windows 设备设置取消原独立“备注”栏；原“自定义名称”改名为“设备备注”，仍作为该手机在本机的显示名称。旧长备注数据只保留格式兼容，不再显示或修改。
- Android 已配对电脑详情新增“设备备注”。备注只保存在此手机的加密配对库，不发送给 Windows；填写后作为电脑卡片显示名称，原始电脑名称仍可见；清空后恢复原始名称。
- 两端设备备注都不改变设备身份、凭据、访问模式或配对协议。Android 修改备注不应断开当前连接。

## 已通过的验收流程

1. 按已通过的完整流程将 r18 Android 与 r18 Windows 重新配对；配对本身不得回退。
2. Windows 进入该手机的“设备设置”：只能看到一个“设备备注”输入框，不得再出现独立“备注”栏或“保存名称和备注”。
3. Windows 保存设备备注，返回首页确认卡片名称变化；重启 r18 后仍保持。清空并保存后恢复手机原始名称。
4. Android 进入该电脑详情：确认存在“设备备注”，填写后返回首页确认电脑卡片优先显示备注且仍能看到原始电脑名称。
5. Android 停止共享并重启 r18，备注仍保持；清空后恢复电脑原始名称。保存和清空过程中不得改变访问模式，也不得要求重新配对。

Android 所有 build type 均允许系统截屏。r18 只用于本轮验收，不覆盖正式包，也不作为以后候选的覆盖安装目标；下一轮必须使用新的 `rN`。
