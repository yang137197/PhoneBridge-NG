# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-006 r21 UI、单卡状态与语言收口已通过限定验收。文件位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包，也不替换 v0.1.0 正式交付。

## 当前 r21 候选

### Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r21.zip`
- SHA-256：`9BE40E1622C3769250B42A1FBFC7C2C10D2173E8B9763097B111819016DCE9A7`
- 大小：115,438,166 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。
- 隔离数据根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.21`

必须使用打包候选和 `--ui-preview`；`scripts/Start-WindowsPreview.ps1` 是非隔离开发入口，不能作为本地验收启动方式。

### Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r21.apk`
- SHA-256：`D98D91CA20C23122F8B98AAB5795645241B72573204F7814B1F0E3699EB6C4BA`
- 大小：4,139,311 bytes
- 包名：`org.phonebridge.ng.uipreviewr21`
- 版本：`0.2.0-ui-preview-r21`，versionCode 2

r21 已安装到 Redmi；Samsung 仍运行 r20，不能把 Samsung r20 状态当作 r21 证据。每轮候选使用独立应用 ID，不读取正式 Android 应用或旧候选的数据。

## r21 已通过

1. Windows 从打包的 r21 隔离候选启动；当前页面与托盘切换 English 后立即更新，语言写入 r21 隔离设置。
2. Windows 从英文托盘正常退出后进程归零；同一路径冷启动仍为 English。
3. Redmi r21 首次启动为简体中文；切换 English 后当前页面和冷启动均保持英文，偏好回读为 `en-US`。
4. Windows 单设备忙碌/取消只改变本卡的模型断言通过；P2-005 已通过的真实双设备流程未重复。

完整证据见 [P2-006 验证](audit/P2-006-VALIDATION.md)。

## 仍沿用的 r20 多设备证据

- r20 Windows 同时连接 Redmi P: 与 Samsung E:；两个盘符均完成双向小文件散列回读。
- 停止 Redmi 后 Samsung E: 继续浏览和传输；托盘退出后客户端、全部 rclone 和两个盘符均清理。
- 完整证据见 [P2-005 验证](audit/P2-005-VALIDATION.md)。P2-004 已通过的配对体验、记录管理、手机存储、移除、截屏和设备备注也未重复测试。

## 边界

- r21 只用于本地验收；正式签名 APK、Windows 安装器和正式升级路径尚未刷新。
- r21 Android 真实前台通知外观未触发；实现、双语资源、构建和 lint 已通过。
- 正式 Windows 配对记录当前为 0；如需旧正式配对必须重新配对。
- P2-005 手机端可能仍有 `PhoneBridge-P2-005*.txt` 测试标记，可由用户直接删除。
- 下一轮验收必须使用新的未占用 `rN`，不得覆盖 r21。
