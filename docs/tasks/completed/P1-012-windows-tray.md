# P1-012 Windows 托盘生命周期

状态：已完成限定任务。2026-09-21。

## 目标

为现有 WPF 客户端加入可验证的系统托盘生命周期，让用户能隐藏/恢复窗口、识别连接状态并明确退出；退出继续遵守安全卸载和待上传保护。

## 范围

实现单一托盘图标、打开主窗口和退出动作，映射离线、已发现、连接中、已挂载和错误状态；明确窗口关闭与退出的行为，保证不会创建重复窗口、重复 rclone 或重复盘符。退出必须先停止自动恢复并走现有安全卸载路径。

## 不做什么

不做 Windows 开机自启、安装器、自动更新、通知中心集成、协议改动、Android 改动、PC 睡眠矩阵或大文件中断恢复。

## 涉及文件

限定在 `windows/src/PhoneBridge.Desktop`、对应资源/测试和项目文档；不更换 WPF 或引入独立后台服务。

## 实现

使用 .NET 自带 Windows Forms `NotifyIcon` 作为 WPF 托盘宿主，不增加第三方运行时包。应用改为显式退出模式；普通关闭隐藏窗口，双击图标或菜单恢复同一窗口，菜单明确退出。状态映射来自现有连接状态，所有文字来自 zh-CN/en-US 资源。退出继续调用现有 `ConnectionClient.DisposeAsync`，卸载失败时恢复窗口并保留挂载所有权。

## 测试

`scripts/Verify-Windows.ps1` 的 Release 构建 0 warnings/0 errors，227 项测试全部通过，其中新增 6 项托盘策略测试。Samsung SM-S9180 实际挂载后完成窗口隐藏、托盘恢复和明确退出；同一桌面 PID、P: 与单一 rclone 在隐藏/恢复期间保持，退出后全部消失。Android 随后停止共享，服务与 WakeLock 均消失。

## 验收结果

限定验收通过。当前可发现并保存多台设备，但单实例桌面只拥有一个 `ConnectionClient` 和一个 `MountManager`，不能同时挂载多台手机。未逐项目视检查所有状态图标、英文 Windows 布局或真实待上传退出失败界面；待上传不强杀由既有 MountManager 自动测试覆盖。证据见 [P1-012 验收](../../audit/P1-012-VALIDATION.md)。唯一下一任务为 [P1-013 Windows 开机启动生命周期](../active/P1-013-windows-autostart.md)。
