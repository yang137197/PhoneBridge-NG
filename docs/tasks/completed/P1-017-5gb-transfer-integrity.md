# P1-017 5 GB 双向传输完整性

状态：已完成。验收日期 2026-09-21。

## 目标

在 Samsung 备用机的隔离共享目录中验证精确 5,000,000,000 字节手机到 PC、PC 到手机的持续传输、独立 SHA-256、缓存清零与安全卸载。

## 范围

沿用 P1-016 的固定 `phonebridge:` 缓存命名空间，在 `Music/PhoneBridge-P1-017` 完成一次干净双向传输；记录长度、两端 SHA-256、耗时、桌面响应、rclone 数量、手机原子目标和卸载结果。

## 不做什么

未重复 Wi-Fi 中断，未测试 10/20 GB、PC 睡眠、重启、磁盘满、播放、随机 Seek、多设备并发或安装器。未执行人工 Explorer 拖放。

## 涉及文件

- `tests/integration/large_file/New-DeterministicFile.ps1`
- `tests/integration/large_file/android-create-segmented-file-5gb.sh`
- `tests/integration/large_file/README.md`
- `README.md`、`docs/TESTING.md`
- `docs/audit/P1-017-VALIDATION.md`、`docs/audit/p1-017/large-file-live.json`

## 实现

修正 PowerShell 生成器超过 2 GiB 时的重载选择；新增只允许 P1-017 固定 Music 目标的 Android 5 GB 生成器，并用十进制字符串偏移避免 Android shell 32 位乘法溢出。没有修改产品运行代码或协议。

## 测试

- 手机到 PC：5,000,000,000 字节，双方 SHA-256 `041eb9931a3d9157533a743bb9cad82d1089e38a04adf5d38e81f1e9cea488a5`，91,711 ms。
- PC 到手机：5,000,000,000 字节，双方 SHA-256 `e2558bd9d1fb5616b0a2370513335cf16d2f781039d6bc0f02c2a38df57a991b`；手机最终文件在临时上传完成后原子出现。
- 传输期间桌面端响应正常、rclone 始终为一个；最终缓存 `Dirty: false`。
- 安全卸载 3,789 ms，P: 与 rclone 均退出。
- Release 构建 0 warnings/0 errors；Windows 273/273 测试通过。

## 验收结果

通过限定范围。5 GB 双向长度、独立哈希、原子提交和安全卸载成立；测试数据及缓存已清理。用户停止共享后，Android 活动服务、活动通知、PhoneBridge Wake Lock 和 8273 监听均不存在；系统仍保留无活动共享组件的应用进程。
