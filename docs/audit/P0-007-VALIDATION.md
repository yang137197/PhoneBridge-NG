# P0-007 Android 支持版本与前台共享方案

2026-09-19。D-02 支持版本与前台共享方案已确定；构建、相关 API 26/36 应用断言通过。生命周期入口在最后重复的 UI 清理处退出 1，恢复设置已经独立验证，详见工具问题。实验对象为专用 AOSP 模拟器；不代表真机、Phase 0 或 MVP 全部通过。

## 方案与官方依据

保留 Android 8.0/API 26 的安装下限，compileSdk/targetSdk 提升到 36，以 Android 16 为当前现代系统验证基线。这是工程版本策略，不是对 API 26 以上每个系统与厂商 ROM 的兼容保证。Android 17/API 37 不在本轮测试范围；正式发布前另行核对其本地网络权限要求。[本地网络权限](https://developer.android.com/privacy-and-security/local-network-permission)

前台共享使用 `connectedDevice`：服务用于本地电脑通过网络访问设备，符合官方列出的外部设备网络交互场景。Manifest 声明 `FOREGROUND_SERVICE_CONNECTED_DEVICE`，沿用 mDNS 实际需要的 `CHANGE_WIFI_MULTICAST_STATE`，满足该类型的权限前提。`dataSync` 在 target 35+ 有后台累计时限及开机启动限制，不适合无限期等待电脑访问的共享会话。[前台服务类型](https://developer.android.com/develop/background-work/services/fgs/service-types)、[时限与官方测试方法](https://developer.android.com/develop/background-work/services/fgs/timeout)、[开机限制](https://developer.android.com/about/versions/15/behavior-changes-15)

系统重新创建仍在共享的 sticky 服务时，处理 null intent 并恢复实际服务；状态查询或明确停止后返回 NOT_STICKY 并释放服务。用户启用自启、系统允许且存储权限可用时才尝试开机共享；尊重强制停止，不承诺锁屏未解锁、Doze 或厂商省电限制下无条件恢复。[Service 生命周期](https://developer.android.com/reference/android/app/Service)、[Doze](https://developer.android.com/training/monitoring-device-state/doze-standby)、[后台受限状态](https://developer.android.com/topic/performance/background-optimization)

target 36 无法退出 edge-to-edge，界面增加系统栏和刘海区域 Insets，保留原始 padding。没有新增产品文案或 UI 重设计。[Android 16 行为变化](https://developer.android.com/about/versions/16/behavior-changes-16)

## 先复现再修复

在 API 36 安装 P0-006 APK（SHA-256 `9373fa5e1e1d807f941a011d49cdb7552c82deb840e88fe0d05ee4905994e898`）后实际复现两项问题：

- 终止正在共享的应用进程，系统创建新进程，但 HTTPS 未恢复。
- 启用官方 FGS 时限兼容开关并将 dataSync 时限缩短为 5 秒，应用进入后台后抛出 `ForegroundServiceDidNotStopInTimeException`。

基线结果见 [复现记录](p0-007/api36/baseline/foreground-baseline.json)。测试后恢复兼容开关与 device_config；预期崩溃单独归档，在修复测试前核对仅有该项预期崩溃并清理模拟器日志。没有把基线的预期崩溃当成修复版测试失败，也没有隐去原始证据。

## 实现与产物

固定原始上游 `a378fec40561a4d18be2334f4de92ee02a0e7d0c`，干净基线未修改，独立副本 `.audit/p0-007-worktree`。相对 P0-006 只修改构建 SDK、Manifest、Service、Activity Insets、BootReceiver 日志并增加 ServicePolicyTest，共 6 个文件。数据流/存储 6 个源文件按 LF 归一后相同，依赖声明未变。[范围审核](p0-007/scope-review.json)、[最终源码检查](p0-007/final-source-check.json)

| 产物 | SHA-256 |
| --- | --- |
| [累计补丁](../../android/patches/P0-007-android-support-policy.patch) | `6334bd8f5d40246f878b7a6f31c20348c5bfb13d8c53f496fd9439aa6d40e24a` |
| Debug APK，9,650,923 字节 | `3d96a0c4f5aecf1f68cedc522392e06f7e5197db78fba97e891e0f71c0e86100` |
| 原生测试 APK | `69625c054bcc796c708d72eec65df1082b61f5de7d1edac441705062a76a385b` |

补丁包含 P0-004–006，直接用于干净固定提交，不能叠加旧补丁。LF 格式、正向 apply-check、反向 apply-check、git diff --check 均通过，最终 APK/补丁散列重新核对一致。

使用已核验的 JDK 21、Gradle 9.3.1、AGP 9.1.1、Kotlin 2.2.10；新增官方 API 36 platform revision 2 与 AOSP x86_64 system image revision 2。`assembleDebug testDebugUnitTest assembleDebugAndroidTest lintDebug` 退出 0。20 个 JVM 单元测试通过；Lint 为 0 errors/126 warnings，相对 P0-006 移除两项 SDK 过旧警告，新增诊断为 0。[构建](p0-007/build.json)、[单元测试](p0-007/unit-results.json)、[Lint 差异](p0-007/lint-comparison.json)

## 运行证据

每次安装和测试前核对 APK SHA-256 与专用 AVD，实际系统属性见 [API 26](p0-007/api26/environment.json)、[API 36](p0-007/api36/environment.json)。全部文件摘要见 [manifest](p0-007/manifest.json)，原始日志保留在 `.audit/runs/P0-007`，[日志散列](p0-007/local-evidence-hashes.json)可供回查。

| 检查 | API 26 | API 36 |
| --- | --- | --- |
| 原生测试 | [16 通过、1 跳过](p0-007/api26/instrumentation-results.json) | [16 通过、1 跳过](p0-007/api36/instrumentation-results.json) |
| 启停/后台/小文件 | [11 项通过](p0-007/api26/compatibility-runtime.json) | [11 项通过](p0-007/api36/compatibility-runtime.json) |
| 共享选择失效 | [2 项通过](p0-007/api26/storage-scope.json) | [2 项通过](p0-007/api36/storage-scope.json) |
| 停止与退出 | [通过](p0-007/api26/cleanup.json) | [通过](p0-007/api36/cleanup.json) |

硬链接样本受模拟器平台限制而跳过，不计通过。11 项运行检查含三轮启停/后台恢复、1,000,000 字节 HTTPS PUT/GET 与 Android 独立散列、崩溃检查。合成数据 SHA-256 `1bf9fd40df4e777d63f6110f9137357b7f86c46f654d9cf6127b3275c071ec5b`。

API 36 的 [7 项生命周期断言](p0-007/api36/foreground-runtime.json)：实际前台类型 `0x10`；5 秒 dataSync 测试限制下后台共享 20 秒仍可用；真实进程终止后新进程恢复同证书 HTTPS（本次 2.66 秒）；手动停止后保持停止；自启关闭后的实际重启不启动；自启开启后的实际重启恢复 HTTPS；强行停止后实际重启仍保持 stopped。三次重启均断言 kernel boot ID 改变，自启测试不打开应用来触发恢复。缩短时限测试不是 6 小时持续运行验收。

退出后回读 ADB、进程、转发和盘符：测试 AVD 全部退出、无模拟器进程、实验转发移除、P: 不存在，缓存与样本保留。[主机核对](p0-007/host-cleanup.json)

## 测试工具问题

API 26 在读取启动后的设备信息、重新建立已存在转发时曾分别返回 ADB 255、1；同一 AVD 随后的相同读取/转发成功，未重启 AVD 或修改应用。具体内部原因未确定，不将这些失败计为通过。

首次 API 36 手动停止测试读到了早于当前 Activity 的 UI XML。uiautomator 的非零失败检查不足：AOSP 实现可以打印失败后返回，未生成新文件。入口改为唯一文件名并要求输出确认新 dump，再解析按钮；修改后真实停止通过。原失败的 stderr 未保留，不能断言具体是 idle timeout。[AOSP DumpCommand](https://android.googlesource.com/platform/prebuilts/fullsdk/sources/+/88c7ff1cd72d6305ec59f97aadc2198cc2dc3592/android-34/com/android/commands/uiautomator/DumpCommand.java)

本 AVD 的 shell 发送 BOOT_COMPLETED 被明确拒绝（SecurityException/uid 2000），因此改用实际系统重启，不提权或关闭广播保护。第一次立即重启未观察到共享恢复，未计通过。AOSP Android 16 PackageManager 对停止状态采用 10 秒延迟写盘；测试补充重启前状态断言、15 秒等待及日志，验证系统状态稳定后的行为。初次失败没有足够的重启前后状态记录，延迟写盘只是有依据的可能原因，不能写成已确认根因。[AOSP PackageManagerService](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/android16-release/services/core/java/com/android/server/pm/PackageManagerService.java)

最后一次重启验证的 7 项行为断言均已记录通过，但随后重复的 UI 清理抓取返回 `null root node`、退出码 0；新检查正确拒绝继续点击，整个入口因此退出 1。finally 已恢复时限、关闭测试自启并停止应用，随后独立读取配置、服务、包状态与无崩溃日志，全部符合预期。[独立回读](p0-007/api36/foreground-cleanup-verification.json)、[UI 命令结果](p0-007/api36/last-ui-command.json)。最终脚本移除多余的 UI 清理并补充配置回读；没有重新执行完整生命周期序列，不宣称该最终脚本整体运行通过。后续 storage_scope、stop_android 均实际执行成功。应用 APK 在上述工具修订中始终未变。

## 审核与未验证范围

已审核服务启动/停止和异常路径、null intent 处理、API 条件分支、原有权限/导出边界与 Insets；未改动数据存储或协议实现。新增原生测试分别验证空状态查询释放服务、真正前台通知的停止 PendingIntent，API 36 同时检查 Insets。

未验证 API 27–35 的本轮 APK、API 37、真机 Wi-Fi/mDNS、Doze/长时间锁屏、受 PIN 保护设备重启后首次解锁、PC 睡眠/换网、完整 Explorer 与 1/5/10/20 GB 矩阵。本轮 1 MB HTTPS 回归不能代替大文件；P0-005 的 100 MB 盘符测试属于前一版本。

用户确认备用机已清空并授权实验测试。只读 ADB 预检确认设备报告 Xiaomi M2012K11AC/alioth、Android 16/API 36、build BP2A.250605.031.A3、安全补丁日期 2025-10-01，见 [预检](p0-007/phone-preflight.json)。未验证 ROM 来源，不根据型号推定官方固件或厂商兼容。本任务未安装真机 APK；测试期间曾短暂断开，用户重连后已再次读取 alioth/API 36，连接有效。

126 项既有 Lint 警告及正式配对、凭据保护、TLS 身份绑定、默认安全模式删除等仍待处理。实验副本仍有上游 HTTP 降级、硬编码文案和非目标远程功能，不能作为正式 NG 应用分发。下一任务仅为 P0-008：在已授权空白备用机完成同 Wi-Fi 的限定文件链路验证。
