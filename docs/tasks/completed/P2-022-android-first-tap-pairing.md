# P2-022 — Android 首次点击打开配对

状态：已完成。日期：2026-09-29。

## 目标

修复公开 `v0.2.4` Android APK 全新安装后，未授予通知权限时第一次点击“配对新电脑”不显示配对码、第二次才成功的已复现缺陷。

## 范围

- 配对专用前台服务不以通知权限作为启动前置条件。
- 保留开始共享时现有通知权限请求，不改变共享、配对协议、凭据或 Windows 行为。
- 增加针对前台动作与通知权限关系的 Android 单元测试。
- 在真实手机保持通知权限未授予的条件下，安装同一正式签名的修复候选并只点击一次“配对新电脑”，核对配对码是否立即出现。

## 不做什么

- 不继续 P2-021 的全新 Windows 机器验收。
- 不自动接受配对，不记录或输出配对码。
- 不修改 Windows 客户端，不推送分支，不创建标签或 GitHub Release。
- 不把自动测试替代真实手机首次点击验收。

## 涉及文件

- `android/app/src/main/java/org/phonebridge/ng/MainActivity.kt`
- `android/app/src/main/java/org/phonebridge/ng/NotificationPermissionPolicy.kt`
- `android/app/src/test/java/org/phonebridge/ng/NotificationPermissionPolicyTests.kt`
- 本任务、README 和交接状态文档

## 实现

把通知权限判断按前台动作区分：开始共享仍在 Android 13 及以上且权限缺失时请求通知权限；打开配对不再请求该权限，直接启动仅配对前台服务。Android 官方说明通知权限不是启动前台服务的前置权限，因此该修复不绕过前台服务启动要求。

## 测试

- 已运行 `:app:testDebugUnitTest :app:assembleDebug :app:lintDebug`：成功。
- `NotificationPermissionPolicyTests`：2 项通过，0 失败。
- 已运行完整 `scripts/Verify-AndroidApp.ps1`：`BUILD SUCCESSFUL`，123 个 Gradle 任务完成。
- 本地签名修复候选 APK SHA-256 为 `AAA3A354EB525F2A99682C98A8BB07F0CC0AD6FFF5133408EF45E282DAC0DA2A`，与候选 manifest 一致；APK Signature Scheme v2 验证通过，签名证书 SHA-256 与正式身份 `66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4` 一致。
- Redmi K40（`M2012K11AC`，ADB `61c9a964`）保持通知权限未授予、所有文件访问已允许且零配对记录；同签名候选原位安装后，用户只点击一次“配对新电脑”即确认配对码立即可见。
- 首次点击后配对服务运行且 TCP 8273 监听，通知权限仍未授予，配对记录仍为 0；停止应用后进程、服务和监听均消失，仍无配对记录。
- 验收后已把手机原位恢复为公开 `v0.2.4` APK（SHA-256 `578E135C03EED32C8EC70ECAB4EDCAE023F4637E7A0BB1B7C4F03909B693DD1F`）；版本为 `0.2.4` / versionCode 6，通知权限仍未授予、所有文件访问仍允许且配对记录为 0。

## 验收结果

通过。已复现的首次点击失效根因已按单一范围修复；自动验证、正式签名一致性和真实手机“通知权限未授予时首次单击立即显示配对码”均通过。候选没有接受配对，也没有残留服务、监听或配对记录。

本任务候选仍使用开发基线版本号 `0.2.4`，只用于定向验证，不是新的正式版本或安装入口。正式版本仍为公开 `v0.2.4`；版本递增、完整交付与升级验收进入 P2-023。
