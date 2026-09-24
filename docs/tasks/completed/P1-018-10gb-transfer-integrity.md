# P1-018 10 GB 双向传输完整性

状态：已完成。验收日期 2026-09-22。

## 目标

在 Samsung 备用机的隔离共享目录中验证精确 10,000,000,000 字节手机到 PC、PC 到手机的持续传输、独立 SHA-256、原子提交、缓存清零和安全卸载。

## 范围

沿用确定性流式 PC 样本、受限 Android 分段样本、固定 `phonebridge:` 缓存命名空间和手机端独立哈希；记录两端长度、哈希、客户端响应、rclone 数量、暂存提交、卸载和清理。

## 不做什么

未测试 20 GB、人工 Explorer 拖放、播放、随机 Seek、磁盘满、多设备并发或安装器。测试期间发生的 PC 重启只作为恢复观察，不把界面超时记为通过。

## 涉及文件

- `android/app/src/main/java/org/phonebridge/ng/AuthorizedServer.kt`
- `android/app/src/test/java/org/phonebridge/ng/UploadLengthTests.kt`
- `tests/integration/large_file/android-create-segmented-file-10gb.sh`
- `tests/integration/large_file/README.md`
- `ARCHITECTURE.md`、`README.md`、`docs/TESTING.md`
- `docs/audit/P1-018-VALIDATION.md`、`docs/audit/p1-018/large-file-live.json`

## 实现

新增固定 P1-018 目标的 10 GB Android 样本脚本。将 Android PUT 上限从 8 GiB 修正为十进制 20,000,000,000 字节；上传正文持续到达时每 30 秒刷新 120 秒无进展截止时间。未修改协议、rclone 或 WinFsp 路线。

## 测试

- 手机到 PC：精确 10,000,000,000 字节，双方 SHA-256 `b535a5b0072feff9259a8af8e60c68cea180679c8d8abb58ab36dae2f22f336c`，237,373 ms。
- PC 到手机：精确 10,000,000,000 字节，双方 SHA-256 `3ba013d4185d6b0448b49222e91b9bebe1377b90ec5b2fd1c108e69b1491a224`，最终缓存 `Dirty: false`。
- Android 123 个 Gradle task 成功；17/17 JVM 单元测试和两套 Lint 通过。
- 安全卸载、合成数据与缓存精确清理、停止共享后的资源回读通过。

## 验收结果

通过限定范围。10 GB 双向长度、独立哈希、Android 原子最终文件、缓存清零和安全卸载成立。PC 重启后的脏缓存成功恢复并提交，但首次连接错误显示超时，作为 P1-019 单独修复。
