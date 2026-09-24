# P1-008 Windows 首次配对与只读连接验收

日期：2026-09-21。结果：通过限定任务验收。该结论不代表整个 MVP 或正式发行可用。

## 已完成

- 正式 v3 mDNS 模型、TCP PAKE、严格 CA/SAN HTTPS、DPAPI Pending/Active 生命周期和只读 MountManager 已由 Connection 组合层接入 WPF。
- WPF 提供中英文设备、短码、配对/连接/取消、打开/卸载/撤销入口，网络与密码学不阻塞 UI；关闭窗口等待所属挂载清理。
- 真机发现的盘符漂移：挂载成功后 `FillDrives` 把仍被占用的首选盘符换成其他盘符。修复为挂载状态不刷新可用盘符，只在确认卸载后刷新。

## 自动验证

最终 `Verify-Windows.ps1` 已通过 locked restore、Release build（0 warnings、0 errors）及 215 项测试：54 发现、63 凭据、50 配对、33 挂载、15 Connection 网络组合层。TRX 和日志保存在 `.audit/p1-008/windows-final/` 与 `.audit/p1-008/windows-final.log`，摘要见 [tests.json](p1-008/tests.json)。

盘符漂移修复后再次执行 `Start-WindowsPreview.ps1 -NoLaunch`，Release 构建为 0 warnings、0 errors；更新后的实施文件散列见 [implementation.json](p1-008/implementation.json)。

## 真机链路

备用 Redmi K40（Android 16/API 36）运行 `org.phonebridge.ng` 0.1.0-dev，设备回读 APK 与本地构建 SHA-256 一致。用户仅在手机和本地 WPF 输入/批准短码，短码未进入聊天、命令行或审计文件。

WPF 发现 `M2012K11AC` 的 v3 广告，完成新凭据配对和 session 核对后挂载 `P:`，界面明确显示只读。`Music/P0-010-84926bfb5cd4` 的四个测试项目可列出；`phone-origin.txt` 为 49 字节，SHA-256 为 `7374185329B3A98D96E953D8ED38BFE1C528CA5DD6B8298BDEAD468F65C5251E`。没有向手机写入新内容。

手动卸载后盘符消失且 rclone 进程数为 0。已保存配对无需新短码即可重新连接。初次实测出现 `P:` 后选择漂移到 `E:`，保留为失败证据；修正后真机复验为 `P:` → 卸载 → `P:`，`E:` 未出现且每次仅一个 rclone。最终从窗口关闭时，PhoneBridge Desktop 与 rclone 进程均为 0，`P:`/`E:` 均不存在。手机测试共享已停止，配对记录保留。结构化结果见 [live-chain.json](p1-008/live-chain.json)。

## 未验证与已知限制

- 未验证英文可见布局、IPv6、真机撤销/离线撤销、Windows 或 Android 重启、网络中断/IP 变化、睡眠唤醒、自动重连、托盘、自启和安装器。
- 本任务只读，没有执行安全模式上传、重命名、新建目录或删除确认；未重复 100 MB，也未执行 1/5/10/20 GB 或大目录性能测试。
- 合成网络测试机的 `TcpListener.Start` 约 30 秒现象仍无根因证据；它没有出现在本次真机用户链路中，未修改防火墙或安全设置。

## 下一步

后续任务 [P1-009 安全模式写入与删除保护](../tasks/completed/P1-009-safe-writes.md)现已完成；当时的下一步是先定义服务端权限与提交边界，再用可丢弃数据验证上传、新建目录、重命名和删除确认。
