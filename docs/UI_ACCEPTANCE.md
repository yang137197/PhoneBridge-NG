# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-003 UI 复验已通过；以下 r10 是 P2-004 的全新隔离候选，用于验证“先配对、后共享、Windows 手动连接”及两端本地移除。文件位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r10.zip`
- SHA-256：`34125EBF9D36C4564DFF09DFE1E4C1EA4518724D182BD6BEEBBC1BC051F5CAE7`
- 大小：115,424,253 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。
- 隔离数据根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.10`

当前本机已打开 r10 验收窗口。该窗口不读取正式 `%LOCALAPPDATA%\PhoneBridge-NG` 或旧候选数据；启动后隔离根只有日志，没有配对目录。正式目录中的 3 条既有配对记录在准备前后未改动。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r10.apk`
- SHA-256：`97072BF816F1C7F841E7D5545AB8D3AA9E3589FDB21C9DA0D0AA175EB48686AF`
- 大小：4,130,899 bytes
- 包名：`org.phonebridge.ng.uipreviewr10`
- 版本：`0.2.0-ui-preview-r10`，versionCode 2

r10 已用不带 `-r` 的全新安装部署到 Samsung；旧 r8/r9 已卸载。准备结束时 r10 已清除内部数据、重新授予验收所需权限并停留在首页：共享已停止、没有已配对电脑、“配对新电脑”和“开始共享”均可点击。私有目录只有系统 Activity 计数文件，没有 PhoneBridge 身份或配对记录。

## 验收顺序

1. Android 保持“共享已停止”，点击“配对新电脑”。
2. Windows 点击“添加手机”，选择手机、输入 8 位配对码并点击“配对”；在手机批准。
3. 确认配对完成后 Windows 没有自动连接、没有创建盘符。
4. Android 返回首页并点击“开始共享”。
5. Windows 在设备卡片手动点击“连接”，确认盘符出现。
6. 分别从 Windows 和 Android 各验证一次本地移除：远端无需确认，本端记录与活动连接消失；之后必须按上述完整流程作为全新设备重新配对。

Android 所有 build type 均允许系统截屏。r10 只用于本轮验收，不覆盖正式包，也不作为以后候选的覆盖安装目标；下一轮必须使用新的 `rN`。
