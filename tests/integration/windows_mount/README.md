# P1-002 实验入口

先在根目录运行 `scripts/Verify-Windows.ps1`；构建不会连接手机。`diagnostic_input.py` 无需手机，测试输入拒绝与 120 秒无输入退出。

`real_device_mount.py` 仅用于会话中已授权、已清空的备用 Redmi K40，需当前进程配置 PHONEBRIDGE_TEST_SERIAL、PHONEBRIDGE_TEST_LOCAL_IP、PHONEBRIDGE_TEST_PHONE_IP；既有 P0-009 APK、WinFsp 和固定 rclone 必须已经存在。入口核对型号/API、两 APK 和 CA 散列、地址、盘符与原 P0-010 样本。不得为复现删除或覆盖旧样本。

脚本通过已有测试宿主开启真实共享，C# 负责实际挂载；USB 不搬运测试文件。JSON 输入中的 CA 与内存临时密码只经 stdin，输出只有固定事件、状态、PID、退出码及合成样本散列。不要把输入重定向到持久文件或把真实凭据放入终端命令。

只读测试用目录 `Music/P0-010-84926bfb5cd4`，试建唯一新文件并要求拒绝。若意外成功则保留空文件证据、立即判失败；不删除已有数据。`wrong-ca.pem` 是有效期 2026–2046 的纯合成公有证书，私钥已丢弃，只用于拒绝测试，禁止作为正式配对材料。

新包装只替换旧测试宿主的停止标记发送：Android 按文件存在退出并删除标记，发送 touch 后等待最终 OK；不能回读已消费文件判定失败。原 P0 脚本不变。结束时核对样本/元数据/身份、共享、盘符和转发；中间单步通过不能替代入口 exit 0。

结果见 [验收报告](../../../docs/audit/P1-002-VALIDATION.md)。诊断程序是机器 JSON 测试入口，120 秒后退出，不是供用户保持长期挂载的客户端。
