# P1-014 Windows 日志与诊断包

日期：2026-09-21。结果：通过限定任务验收。结论覆盖有界本地结构化日志、固定字段入口、失败隔离、WPF 显式导出和实际 ZIP 白名单回读；不等于全部设备/网络故障场景已经产生过真实日志。

## 设计与实现

日志默认位于当前用户 `%LOCALAPPDATA%\PhoneBridge-NG\Logs`，目录 DACL 关闭继承，只允许当前用户与 LocalSystem 完全控制，并拒绝重解析目录。逐行 JSON 入口只接收固定事件、级别、结果码、状态、非负计数和耗时；调用方不能提供任意消息。设备名、设备 ID、IP、端口、盘符、用户文件路径、rclone 参数、认证 Header、Token、密码、私钥和异常原文均不进入日志。

单文件上限 1 MiB，最多 5 个文件；过大的旧文件在打开前移除。写入进程内串行并逐条同步。ACL、磁盘、轮转或写入失败会将日志器标为不可用，但不向发现、连接、挂载或退出状态机抛出异常。应用接入启动/停止、发现变化、认证结果、连接、挂载状态、rclone 退出、WinFsp、网络健康、重连、托盘可见性和自启动设置事件；未知业务错误只折叠为固定 `Failure`，rclone stderr 不记录。

WPF 新增中英文“导出诊断包”按钮。用户选择目标后，导出器只创建 `manifest.json` 和 `logs/events-*.jsonl`，先在目标目录生成随机临时文件，关闭并同步 ZIP 后再同卷移动提交。取消或失败只清理自身临时文件，不修改源日志。实现使用 .NET 自带 JSON/ZIP/ACL API，不加入上传、遥测或崩溃采集依赖。[ZIP API](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression?view=net-10.0)、[ZIP 最佳实践](https://learn.microsoft.com/en-us/dotnet/standard/io/zip-tar-best-practices)、[DirectorySecurity](https://learn.microsoft.com/en-us/dotnet/api/system.security.accesscontrol.directorysecurity?view=net-10.0)、[File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0)。

## 自动验证

新增 11 项 Desktop 测试，覆盖固定 JSON schema、100 路并发写入、轮转数量/大小、存储不可用失败隔离、实际 ACL、过大旧日志、异常原文与合成秘密不落盘、ZIP 条目/manifest 白名单、取消/提交失败保留日志和清理临时文件、导出后继续写入、未知错误折叠及中英文资源。

测试先发现当前写句柄共享规则使 `File.ReadAllBytes` 无法读取活动日志；修正为在同一串行锁内从既有句柄快照，没有放宽外部写共享。随后发现 ZIP 时间戳不能早于 1980 年；固定为 ZIP 可表示的 1980-01-01。两项均保留为测试发现的明确根因，没有连续更换实现。

最终 `scripts/Verify-Windows.ps1` 完成 locked restore、Release 构建和全量测试。构建为 0 warnings/0 errors；254 项全部通过：Desktop 33、Discovery 54、Mounting 36、Pairing 50、Connection 18、Credentials 63。结构化记录见 [windows-tests.json](p1-014/windows-tests.json)。

## 实际界面导出

用户在中文 WPF 界面点击“导出诊断包…”并完成保存。审计副本 `.audit/p1-014-live.zip` 为 622 bytes，SHA-256 `d30c1089a75287bac0c9e6a127e5583dc611349d1ac0fb680e3307adf357e8a7`。

独立回读确认 ZIP 只有 `manifest.json` 和 `logs/events-0.jsonl`；manifest 恰有 8 个白名单字段，声明/实际日志数均为 1，日志器状态为 available。3 行 JSONL 全部可解析，字段均在白名单内；密码、Token、认证、私钥、用户名、计算机名、设备身份、地址、端点及路径字段扫描为 0 命中，临时文件为 0。导出完成后本地日志新增 `DiagnosticExportCompleted`，证明同一日志器继续写入。结构化记录见 [diagnostic-export-live.json](p1-014/diagnostic-export-live.json)。

## 未验证

没有让实际磁盘写满、撤销当前用户目录权限或在真实运行中生成 5 MiB 全量轮转；这些失败由隔离测试覆盖。没有逐项制造真实 rclone 异常退出、WinFsp 丢失、认证失败和网络重连再检查对应日志序列；事件接入已编译并由既有状态机回归覆盖，但本轮实际界面只产生启动、发现、初始挂载状态和导出完成事件。没有在英文 Windows 布局中人工点击导出；en-US 资源由自动测试核对。

## 下一步

后续任务 [P1-015 Windows 手动地址排障入口](../tasks/completed/P1-015-windows-manual-endpoint.md)现已完成。
