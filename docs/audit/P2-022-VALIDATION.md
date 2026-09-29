# P2-022 Android 首次点击打开配对验证

日期：2026-09-29
结论：通过限定缺陷修复验收；未发布。

## 已复现问题

公开 `v0.2.4` Android APK 全新安装且通知权限未授予时，第一次点击“配对新电脑”没有显示配对码，返回后第二次点击才显示。日志没有应用崩溃。

源码顺序确认 `requestPairing()` 先请求 `POST_NOTIFICATIONS`，随后启动仅配对前台服务；权限界面使 Activity 进入 `onPause()`，现有生命周期逻辑继而取消尚未激活的配对窗口。Android 官方文档说明通知权限不是启动前台服务的前置权限：<https://developer.android.com/develop/ui/views/notifications/notification-permission>。

## 修复

- 把通知权限策略按前台动作区分。
- 开始共享仍在 Android 13 及以上且权限缺失时请求通知权限。
- 打开配对不再请求通知权限，直接启动仅配对前台服务。
- 未改变配对协议、凭据、共享流程或 Windows 行为。

## 自动验证

- `NotificationPermissionPolicyTests`：2 项通过，0 失败。
- `:app:testDebugUnitTest :app:assembleDebug :app:lintDebug`：通过。
- `scripts/Verify-AndroidApp.ps1`：`BUILD SUCCESSFUL`，123 个 Gradle 任务完成。

## 候选身份

- 候选路径：`.audit/delivery/P2-022-android-first-tap/output/PhoneBridge-NG-0.2.4.apk`
- APK SHA-256：`AAA3A354EB525F2A99682C98A8BB07F0CC0AD6FFF5133408EF45E282DAC0DA2A`
- 候选 manifest 中 APK SHA-256：一致。
- APK Signature Scheme v2：通过。
- 签名证书 SHA-256：`66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4`，与正式长期身份一致。

## 真机验证

设备：Redmi K40，型号 `M2012K11AC`，ADB 序列 `61c9a964`。

前置状态：所有文件访问为允许；`POST_NOTIFICATIONS` 为 `granted=false`；共享已停止；无配对文件和后台服务。

同签名候选原位安装成功。用户仅点击一次“配对新电脑”后确认配对码立即显示；运行回读同时确认配对服务已启动、TCP 8273 正在监听、通知权限仍未授予、配对记录仍为 0。没有接受配对，也没有记录或输出配对码。

UIAutomator 因持续刷新的配对页无法取得 idle state，因此“配对码可见”由用户现场确认；服务、监听、权限和零配对记录由 ADB 独立回读。停止应用后，进程、服务和 TCP 8273 监听均消失，配对记录仍为 0。

验收完成后使用 P2-021 已从 GitHub Release 下载并按 manifest 核验的公开 `v0.2.4` APK 原位覆盖测试候选。公开 APK SHA-256 为 `578E135C03EED32C8EC70ECAB4EDCAE023F4637E7A0BB1B7C4F03909B693DD1F`，与公开 manifest 一致；安装后版本为 `0.2.4` / versionCode 6，通知权限仍未授予、所有文件访问仍允许且配对记录为 0，可作为 P2-023 开始时重新核验的 Android 正式基线。

## 边界

该 APK 沿用开发基线版本号 `0.2.4`，只作为本地定向验证候选，不是公开 `v0.2.4` 的替代品，也不是新的正式版本。未推送分支、未创建标签或 Release。正式版本递增和动态 `v0.2.4 → v0.2.5` 升级验收属于 P2-023。
