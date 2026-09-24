# P1-037 — 干净 Windows 首装验收入口

状态：已完成工具。日期：2026-09-23。

## 目标

为全新、未安装 WinFsp 和 PhoneBridge NG 的 Windows 11 x64 环境提供一次性、失败关闭的首装验收入口，验证当前安装器确实触发内置 WinFsp 安装分支并留下可审计结果。

## 范围

新增一个 PowerShell 验收脚本：先核对干净环境、交付 manifest 和安装器散列；只有显式选择安装模式且所有前置条件通过才运行当前安装器；安装后核对 WinFsp、客户端、rclone、许可、卸载项、进程和重启返回码。

## 不做什么

不在当前已安装 WinFsp 的开发机卸载驱动或伪造干净环境，不自动启用 Windows Sandbox/Hyper-V，不重启电脑，不连接手机，不测试文件挂载，不发布产物。

## 涉及文件

- `scripts/Test-CleanWindowsInstall.ps1`：首装预检、执行和证据输出。
- `docs/LOCAL_DELIVERY.md`：干净环境验收操作边界。
- 本任务、验收记录和机器可读证据。

## 实现

- 新增 `Test-CleanWindowsInstall.ps1`，默认只执行预检，只有显式 `InstallAndVerify` 才能运行安装器。
- 预检要求 Windows 11 x64、普通用户会话、无待重启、无 WinFsp、无 PhoneBridge 安装/进程、Windows Installer 空闲，并核对当前 manifest 和安装器 SHA-256。
- 证据目录必须与交付目录分离，避免测试日志污染可交付制品；manifest 中安装器字段只允许普通文件名，不能跳出交付目录。
- 安装模式固定使用 Inno Setup 的静默、不自动重启和 `3010` 重启返回码；安装后核对 WinFsp 注册和 DLL、客户端、固定 rclone 散列、GPL/NOTICE、卸载项以及未启动客户端/rclone。
- JSON证据不记录用户名、计算机名或完整本机安装路径；Inno Setup 原始日志独立保存在操作者选择的证据目录。

## 测试

- PowerShell 语法解析通过。
- 当前 Windows 11 x64 主机的真实预检识别 `winfsp-already-present` 和 `phonebridge-already-installed`，返回 2；当前安装器散列与 manifest 一致。
- 在当前主机显式选择 `InstallAndVerify` 仍在预检阶段返回 2：安装器进程前后均为 0，安装证据为 null，交付安装器和已安装客户端散列未变化。
- 使用四字节伪安装器和真实 manifest 的负向用例识别 `installer-hash-mismatch`、返回 2，未进入安装。
- 把证据目录指向交付目录内部时返回 1，目录未创建。
- 机器可读证据：`.audit/runs/P1-037/clean-windows-runner-verification.json`。

## 验收结果

工具通过当前可执行范围的限定验收。当前主机已有 WinFsp 和 PhoneBridge，且没有 Windows Sandbox/Hyper-V 测试面，因此没有运行真实干净首装，也没有改变本机驱动、安装状态或进程。P1-037 完成的是可重复验收入口，不关闭 P1-034 记录的真实环境证据缺口；未新增产品架构决定。
