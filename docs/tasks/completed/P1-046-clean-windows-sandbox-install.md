# P1-046 — Windows Sandbox 干净首装验收

状态：已完成。日期：2026-09-23。

## 目标

在没有 WinFsp 和 PhoneBridge NG 的一次性 Windows 环境中验证正式安装器可以安装内置 WinFsp、客户端、固定 rclone 和许可材料，且不会自动启动客户端或创建盘符。

## 实现

- 新增 `scripts/New-CleanWindowsSandboxAcceptance.ps1`，生成禁网、只读交付映射和独立证据回写的 `.wsb` 配置。
- 修复含空格配置路径的启动参数、Windows PowerShell 5.1 路径兼容和 GUI 安装器提前返回问题。
- `Test-CleanWindowsInstall.ps1` 默认继续拒绝提升会话；仅显式允许 Windows Sandbox 固定管理员账户执行干净安装验收。

## 测试

全新 Windows 11 x64 build 26100 Sandbox 中，预检确认无 WinFsp/PhoneBridge、无待重启且安装器散列匹配。正式安装器退出 0；WinFsp、客户端、rclone、许可、NOTICE 和卸载项检查全部通过，客户端/rclone 未自动启动。

## 验收结果

任务通过，详细证据见 [P1-046 验收](../../audit/P1-046-VALIDATION.md)。隔离实例已销毁，宿主机无 PhoneBridge/rclone 进程或 P:。本任务没有验证普通用户 UAC 界面、手机连接或盘符链路。
