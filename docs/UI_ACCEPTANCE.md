# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-005 r20 多设备最小链路已通过。文件位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包，也不替换 v0.1.0 正式交付。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r20.zip`
- SHA-256：`0728748900DE20C0FAC22EB06DC7DFCE16F4EB9EA8D3033D66309A7DCE529A31`
- 大小：115,435,951 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。
- 隔离数据根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.20`

必须使用打包候选和 `--ui-preview`；`scripts/Start-WindowsPreview.ps1` 是开发入口，不提供候选数据隔离，不能作为本地验收启动方式。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r20.apk`
- SHA-256：`004D5C39A672727B3CE61B6F4D9379918781CA1F636D3803079BD0D7B4978F0A`
- 大小：4,139,107 bytes
- 包名：`org.phonebridge.ng.uipreviewr20`
- 版本：`0.2.0-ui-preview-r20`，versionCode 2

同一 APK 已安装到 Samsung SM-S9180 与 Redmi K40。它使用独立应用 ID，不读取正式 Android 应用或旧候选的数据。

## 已通过的验收流程

1. 正确的 r20 Windows 隔离候选同时显示并连接 Redmi `M2012K11AC` 与 Samsung `SM-S9180`。
2. Redmi 挂载为 P:，Samsung 挂载为 E:；两者分别拥有独立 rclone、loopback RC 端口和设备会话目录。
3. 两个盘符均可浏览，并各完成一个小文件的本机→手机写入和手机→本机回读；三方 SHA-256 一致。
4. 停止 Redmi r20 后，其 P: rclone 退出；Samsung E: 保持可用，仍可浏览并再次完成双向回读，SHA-256 一致。
5. r20 日志使用 schema 2 和进程内会话序号 1、2；两台设备名称、型号、地址和盘符模式扫描为 0 命中。
6. 从托盘正常退出后，Windows 客户端、全部 rclone 和 P:/E: 均消失；r20 隔离配对记录保留。

完整自动与实机证据见 [P2-005 验证](audit/P2-005-VALIDATION.md)。P2-004 已通过的配对体验、记录管理、手机存储、移除、截屏和设备备注未重复测试。

## 边界

- r20 只用于本地验收；正式签名 APK、Windows 安装器和正式升级路径尚未刷新。
- 一次误用开发入口曾写入正式 Windows 数据根；清理后原有 3 条正式配对记录也已不存在且未找到备份。最终 P2-005 证据已全部在正确隔离根重做；旧正式配对如需使用必须重新配对。
- 本轮手机端创建的 `PhoneBridge-P2-005*.txt` 测试标记未能通过 ADB 验证清理，可由用户直接删除。
- 下一轮验收必须使用新的未占用 `rN`，不得覆盖 r20。
