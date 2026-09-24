# P1-013 Windows 开机启动生命周期

日期：2026-09-21。结果：通过限定任务验收。结论覆盖当前用户启动项、明确启用/禁用、应用自身隐藏启动、单实例和唯一已配对 Samsung 的自动挂载；未实际注销、重新登录或重启 Windows，因此 MVP-03 仍未完整通过。

## 设计与实现

当前客户端是未打包 WPF 应用，没有包身份。依据 Microsoft 的 Run/RunOnce 文档，选择 `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` 的普通字符串值，不使用管理员权限、系统服务或计划任务。启动项默认不存在，只有用户勾选时写入；命令固定为带引号的当前可执行文件绝对路径和唯一参数 `--startup`，并拒绝超过 260 字符的命令。[Run and RunOnce Registry Keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)、[Windows 应用打包概览](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/)。

启用、禁用和回读均以注册表真实值为准。同名值的类型或命令不完全匹配时报告冲突，既不覆盖也不删除。写入和删除后必须回读确认。自启动的第二个实例静默退出，不弹出重复实例提示。

`--startup` 使用已有托盘生命周期隐藏主窗口。发现集合稳定两秒后，只在恰好一台 PairedV3 候选与 Active 受保护记录精确匹配时，才将其交给现有重连监督；后续仍执行保存 CA、凭据、`share_ready` 和模式校验。多台已配对手机同时可用时不按发现顺序选择，而是要求用户打开窗口手动选择。

## 自动验证

首次构建因新文件缺少 `System.IO` 引用失败，补齐唯一明确根因后重新运行。最终 `scripts/Verify-Windows.ps1` 完成 locked restore、Release 构建和测试，构建为 0 warnings/0 errors；243 项全部通过：Desktop 22、Discovery 54、Mounting 36、Pairing 50、Connection 18、Credentials 63。

新增测试覆盖命令引用、默认关闭、启停幂等、异常类型/内容冲突不改写、读写失败、写后回读、无效/超长路径、精确启动参数，以及零/一/多台已激活配对候选的选择策略。结构化记录见 [windows-tests.json](p1-013/windows-tests.json)。

## 本机与真机验收

初始 HKCU 启动项不存在，界面开关为关闭。用户启用后回读为 `REG_SZ`，命令与当前可执行文件和 `--startup` 精确匹配，长度 176。按保存命令启动后窗口隐藏，重复执行仍只有原桌面 PID；用户从托盘恢复并关闭设置后，注册表值消失。随后不使用 PowerShell 隐藏窗口参数再次启动，主窗口句柄仍为 0，证明隐藏由应用自身完成。

补齐自动连接后，Samsung SM-S9180 开始共享，电脑只执行 `PhoneBridge.Desktop.exe --startup`。6.86 秒后 P: 出现，桌面进程和 rclone 各一个；用户从托盘恢复并确认界面为 `P:\ · SM-S9180 · 安全模式`。没有手动选择手机或点击连接。最终退出桌面并停止手机共享后，P:、rclone、桌面进程、HKCU 启动项、Android `SharingService` 和共享 WakeLock 均不存在。结构化记录见 [autostart-live.json](p1-013/autostart-live.json)。

## 未验证

没有实际注销/登录或重启 Windows，Windows 对 Run 启动时机的调度未实测；MVP-03 保持部分证据而非通过。临时启动项引用开发构建，并已在验收结束时删除；安装后的固定路径、升级迁移和卸载清理需由安装器任务验证。两台已配对手机同时在线只做策略测试，未使用两台真机。未验证 PC 重启前存在待上传文件、非默认盘符偏好或企业策略禁用 Run 键。

## 下一步

当时的唯一下一任务：[P1-014 Windows 日志与诊断包](../tasks/completed/P1-014-windows-logging.md)，现已完成。
