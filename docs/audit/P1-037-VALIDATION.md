# P1-037 干净 Windows 首装验收入口

日期：2026-09-23。结果：工具通过限定验收；真实无 WinFsp 首装尚未执行。

## 已确认

- `Test-CleanWindowsInstall.ps1` 默认仅预检；只有显式 `InstallAndVerify` 且全部前置条件通过才运行安装器。
- 预检覆盖 Windows 11 x64、普通用户会话、待重启、WinFsp/PhoneBridge 已存在、安装器并发、manifest、安装器普通文件名和 SHA-256。
- 安装模式使用 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010`。Inno Setup 官方文档确认 `/NORESTART` 禁止自动重启，`/RESTARTEXITCODE` 可在成功但需要重启时返回指定值；Windows 官方将 3010 定义为成功且需要重启。
- 安装后核对 WinFsp 注册位置与 `winfsp-x64.dll`、客户端、rclone 固定散列、GPL/NOTICE、卸载项，以及静默安装期间没有启动客户端或 rclone。
- 当前主机真实预检检测到已安装 WinFsp 和 PhoneBridge，返回 2。即使选择安装模式，安装器进程仍为 0，安装结果字段为 null，交付安装器和已安装客户端散列不变。
- 篡改安装器用例返回 2 并报告 `installer-hash-mismatch`；证据目录与交付目录重叠用例在目录创建前返回 1。

机器可读证据：`.audit/runs/P1-037/clean-windows-runner-verification.json`。依据：[Inno Setup 命令行参数](https://jrsoftware.org/ishelp/topic_setupcmdline.htm)、[Microsoft 系统错误码 3010](https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--1700-3999-)。

## 未验证与边界

当前主机没有可用 Windows Sandbox、Hyper-V PowerShell 或其他干净 Windows 测试面，并且日常环境已经安装 WinFsp。因此没有触发真实 UAC、运行内置 MSI或验证安装后 WinFsp；本任务没有卸载现有驱动来伪造环境。

真实通过必须在一台没有 WinFsp 和 PhoneBridge、无待重启的 Windows 11 x64 普通用户会话执行 `InstallAndVerify`，取得退出码 0 或 3010，并由生成的 JSON证明所有安装后检查通过。该验收只关闭安装分支证据，不重复手机配对、盘符或文件传输测试。
