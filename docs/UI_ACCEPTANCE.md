# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-003 UI 复验已通过；以下 r13 是 P2-004 的全新隔离候选，已修复未配对候选被错误显示为“已连接”的问题。Windows 主列表只显示已配对手机；未配对手机必须先在 Android 打开“配对新电脑”，再从 Windows“添加手机”输入配对码。文件位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r13.zip`
- SHA-256：`7F342AA2B3B0499055ED2E42BA46F251A5B7B322FD8641F5AE20979111BFF3E6`
- 大小：115,424,736 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。
- 隔离数据根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.13`

当前本机已打开 r13 验收窗口。该窗口不读取正式 `%LOCALAPPDATA%\PhoneBridge-NG` 或旧候选数据；启动后隔离配对数为 0。正式目录中的 3 条既有配对记录在准备前后未改动。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r13.apk`
- SHA-256：`B0841DB2B31B8EEB56C6B49A301AC9D4BD8C6DE0392470F01DC844BCD18C37FF`
- 大小：4,130,899 bytes
- 包名：`org.phonebridge.ng.uipreviewr13`
- 版本：`0.2.0-ui-preview-r13`，versionCode 2

r13 已在实机验证后卸载并重新全新安装到 Samsung；旧候选已卸载。准备结束时 r13 停留在首页：共享已停止、没有已配对电脑、“配对新电脑”和“开始共享”均可点击。私有目录只有系统 Activity 计数文件，没有 PhoneBridge 身份或配对记录。

## 验收顺序

1. Android 保持“共享已停止”。此时 Windows“我的手机”不得出现未配对手机，更不得显示“已连接”。
2. Android 点击“配对新电脑”，Windows 点击“添加手机”。发现配对广播后应自动显示 `SM-S9180`；未输入完整 8 位码时“配对”禁用，输入完整后才可用。点击“配对”并在手机批准。
3. 确认配对完成后 Windows 没有自动连接、没有创建盘符。
4. Android 返回首页并点击“开始共享”。
5. Windows 在设备卡片手动点击“连接”，确认盘符出现。
6. 分别从 Windows 和 Android 各验证一次本地移除：远端无需确认，本端记录与活动连接消失；之后必须按上述完整流程作为全新设备重新配对。

Android 所有 build type 均允许系统截屏。r13 只用于本轮验收，不覆盖正式包，也不作为以后候选的覆盖安装目标；下一轮必须使用新的 `rN`。
