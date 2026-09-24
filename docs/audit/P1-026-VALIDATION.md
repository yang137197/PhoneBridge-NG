# P1-026 Android 重启恢复验收

日期：2026-09-22。结果：通过。设备为 Samsung SM-S9180/API 36，使用 Android `0.1.0` 和 P1-024 Windows 最终安装版；重启期间没有文件传输，不要求保持共享。

## 重启前基线

手机处于已解锁亮屏状态，Music 共享由 ADB 自动启动。Windows 安装版 6.192 秒挂载 P:，只有一个 `PhoneBridge.Desktop` 和一个 rclone。P: 顶层包含 2 项，规范清单 SHA-256 为 `EEFDA3D67FFE99879C960245A5CB20F0DC6AE82BC55551B6C3648B2C64E02D94`。选取一个 36 字节非空既有文件作为只读样本，SHA-256 为 `1193D1082C29D7BC31D949054301393B5420E4473D4DB779DBD44160C95B0359`。

两份 Windows `.pairing` 记录与 `.store.lock` 的散列同时保存。完整相对路径只保存在被忽略的 `.audit/runs/P1-026/preflight.json`，没有写入产品日志或提交文档。预检没有修改手机文件；确认前安全停止共享并清除全部运行资源。

## 实际重启与恢复

用户明确确认后执行一次正常 Android 重启。ADB 观察到设备断开和重新连接；`sys.boot_completed=1` 用时 52 秒。内核 boot ID 从 `e72751d0-6097-48fd-b5bd-60705aa9ca89` 变为 `31f9cff1-24c6-4b45-bad6-9800a2b03f9b`，证明不是 Activity 或 ADB 重启。用户 0 状态为 `RUNNING_UNLOCKED`，`deviceLocked=0`。

重启后 PhoneBridge 没有自行后台共享，符合用户明确启动的产品边界；Music 选择仍在。首次解锁后由 ADB 自动打开应用并点击开始共享，前台服务和 8273 监听建立，界面恢复 `LY` 配对和安全模式。

Windows 最终安装版以 `--startup` 进入现有唯一设备恢复路径，7.289 秒挂载 P:。结果如下：

- 桌面客户端 1、rclone 1、P: 1，没有重复实例或盘符。
- 顶层项目数仍为 2，规范清单 SHA-256 与重启前完全相同。
- 36 字节样本仍存在，长度和 SHA-256 与重启前完全相同。
- 两份 `.pairing` 记录及 `.store.lock` 散列均未变化。

结构化结果保存在 `.audit/runs/P1-026/postreboot.json`。Windows 客户端在活动连接丢失时的安全卸载与同身份自动恢复已由 P1-010/P1-011 真机直接验证；本任务补足实际 Android 重启、凭据/目录持久化和重启后重新共享恢复，没有重复制造文件传输中断。

## 清理

验收后自动停止共享并等待 P: 与 rclone 正常消失，再结束空闲桌面客户端。最终 Android 前台服务、8273、PhoneBridge WakeLock、P:、rclone 和 Windows 客户端均不存在。产品代码没有变化，因此未重复 P1-024 已通过的 Windows 279/279 和 Android 123 个 Gradle task。
