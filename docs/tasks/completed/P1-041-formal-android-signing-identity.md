# P1-041 — 正式 Android 长期签名身份

状态：已完成。日期：2026-09-23。

## 目标

在项目目录外建立 PhoneBridge NG 的正式 Android 长期签名身份，并把完全相同的备份保存到独立 USB 介质；随后用该身份生成并核验一个第三方侧载构建。

## 范围

- 主身份：`%LOCALAPPDATA%\PhoneBridge-NG\Signing`。
- 独立备份：`E:\PhoneBridge-NG-Signing-Backup`。
- 操作者在本地遮罩终端中输入两次相同密码；密码只进入当前 PowerShell 进程的临时环境变量。
- 验证主副 keystore、身份记录、证书和侧载 APK 的一致性。

## 不做什么

不把密码、私钥或完整密钥路径写入项目日志；不上传、发布或安装正式 APK；不替换当前默认本地预览交付；不修改产品运行功能。

## 涉及文件

- `scripts/Initialize-AndroidReleaseSigning.ps1`：使用既有初始化实现。
- `.audit/runs/P1-041/initialize-formal-signing-interactive.ps1`：仅供本次本地遮罩输入和验证构建。
- 本任务记录与验收证据。

## 实现

- 使用固定 PKCS12、RSA-4096、`phonebridge-release` 别名，在 `%LOCALAPPDATA%\PhoneBridge-NG\Signing` 创建唯一正式身份。
- 在独立 Samsung USB 介质的 `E:\PhoneBridge-NG-Signing-Backup` 写入完全相同的 keystore 和身份记录。
- 密码只在独立遮罩窗口输入并通过当前进程环境传递；完成或失败后均清除环境变量。
- 使用正式身份在隔离目录 `.audit/delivery/p1-041-formal` 生成第三方侧载 APK和 Windows 安装器，未替换当前默认本地预览交付。
- 辅助窗口最初错误地把构建命令的普通输出整体当作 JSON解析，导致构建完成后报告 `ArgumentException`；已改为构建成功后直接读取 `delivery-manifest.json`。

## 测试

- 主、备份 keystore SHA-256 相同，并与身份记录一致。
- 两份身份记录逐字节和 SHA-256 相同，不含密码或密码环境变量名称。
- 主目录 ACL 只允许当前用户与 SYSTEM。
- 主目录位于磁盘 0，备份位于 USB 可移动磁盘 1。
- `apksigner verify --verbose --print-certs` 通过；APK证书摘要同时匹配身份记录和交付清单。
- APK、Windows 安装器、README 的 SHA-256 与交付清单一致。
- 机器可读证据的 17 项检查全部为 `true`。

## 验收结果

正式 Android 长期签名身份及独立 USB 备份已创建并通过验证，正式侧载构建链路已证明。正式 APK尚未安装；当前手机上的本地测试签名版本不会在本任务中卸载或迁移。
