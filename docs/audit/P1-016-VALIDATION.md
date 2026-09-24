# P1-016 1 GB 双向传输与中断完整性验收

日期：2026-09-21  
结果：通过限定范围；发现并修复一个待写缓存恢复缺陷。

## 环境与边界

- Windows 11、WinFsp、项目固定 rclone 1.75.1、Release 构建。
- Samsung SM-S9180，Android API 36，用户已授权的备用测试机。
- 实际共享根为 `Music`；测试数据只放入 `Music/PhoneBridge-P1-016`。
- 样本均为精确的十进制 1,000,000,000 字节，不是 1 GiB。
- 文件通过 P: 的 WinFsp/rclone 挂载路径复制；本轮没有人工 Explorer 拖放验收。

## 双向完整性

手机端独立生成的样本 SHA-256 为 `0513693b8f68aa30e918794c05e923ac889e9574b8bb47b78e7081fbd59b69dd`。通过 P: 复制到 PC 后，长度仍为 1,000,000,000 字节，Windows 独立 SHA-256 相同；复制耗时 20,532 ms。

PC 流式生成样本 SHA-256 为 `4067b246c9621df11603a78e2b820f25b3499fc77b1be352e9c33db2a497ee60`。写入 P: 后等待手机端原子目标出现，再由 Android `sha256sum` 独立校验，长度和哈希均相同。随后安全卸载成功，P: 与 rclone 均退出，证明该次写回已完成。

## 中断缺陷与根因

首轮中断中，Wi-Fi 关闭后桌面端未崩溃、手机最终文件不存在、本机 1 GB 缓存仍在且元数据为 `Dirty: true`。但原 rclone 被健康监督器退出；网络恢复后新会话没有接管旧缓存，只能显式重试。日志与缓存目录确认每次会话使用 `:webdav:{随机后缀}`，因此“每设备缓存目录稳定”不足以保证 rclone 能重新发现上次会话的脏条目。

rclone 官方文档说明，后端配置覆盖会给远端名增加配置哈希后缀；其退出前完整确认 VFS 缓存已清空仍有公开问题。因此只依赖 `vfs/queue`/`vfs/stats` 不能证明磁盘上没有未提交的脏元数据。[rclone 配置与远端名](https://rclone.org/docs/#connection-strings-config-and-logging)、[RC vfs/stats](https://rclone.org/rc/#vfs-stats)、[退出前刷新 VFS 缓存问题](https://github.com/rclone/rclone/issues/1909)

## 修复

- 每次会话在受保护的随机会话目录写入短期 `rclone.conf`，固定使用 `phonebridge:` 远端；配置文件只包含当前会话的 obscured 密码并由只读句柄持有，卸载后精确删除。
- 每设备缓存根和跨进程租约保持不变；固定远端名使不同会话使用同一 `vfs/phonebridge` 与 `vfsMeta/phonebridge` 命名空间。
- 安全停止除核对队列、上传中、错误及空间状态外，还扫描当前固定命名空间的元数据；任何 `Dirty: true`、畸形、超大或重解析点元数据均失败关闭，不执行 `core/quit` 或强杀。
- 没有采用 `inUse == 0`：官方输出中的该值不等同于“没有待写文件”，实机也证明把它作为退出条件会阻止正常卸载。

## 修复后实机复验

第二次 1 GB 上传在本地缓存写完 644 ms 后关闭 Wi-Fi。35 秒后桌面端和 rclone 仍存活，P: 仍由原进程持有，手机最终目标不存在，固定缓存元数据明确为 `Dirty: true`。恢复 Wi-Fi 后原 rclone 在 24,994 ms 内提交最终文件，元数据变为 `Dirty: false`；Android 独立 SHA-256 与 PC 源相同。最终构建再次挂载并在 3,631 ms 内安全卸载，P: 与 rclone 均消失。

Release 构建为 0 warnings、0 errors；完整 Windows 测试 273/273 通过，其中 Mounting 为 38 项。结构化摘要见 [large-file-live.json](p1-016/large-file-live.json)。本地原始日志在 `.audit/p1-016/`，大样本和本任务缓存已删除。

## 未覆盖

- 人工 Explorer 拖放、播放和随机 Seek。
- 5/10/20 GB、PC 睡眠/唤醒、Windows 或手机重启、磁盘满、路由器重启。
- rclone 进程或 Windows 客户端在待写期间被强制终止后的跨进程恢复。
- 其他手机型号和其他共享根。

## 清理

手机测试目录、PC 两个 1 GB 样本及本任务 8 个缓存/元数据文件均已精确删除。用户明确停止共享后，Android 活动服务、活动通知、PhoneBridge WakeLock 和 8273 监听均不存在；应用进程仅处于系统 `top-sleeping` 状态且没有活动服务。P:、rclone 和桌面进程均不存在。
