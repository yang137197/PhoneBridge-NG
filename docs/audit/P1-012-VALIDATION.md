# P1-012 Windows 托盘生命周期

日期：2026-09-21。结果：通过限定任务验收。结论覆盖当前 Windows 11 开发机、Samsung SM-S9180/API 36、一次安全模式 P: 挂载以及窗口隐藏、托盘恢复和明确退出，不代表完整 MVP 或多设备同时挂载。

## 实现

- WPF 应用改为显式退出生命周期，并以 .NET 自带 Windows Forms `NotifyIcon` 提供单一托盘图标、双击/菜单打开和明确退出；没有增加第三方运行时包。
- 窗口右上角关闭在托盘可用时只隐藏原窗口；托盘恢复继续使用原窗口、原 `ConnectionClient` 和原挂载。
- 状态按错误、已挂载、连接中、已发现、离线的固定优先级映射，文本来自 zh-CN/en-US 资源。
- 明确退出停止恢复意图并复用既有安全卸载；卸载未确认时恢复窗口并保留进程和缓存归属。

## 自动验证

`scripts/Verify-Windows.ps1` 完成 locked restore、Release 构建和测试，构建为 0 warnings/0 errors。共 227 项全部通过：Discovery 54、Mounting 36、Pairing 50、Connection 18、Credentials 63、Desktop 6。

Desktop 测试覆盖五种状态优先级和窗口关闭策略。既有 Mounting 回归继续覆盖待上传未确认时不强杀、卸载失败保留所有权、盘符仍在时不报告已停止。结构化记录见 [windows-tests.json](p1-012/windows-tests.json)。

## 真机与托盘验收

Samsung 开始共享并恢复既有配对后，P: 挂载，桌面 PID 为 30412，只有一个 rclone PID 28248，界面显示“手机会话正常”和 `P:\ · SM-S9180 · 安全模式`。

点击窗口关闭后，用户确认托盘图标仍可见；桌面仍为原 PID，主窗口句柄消失，P: 与原 rclone 保持。通过托盘恢复后仍为同一桌面 PID、同一 rclone 和同一 P:，没有重复实例。托盘明确退出后，桌面进程、rclone 均为 0，P: 不存在。随后手机停止共享，Android `SharingService` 不存在且 PhoneBridge 共享 WakeLock 未持有。结构化记录见 [tray-live.json](p1-012/tray-live.json)。

## 多设备边界

当前版本不支持多设备同时共享到同一 Windows 客户端。发现列表和受保护配对记录可以包含多台手机，但单实例桌面只有一个 `ConnectionClient` 和一个 `MountManager`；活动挂载存在时新的连接会被拒绝。因此当前并发活动挂载数为一。

## 未验证

未逐项肉眼检查离线、已发现、连接中和错误图标变体；未在英文 Windows 布局验收菜单；未制造真实待上传缓存来观察退出失败界面。待上传不强杀只由自动测试证明。未验证开机自启、PC 睡眠、长期运行、大文件中断恢复或同时多设备挂载。

## 下一步

唯一下一任务：[P1-013 Windows 开机启动生命周期](../tasks/active/P1-013-windows-autostart.md)。
