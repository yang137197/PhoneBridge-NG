# P1-041 正式 Android 长期签名身份验收

日期：2026-09-23。结果：通过。

## 已确认

- 正式身份使用 PKCS12、RSA-4096、固定别名 `phonebridge-release` 和非 Debug 产品证书。
- 主 keystore 与身份记录位于项目目录外的当前用户本地应用数据目录；ACL 只允许当前用户和 SYSTEM。
- 独立备份位于 Samsung USB 可移动介质。主目录为磁盘 0，备份为磁盘 1，物理独立性正向路径已实际通过。
- 主副 keystore SHA-256 相同；两份身份记录逐字节相同，且身份记录内的 keystore 摘要与实际文件一致。
- 身份记录不含密码值、密码字段或密码环境变量名称。密码只在本地遮罩窗口进入创建/构建进程，并在进程结束前清除。
- 正式模式生成 `PhoneBridge-NG-0.1.0.apk`，`apksigner` 完整性验证通过；APK证书 SHA-256 同时匹配正式身份和 `delivery-manifest.json`。
- 正式 APK SHA-256 为 `E53F618DB6888F14811A98BBC0EFD7965D72E44E84CE28D27C5840C427B1F0C3`；证书 SHA-256 为 `66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4`。
- Windows 安装器内容未因 Android 签名模式改变，SHA-256 保持 `B357083FD1B53491BE9F7C2E1378450B31BAC7FBA47EF63363D39F9158F280A1`。

机器可读证据：`.audit/runs/P1-041/formal-identity-verification.json`，17 项检查全部通过。

## 执行偏差与修正

首次终端打开请求没有把遮罩会话显示到用户看到的普通终端，导致一次候选密码作为无效 PowerShell 命令显示。该密码立即作废，对应历史记录已删除，并确认正式身份和备份均尚未创建；随后改用有明确窗口标题的独立遮罩输入窗口和全新密码完成创建。该候选密码没有用于正式身份。

正式构建已完成并写出清单后，本次交互辅助脚本因把包含工具日志的全部标准输出当作单一 JSON解析而报告 `ArgumentException`。辅助脚本已改为在构建成功后读取 `delivery-manifest.json`，现有产物则通过独立脚本重新核验。独立核验首次运行因未设置 `JAVA_HOME` 被 `apksigner` 拒绝；设置为项目固定 JDK 后，同一 APK验证通过。

## 未验证与边界

- 正式 APK未安装到手机。当前安装的是本地测试证书版本，首次切换正式证书必须卸载旧 APK，会清除其本地配对与设置。
- `.audit/delivery/p1-041-formal/output` 是本任务的验证产物；当前唯一交付入口 `.audit/delivery/output` 仍为本地测试签名版本。
- 密码由操作者独立保管，项目不能验证密码是否另有安全记录。
- 真实无 WinFsp 的干净 Windows 首装仍缺独立机器证据。
