# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-003 UI 复验已通过；以下制品已刷新为 P2-004 本地移除/截屏策略候选，等待真实配对验收。文件均位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r7.zip`
- SHA-256：`C9D375AC16AEC445AD2E35650CE10B06C4013E3CFBE42243F07BDA73138CD504`
- 大小：39,560,586 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。

当前本机已打开 r7 验收窗口。设备设置只保留“移除此手机”，说明其只清理 Windows 本地配对信息且再次连接需要重新配对。`--ui-preview` 只隔离单实例互斥体，不隔离配对和设置数据；未进入真实移除验收前不要点击现有真实设备的删除按钮。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview.apk`
- SHA-256：`6A99753BA44BFCF1C06F3E7055F0F4BCDFE9B65AB94958C798990A250AFF2A35`
- 大小：4,149,724 bytes
- 包名：`org.phonebridge.ng.uipreview`
- 应用内名称：`PhoneBridge NG · UI 验收`；Samsung 启动器/应用信息当前仍显示 `PhoneBridge NG`，原因尚未验证，以包名区分验收应用
- 版本：`0.2.0-ui-preview`，versionCode 2

验收包已在 Samsung 上覆盖安装。Android 所有 build type 已取消 `FLAG_SECURE`；当前验收包实际截屏成功。电脑详情中的操作改为“移除此电脑”，会删除手机本地的完整配对记录并断开旧连接。该包使用本地调试签名且有独立数据，不是升级包。

## 反馈范围

P2-003 视觉已通过。当前只验收 P2-004 行为：用可重建的真实配对分别在 Windows 和 Android 执行一次本地移除，确认另一端无需确认、旧凭据失效，并按全新设备重新配对。不要用不可恢复的正式配对直接试验。
