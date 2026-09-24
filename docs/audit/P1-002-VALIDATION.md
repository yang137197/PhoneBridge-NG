# P1-002 Windows 只读挂载验收

日期：2026-09-19。本任务只交付 C# 只读会话与实验诊断入口，不是正式配对、完整 Windows 客户端或 MVP。

## 实现与代码复核

`PhoneBridge.Mounting` 分离已确认 CA、内存凭据与未认证发现候选。固定 rclone 1.75.1 的 SHA-256；检测已安装 WinFsp 和空闲盘符，CA 放在当前用户/SYSTEM 私有目录，保持只读文件句柄禁止修改/删除。只使用 `--read-only`、`--ca-cert`、VFS cache off，不写配置或凭据文件。

RC 只监听随机 loopback 端口，使用独立随机认证。先检查 core/pid 与所属进程一致，再以真实远端列表请求验证 TLS/认证和盘符，才报告 Mounted。停止先 core/quit，等待所属进程与盘符消失；超时仅可强制结束本只读会话，记录 Forced。停止失败保留所有权、拒绝新挂载，允许重试。stdout/stderr 持续排空且限制捕获量；对外只输出固定错误代码。

复核发现先启动再加入 Job 留有崩溃窗口，已改成 CreateProcessW 在创建时绑定 Job。只继承三个标准管道，不继承 Job 句柄；直接保存创建返回的进程句柄，无按名称杀进程或 PID 重查。Windows 参数转义通过实际 .NET 子进程往返测试覆盖空字符串、空格、中文、引号、尾部反斜杠。参照 [Microsoft Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)、[JOB_LIST/HANDLE_LIST](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)、[CreateProcessW](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw)。rclone 行为依据 [mount](https://rclone.org/commands/rclone_mount/)、[RC](https://rclone.org/rc/)、[WebDAV](https://rclone.org/webdav/)。

诊断入口只供 JSON 自动化测试；输入通过 stdin，最大 32 KiB、生命周期 120 秒。Console.In 同步读在工作线程上执行，取消等待可以结束宿主；未创建中文/英文产品 UI，固定事件代码供后续资源化界面映射。正常清理删除本会话公有 CA；异常终止会留下公有 CA 文件，未留下认证凭据。没有添加自动扫描删除旧目录的逻辑。

## 环境与执行

Windows 11 x64；.NET SDK 10.0.401 / runtime 10.0.12；WinFsp 2.1.25156.ddca7bd；rclone 1.75.1，SHA-256 `033eee51c9ad47c2de2624b6674d355274bcd6cf0027a5f85db4437ba24ae81c`。驱动沿用此前已授权安装，没有重装或改变系统信任库。

备用 Redmi K40 / Android 16 API 36，原 P0-009 应用/测试 APK；PC Ethernet `192.168.100.216`、手机 Wi-Fi `192.168.100.197`。授权 USB 仅取得公有 CA/临时认证和独立核对，文件读取经 LAN。保持 Music 下 P0-010 合成目录，不重新复制 100 MB 或创建其他共享样本。

| 检查 | 结果及原始记录 |
| --- | --- |
| `scripts/Verify-Windows.ps1` | locked restore、Release build exit 0，0 warnings / 0 errors；41 发现 + 33 挂载/进程测试通过，无跳过；`.audit/runs/P1-002/final-unit-4` |
| `tests/integration/windows_mount/real_device_mount.py` | exit 0，12/12；最终 `real-bf9be356e258`，Android 宿主 `OK (1 test)` |
| `tests/integration/windows_mount/diagnostic_input.py` | exit 0，4/4；静默 stdin 于 120.265 秒退出，未残留诊断进程；`input-aef606d05308` |

真机 12 项包括：指纹不符、占用盘符、二进制散列不符拒绝；指定四个项目可列出、49 字节文本 SHA-256 正确、新建文件被拒绝；正常卸载 exit 0、Forced=false；错误密码与另一个有效 CA 拒绝、从未报告 Mounted；运行中 CA 不可写；另一进程重复挂载拒绝且原会话仍在；强制结束诊断宿主后所属 rclone 与 P: 消失；手机样本/元数据/身份不变；最终共享停止、无 ADB 转发。

文本 SHA-256：`7374185329b3a98d96e953d8ed38bfe1c528ca5dd6b8298bdead468f65c5251e`。两个原 100,000,000 字节样本只在手机核对散列，没有重新传输。只读探针尝试创建唯一新文件，失败后不触碰已有文件。所有权/超时状态的部分分支使用注入会话测试；没有把这些模拟结果当作真实驱动故障。

最终只读探针取得 Windows 错误码 19（写保护），不将任意 IOException 视为只读通过。4 项输入测试覆盖的输入/期限代码未在该探针细化中改变，无输入退出实测为 120.265 秒。

脱敏证据：[真机结果](p1-002/real-device.json)、[单元结果](p1-002/unit-tests.json)、[诊断输入结果](p1-002/diagnostic-input.json)、[失败记录](p1-002/failures.json)、[最终状态](p1-002/final-state.json)、[源码与二进制清单](p1-002/manifest.json)。原始 TRX、JSONL 与宿主日志保存在忽略目录 `.audit/runs/P1-002/`。

## 失败与修正

- `unit-first`：一条测试错误要求 Job 强制结束返回非零。官方保证终止，不保证此退出码；改为证明原进程未结束、关闭 Job 后实际退出。其余测试未用退出码代替存活检查。
- `real-7f16e9946299`：正常挂载/只读/密码拒绝已过，但合成 CA 生成依赖未安装的 Python cryptography，入口 exit 1。改用固定公有 CA 夹具，私钥已丢弃；没有安装额外依赖。
- `final-unit`：PowerShell -File 测试夹具被本机执行策略拒绝，exit 1；直接复现得 UnauthorizedAccess。改用专用 .NET argv 夹具，保留原策略。
- `real-f4c413c0a1a0`：12 项断言已过，但旧宿主脚本回读停止标记失败，入口 exit 1，不能算整轮通过。源码确认 Android 只检查标记存在，随后删除；宿主日志为 OK。新测试包装用 touch 发信号，依赖 instrumentation 完成回执，消除消费与回读的竞争；历史 P0 脚本和 APK 未改动。最终新入口整轮 exit 0。

## 限定结论与下一任务

本机备用手机的只读会话可挂载、读取、拒绝写入并清理；最终源码已做生命周期、进程所有权、凭据输出、固定二进制/CA、取消和失败路径复核。手机仍使用上游实验版的旧认证配置，正式配对和长期安全存储尚未实现；不能把实验 USB 引导用于正式自动连接。

未验证：三星、ARM64/其他 Windows、两端同时 Wi-Fi、真实断网/IP 变化/睡眠/重启、长期运行、1–20 GB、5,000/10,000 项目录、全部宿主启动时刻的故障注入及真实 WinFsp 卸载失败。当前远端列表响应限制 1 MiB，超限拒绝，不宣称大目录已验收；错误认证与 TLS 失败统一返回固定错误类别。没有 WPF、托盘、安装包或自动重连。

本轮交接任务：[P1-003 配对与凭据生命周期设计](../tasks/completed/P1-003-pairing-contract.md)（现已完成设计），先明确 D-03 身份确认/撤销与安全保存的协议，避免界面直接信任发现候选。
