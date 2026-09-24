# P1-011 Samsung 真机兼容验收

状态：已完成。P1-010 验收后的限定任务。

## 目标

在目标设备 Samsung Galaxy S23 Ultra 上验证正式 Android APK、锁屏共享、严格配对/挂载、基本文件操作、明确退出无后台和同设备恢复，不用 Redmi 结果代替三星兼容证据。

## 范围

先回读三星型号、Android/API、USB 调试、存储与账号隔离状态；使用合成测试文件和明确选定目录。验证电池优化授权、前台通知、锁屏读写、停止共享、最近任务移除后的服务/端口/WakeLock 清理，以及 Windows 严格身份连接与安全卸载。

## 不做什么

不发布或提交任何品牌应用商店，不测试 PC 睡眠期间继续传输，不做手机/电脑重启期间传输，不执行大文件中断矩阵，不读取、覆盖或删除个人文件，不启用厂商私有后台白名单。

## 涉及文件

以验收证据和必要的最小兼容修复为限；若三星暴露新问题，先记录复现与根因，再修改 Android/Windows 对应路径和文档。

## 实现

补齐退出语义：普通离开应用或锁屏继续共享；应用/通知停止共享或从最近任务移除会清除恢复标记，并结束 HTTPS、mDNS、WakeLock、MulticastLock 与前台服务。Samsung SM-S9180 使用正式 Debug APK 完成首次配对、P: 挂载、锁屏读写、再次共享和两种显式停止路径。

## 测试

前置回归运行 `scripts/Verify-AndroidApp.ps1`：Debug/Release/test APK、JVM 单元测试及 Debug/Release Lint 共 123 个 Gradle 任务通过。已确认 Samsung SM-S9180、Android 16/API 36，安装 SHA-256 匹配的 Debug APK；Music 共享、所有文件访问、通知、电池优化豁免、前台服务、8273 监听和 WakeLock 均通过。

首次网络预检发现手机与 PC 位于不同子网且 TCP 8273 不可达；用户切换到同一 LAN 后复测通过。首次配对、P:、单一 rclone、双向 1 MiB、目录新建/重命名、Dozing/Keyguard 下读取 1 MiB 与写入 2 MiB、手机独立散列、最近任务移除、再次手动共享和通知停止均通过。结构化记录见 [preflight.json](../../audit/p1-011/preflight.json)与 [live-chain.json](../../audit/p1-011/live-chain.json)。

## 验收结果

限定验收通过。最近任务移除和通知停止后，共享标记关闭，服务、通知、8273、WakeLock、P: 与 rclone 均消失；Android 保留的进程被系统标记为 `CACHED_EMPTY`，无活动组件且 5 秒 CPU 增量为零，不计作后台共享。再次手动共享复用既有配对并自动恢复 P:。本轮合成文件和目录已删除，Windows 客户端已关闭，配对记录保留。详见[验收报告](../../audit/P1-011-VALIDATION.md)。

未验证长时间锁屏、大文件、换 IP、路由器重连、PC 睡眠、两端重启或写缓存中断恢复；这些不属于本任务通过范围。唯一下一任务为 [P1-012 Windows 托盘生命周期](../active/P1-012-windows-tray.md)。
