# P2-026 — Android 更新下载进度反馈

状态：已完成。日期：2026-09-30。

## 目标

修复 Android 应用内更新下载期间没有持续可见反馈的问题，让用户明确看到正在下载的目标版本和实时百分比，并保持既有大小、SHA-256、包名、版本及签名校验不变。

## 范围

- 为 Android 更新下载服务增加基于已验证资产总大小的进度回调。
- 在下载期间显示不可误解的水平进度条和百分比；下载完成、失败、离开页面及 Activity 销毁时正确结束反馈。
- 同步简体中文与英文文案，并增加进度计算/边界自动测试。
- 运行 Android 定向测试及完整构建、单元测试与 Lint。

## 不做什么

- 不修改 GitHub Release、下载来源、更新安全校验或系统安装确认流程。
- 不引入后台下载器、通知下载、静默安装或断点续传。
- 不在本任务中发布、推送、打标签或生成正式 v0.2.6 Release。

## 涉及文件

- `android/app/src/main/java/org/phonebridge/ng/MainActivity.kt`
- `android/app/src/main/java/org/phonebridge/ng/AndroidUpdateService.kt`
- Android 中英文资源与更新测试

## 实现

- `AndroidUpdateService.download` 以已验证的 Release 资产大小为总量，在开始、每个数据块写入后及命中已校验缓存时回报下载字节数和确定型百分比。
- Android 设置页开始下载后显示不可在下载期间误关闭的模态进度框，包含目标版本、水平进度条及实时百分比；下载完成、失败或 Activity 销毁时关闭。
- 进度文字同时写回设置页状态，并补齐简体中文与英文资源。
- 下载来源、资产大小上限、SHA-256、APK 包名/版本/签名校验及系统安装确认路径均未修改。

## 测试

- 通过：`:app:testDebugUnitTest :app:compileDebugKotlin`，覆盖进度百分比的正常值、上下界及无效总量。
- 通过：`scripts/Verify-AndroidApp.ps1`，完成 Debug/Release/test APK 构建、单元测试及 Debug/Release Lint，共 123 项 Gradle 任务。
- 通过：`:app:assembleDebugAndroidTest`，真实进度弹窗测试已编译进 test APK。
- 失败后修正测试入口：`startActivitySync` 在该 MIUI 设备上等待主线程空闲超时，普通 target context 启动也未生成 Activity；改为 instrumentation 官方 `UiAutomation.executeShellCommand("am start -W …")` 启动测试 Activity，未修改产品逻辑。
- 通过：Redmi K40（ADB `61c9a964`）单项 instrumentation `UpdateProgressUiTests`，弹窗保持可见，水平进度条及可访问描述依次显示 0%、50%、100%；结果 `OK (1 test)`。
- 通过：真机测试结束后卸载测试 APK，恢复 SHA-256 为 `CEF0172E316B8F31C9A2B80389C83FDF51C6C86D29AB7CF73E8559FE1B77CEAB` 的正式 v0.2.5 APK；包管理器回读 versionName `0.2.5`、versionCode `7`、正式签名摘要 `[8ae58d98]`，且测试包不存在。

## 验收结果

通过。下载服务会持续回报真实字节进度，Android 界面持续显示目标版本、水平进度条和百分比，并在完成、失败或 Activity 销毁时关闭。单元测试、完整 Android 验证及真实设备界面测试均通过；未修改既有更新来源与安全校验。
