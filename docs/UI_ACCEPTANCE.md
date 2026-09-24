# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-003 UI 复验已通过；以下 r12 是 P2-004 的全新隔离候选，已包含 Windows 配对候选迟到时自动选中的修复，用于验证“先配对、后共享、Windows 手动连接”及两端本地移除。文件位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r12.zip`
- SHA-256：`3E235833244C6E7BE3F3FC4E503947986474638C033F0FC99B6C220A091EBFC3`
- 大小：115,424,379 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。
- 隔离数据根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.12`

当前本机已打开 r12 验收窗口。该窗口不读取正式 `%LOCALAPPDATA%\PhoneBridge-NG` 或旧候选数据；启动后隔离根只有日志，没有配对目录。正式目录中的 3 条既有配对记录在准备前后未改动。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r12.apk`
- SHA-256：`561B9A6E661B26F668AEEE80E3D35F2B1D800CB75926AA34906D6B9584D68598`
- 大小：4,130,899 bytes
- 包名：`org.phonebridge.ng.uipreviewr12`
- 版本：`0.2.0-ui-preview-r12`，versionCode 2

r12 已用不带 `-r` 的全新安装部署到 Samsung；旧候选已卸载。准备结束时 r12 停留在首页：共享已停止、没有已配对电脑、“配对新电脑”和“开始共享”均可点击。私有目录只有系统 Activity 计数文件，没有 PhoneBridge 身份或配对记录。

## 验收顺序

1. Android 保持“共享已停止”，点击“配对新电脑”。
2. Windows 点击“添加手机”。即使页面先显示“请选择附近手机”，发现配对广播后也应自动显示 `SM-S9180`；输入 8 位配对码后“配对”应立即可用。点击“配对”并在手机批准。
3. 确认配对完成后 Windows 没有自动连接、没有创建盘符。
4. Android 返回首页并点击“开始共享”。
5. Windows 在设备卡片手动点击“连接”，确认盘符出现。
6. 分别从 Windows 和 Android 各验证一次本地移除：远端无需确认，本端记录与活动连接消失；之后必须按上述完整流程作为全新设备重新配对。

Android 所有 build type 均允许系统截屏。r12 只用于本轮验收，不覆盖正式包，也不作为以后候选的覆盖安装目标；下一轮必须使用新的 `rN`。
