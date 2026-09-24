# P1-042 正式 APK 迁移与覆盖升级验收

日期：2026-09-23。结果：通过。

## 已确认

- Samsung SM-S9180/API 36 上原安装包证书为本地测试证书；卸载后安装 P1-041 正式 APK，已安装证书 SHA-256 为 `66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4`，版本为 `0.1.0`。
- 首次跨证书迁移按 Android 规则清除应用私有数据并重新配对；Music 下的专用哨兵文件在卸载和正式安装后均保持原 SHA-256。
- 正式版本与 Windows 客户端重新配对后，唯一 P:、唯一 rclone 和唯一客户端成立。P: 读取哨兵、新建并重命名小文件成功，Android 独立 SHA-256 与 Windows 一致。
- Samsung 在 `Dozing` 且 Keyguard 显示时，P: 仍能读取两项测试文件；客户端、rclone、盘符和手机 8273 端口均保持健康。
- 使用完全相同的正式 APK 执行 `adb install -r` 返回 `Success`。覆盖前后 `ceDataInode` 均为 `121144`，Music 选择和两项文件均保留。
- 覆盖安装后启动共享，手机界面直接显示原配对电脑 `LY`，没有重新输入配对码；Windows 直接恢复 P:，正式证书、版本和两端文件 SHA-256 再次核对通过。
- 收尾后专用手机测试目录和临时 UI 辅助文件已删除；手机应用进程、8273 端口、Windows 客户端、rclone 和 P: 均不存在。

机器可读证据：`.audit/runs/P1-042/formal-install.json`、`mounted-file-operations.json`、`lockscreen-read.json`、`same-signer-upgrade.json`、`post-upgrade-link.json` 及对应 UI XML。

## 执行偏差与修正

- 为读取共享中的即时 Android 界面而制作的临时 `app_process` 辅助程序受运行环境限制未成功；它未修改产品。后续使用系统 `uiautomator` 和已显示界面取证，临时手机文件已删除。
- 首次小文件操作辅助命令把 `-or` 写入 `Test-Path` 参数表达式，产生一条非终止 PowerShell 错误；文件操作已完成，并由独立命令重新核对旧名不存在、Android 与 P: 哈希一致。
- 覆盖安装后的已安装 APK 拉取期间 USB ADB 短暂掉线；此时 P:、单一 rclone 和 TCP 8273 仍健康。重启本机 ADB daemon 后同一设备恢复，随后已安装 APK证书核验通过。
- 收尾脚本的目录判断引用和 PowerShell 保留变量名有误；停止、删除命令已经执行，随后用独立回读确认目录、手机进程、端口、P: 和两端进程均已清理。

## 未验证与边界

- 本任务只验证正式 APK迁移、锁屏小文件链路和同签名覆盖升级；没有重复大文件、大目录、断网、睡眠或重启矩阵。
- `.audit/delivery/p1-041-formal/output` 仍是隔离验证产物；当前唯一交付入口 `.audit/delivery/output` 仍为本地测试签名版本，尚未提升为正式交付。
- 只在 Samsung SM-S9180/API 36 验证正式迁移；其他厂商没有重复本项。
- 真实无 WinFsp 的干净 Windows 首装仍缺独立机器证据。
