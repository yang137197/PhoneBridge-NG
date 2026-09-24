# P1-046 Windows Sandbox 干净首装验收

日期：2026-09-23。结果：通过。

## 范围与环境

- Windows 11 专业版宿主机启用 Windows Sandbox；最终隔离系统为 Windows 11 x64 build 26100。
- Sandbox 禁用网络、vGPU、剪贴板、音视频和打印机重定向；正式交付、脚本和启动脚本均只读映射，只有证据目录可写。
- 隔离环境预检确认无 WinFsp、无 PhoneBridge NG、无待重启状态，正式安装器 SHA-256 为 `02B63BE25590FD1F86BC661CF15B7A3941E2FE13440920AAA0F850B4E7961262`，与交付清单一致。
- Windows Sandbox 固定 `WDAGUtilityAccount` 为管理员；验收入口只在显式参数且用户名精确匹配时允许该环境，其他提升会话仍失败关闭。本次不声称独立验证了普通用户 UAC 界面。

## 结果

正式安装器退出码为 0，无需重启。安装后 WinFsp 注册项及 `winfsp-x64.dll`、PhoneBridge 客户端、固定 rclone 散列、GPL、NOTICE 和当前用户卸载项全部通过；安装期间没有启动 PhoneBridge 客户端或 rclone。Inno Setup 日志以 `Installation process succeeded` 和 `Need to restart Windows? No` 结束。

机器可读结果位于 `.audit/runs/P1-046/sandbox-20260923-111546/evidence/`：

- `sandbox-result.json`：预检 0、安装 0、`passed=true`。
- `clean-windows-install-verification.json`：安装前后断言及空问题列表。
- `phonebridge-setup.log`：完整安装日志。
- `sandbox-console.txt`：Windows PowerShell 5.1 执行记录。

完成后通过 `wsb stop` 销毁隔离实例；宿主机 PhoneBridge/rclone 进程均为 0，P: 不存在。

## 修复与边界

验收过程中修复了三项工具问题：含空格 `.wsb` 路径未加引号、Windows PowerShell 5.1 不支持 `Path.IsPathFullyQualified`、GUI 安装器调用未等待进程结束。失败证据均保留在 P1-046 历史运行目录，最终通过证据来自全新隔离实例。

首次启动时 Windows Sandbox 自身商店更新曾以 `0x800705b4` 超时；微软允许继续使用经典 Sandbox，随后后台更新到 0.8.107.0。该问题不属于 PhoneBridge 安装器。验收不连接手机、不挂载盘符，也不重复已完成的文件传输测试。
