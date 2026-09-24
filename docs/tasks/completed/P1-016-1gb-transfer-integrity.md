# P1-016 1 GB 双向传输与中断完整性

状态：已完成。验收日期 2026-09-21。

## 目标

在 Samsung 备用机的可丢弃共享目录中验证 1 GB 手机到电脑、电脑到手机的真实挂载链路、独立大小与 SHA-256，并确认网络中断不会导致客户端崩溃、半文件被误报成功或失去缓存归属。

## 范围

核对两端空间和缓存边界；使用不同区段内容不同的确定性 1,000,000,000 字节样本完成双向传输；再执行一次 PC 到手机上传期间 Wi-Fi 中断，记录桌面端、P:、rclone、手机最终目标和 VFS 元数据，修复已复现的单一缓存恢复根因并重跑同一 1 GB 场景。

## 不做什么

未要求断网期间继续传输；未扩大到 5/10/20 GB、PC 睡眠、重启、磁盘满、安装器、多设备并发或真实用户文件。未执行人工 Explorer 拖放、视频播放或随机 Seek。

## 涉及文件

- `windows/src/PhoneBridge.Mounting/MountResources.cs`
- `windows/src/PhoneBridge.Mounting/RcloneMountSession.cs`
- `windows/tests/PhoneBridge.Mounting.Tests/MountTests.cs`
- `tests/integration/large_file/`
- `ARCHITECTURE.md`、`DECISIONS.md`、`README.md`、`docs/TESTING.md`
- `docs/audit/P1-016-VALIDATION.md`、`docs/audit/p1-016/large-file-live.json`

## 实现

新增确定性流式大文件生成工具和 Android 分段样本生成脚本。首轮中断复现出实际缺陷：磁盘元数据仍为 `Dirty: true`，但临时 `:webdav:` 后端的会话后缀变化使恢复会话无法接管旧缓存。

修复后，rclone 使用受当前用户 ACL 保护、卸载后精确删除的短期配置文件及稳定 `phonebridge:` 远端名；安全停止额外检查固定命名空间中的 VFS 元数据，脏、畸形、超大或重解析点元数据均禁止退出。既有每设备缓存、租约、RC 认证和 TLS 边界不变。

## 测试

- 手机到 PC：1,000,000,000 字节，双方 SHA-256 `0513693b8f68aa30e918794c05e923ac889e9574b8bb47b78e7081fbd59b69dd`。
- PC 到手机：1,000,000,000 字节，双方 SHA-256 `4067b246c9621df11603a78e2b820f25b3499fc77b1be352e9c33db2a497ee60`。
- 修复后中断：离线时桌面/rclone/P: 保留、最终目标不存在、元数据为脏；恢复后 24,994 ms 内由原 rclone 提交，手机独立哈希一致，元数据转为干净。
- 最终构建干净卸载 3,631 ms，P: 与 rclone 均退出。
- Release 构建 0 warnings/0 errors；Windows 273/273 测试通过。

## 验收结果

通过限定范围。实测证明 1 GB 双向完整性、一次 Wi-Fi 中断不崩溃/不误报、原缓存恢复和安全卸载成立。测试文件及缓存已清理；用户停止共享后，Android 服务、活动通知、WakeLock 和 8273 监听均不存在，Windows 盘符、rclone 和桌面进程也均不存在。
