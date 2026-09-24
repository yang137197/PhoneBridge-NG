# P1-019 重启后大脏缓存恢复挂载状态验收

日期：2026-09-22  
结果：通过限定范围。

## 根因与修复

P1-018 的 10 GB 现场记录证明数据可恢复，但首次连接在固定 30 秒后误报挂载超时。P1-019 用 Samsung 4 GB 可控脏缓存复现两次后确认：rclone 会在 RC 或 WinFsp 盘符可用前开始恢复写回，且恢复期间部分 VFS/远端探测会等待上传完成。仅增加超时或只调整盘符检查顺序均不能解决。

最终实现先从受设备缓存租约和 ACL 保护的稳定 `vfsMeta/phonebridge` 读取 `Dirty` 状态。发现历史待提交数据即进入 `RecoveringWrites`；RC 可响应时以 `core/stats` 字节数确认进展，每个进度探测最多等待 2 秒。普通启动仍限 30 秒，恢复另有 2 分钟无进展期限及 24 小时绝对上限。恢复完成后仍执行 PID、盘符、TLS/凭据和远端列表验证，不降低身份门槛。停滞、总时限或取消保留缓存及所属进程，不强杀待提交数据。

rclone 官方文档说明 `--vfs-cache-mode writes` 的未完成文件会在使用相同参数的下一次运行继续上传；RC `core/stats` 明确定义当前传输的累计及单文件字节字段。本任务用现场状态确认了这些机制在固定 rclone 1.75.1 下的实际顺序：[VFS 缓存](https://rclone.org/commands/rclone_mount/#vfs-file-caching)、[RC core/stats](https://rclone.org/rc/#core-stats-returns-stats-about-current-transfers)。

## 自动验证

固定 .NET SDK 10.0.401 的 Release 还原、构建及全部测试通过：0 warnings、0 errors，6 个测试程序集共 279 项通过。挂载测试包含持续进展超过启动期限、恢复期间无盘符、真实停滞、绝对上限、取消、身份失败、同会话 Mounted、单一所有者和最终清理；rclone 进度 JSON 对缺失、负数及错误类型失败关闭。

## Samsung 真机验收

环境为 Samsung SM-S9180、Android 16/API 36、Music 共享根、WinFsp 和固定 rclone 1.75.1。PC 经 P: 写入精确 4,000,000,000 字节测试文件，在本地元数据确认为 `Dirty: true` 后强制结束唯一所属 rclone，模拟断电留下的稳定缓存。

最终轮在 `2026-09-22T03:23:02.628Z` 开始下一连接，约 2.006 秒后记录 `Recovering`；此时 P: 尚未出现，系统只有一个 rclone。恢复跨过旧 30 秒边界，期间没有 Timeout 或 Stopping。`2026-09-22T03:24:18.906Z` 同一连接直接记录 Mounted，总耗时约 76.278 秒，没有第二次连接或重复进程。

手机最终文件长度为 4,000,000,000，Android 独立 `sha256sum` 与本地一致：`ddd45e35df0b676747319e4da247e7dc78b53cb99cba1411f1db51ebf4202efb`。前两轮失败现象和最终成功时间线保留于[现场证据](p1-019/recovery-live.json)。

## 清理与边界

三个复现文件均只位于专用 `Music/PhoneBridge-P1-019`。验收后手机任务目录、本地 4 GB 源、对应 VFS 数据和元数据均删除；P:、rclone、桌面进程、Android 前台共享服务均不存在。

未测试 20 GB、真机持续两分钟无进展、磁盘满、多设备、安装器或 PC 睡眠。重启或睡眠期间不要求继续传输，只要求恢复后的状态和数据正确。唯一下一任务为 P1-020 20 GB 双向边界完整性。
