# P1-011 Samsung 真机兼容验收

日期：2026-09-21。结果：通过限定任务验收。设备为 Samsung Galaxy S23 Ultra 中国版 SM-S9180、Android 16/API 36，Windows 11 开发机通过有线网络接入同一 LAN。结论只覆盖当前 APK、Music 共享、小型合成文件、一次锁屏和两种显式停止路径，不代表完整 MVP。

## 构建与预检

- `scripts/Verify-AndroidApp.ps1` 完成 Debug/Release/test APK、JVM 单元测试及 Debug/Release Lint；123 个 Gradle 任务通过。
- 安装的 Debug APK SHA-256 为 `2550b45d2e2802e1075c8a5cc2fec8053933289a9c82cd53aeb13cc84696381b`。
- 所有文件访问、通知权限、电池优化用户豁免、`connectedDevice` 前台服务、8273 监听和服务持有的 WakeLock 均确认。
- 首轮手机位于另一子网，Windows 到 8273 超时；切换至同一 LAN 后 TCP 通过，再开始配对。未用手动端点掩盖网络阻断。

## 配对与文件链路

首次正式配对成功，P: 挂载且仅有一个受管 rclone。未枚举个人文件，只操作 `phonebridge-p1-011-*` 合成路径。

- 手机到 PC：1,048,576 字节，双方 SHA-256 `31912056f4b0fdff4170409cb51b36cc9ded5e16285a8d9bd87b8a23a7ff42b6`。
- PC 到手机：1,048,576 字节，手机独立 SHA-256 `7df0071ecd70494b2222f7b85d0913073015145485f06d84f8a2e591634fdfec`。
- P: 新建并重命名合成目录，手机端确认结果存在。

## 锁屏与退出

手机为 `mWakefulness=Dozing`、Keyguard `showing=true` 时，WakeLock、TCP 8273、P: 和单一 rclone 保持。跨三个健康检查周期后读取既有 1 MiB 文件散列不变；锁屏写入 2,097,152 字节文件，手机独立 SHA-256 为 `24a6fa606ef17bbc651dd624779cde0d78adf19deec824ceb3d5b29573c81fa4`。

从最近任务移除 PhoneBridge 后，共享标记立即变为 false，服务、通知、8273 与 WakeLock 消失；Windows 三个健康周期后 P: 和 rclone 消失。Android 一度保留应用进程，但系统明确标记为 `CACHED_EMPTY`、活动组件类型为 0、5 秒 CPU tick 增量为 0；这是系统管理的内存缓存，不是后台共享。依据：[Android 进程与应用生命周期](https://developer.android.com/guide/components/activities/process-lifecycle)。

再次手动开始共享时复用已保存身份，无需重新配对，P: 和单一 rclone 自动恢复，既有合成文件回读散列一致。通知栏“停止共享”再次清除服务、通知、8273、WakeLock、P: 与 rclone。

## 清理与限制

三星端三个合成文件和一个合成目录、本地合成载荷均已删除；Windows 客户端正常关闭。最终 P:、rclone、Windows 客户端进程、Android 服务、通知、8273 与 WakeLock 均不存在；配对记录按产品预期保留。

未验证数小时/数天锁屏、1/5/10/20 GB、大目录、IP 变化、路由器重连、PC 睡眠、手机/电脑重启、磁盘满或写缓存中断恢复。睡眠、重启和断网期间不要求继续传输；后续只验证中断安全、恢复状态和无僵尸资源。项目仅作第三方 App 分发，不安排品牌应用商店审核。

结构化证据：[preflight.json](p1-011/preflight.json)、[live-chain.json](p1-011/live-chain.json)。

## 下一步

唯一下一任务：[P1-012 Windows 托盘生命周期](../tasks/active/P1-012-windows-tray.md)。
