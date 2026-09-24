# P1-033 当前协议与安全状态收口验收

结果：通过限定验收。核心规范与当前 v3 配对、严格 TLS、受保护凭据、三种访问模式及 Windows 手动挂载行为一致。

## 已确认

- Android `SharingEngine.kt` 公告 `version=3` 和 `auth=paired-v1`；Windows `DeviceCandidate.cs` 将 v3 解析为 `PairedV3`，v2 仅为 `ExperimentalV2`。
- Android `AuthorizedServer.kt` 按 READ_ONLY、SAFE、READ_WRITE 三种模式在服务端授权；Windows `DeviceApi.cs` 使用认证头与 `CustomRootTrust`。
- `PROTOCOL.md`、`PAIRING.md`、`SECURITY.md`、`ARCHITECTURE.md`、`DECISIONS.md` 和 `README.md` 已区分当前实现与历史阶段记录。
- 根 `AGENTS.md` 的目标体验及 MVP-03 已按最终需求修正：启用 Windows 自启动时只进入托盘，电脑重启后必须手动连接，不自动启动 rclone 或创建盘符。
- `App.xaml.cs` 的启动路径只识别 `--startup`、建立单实例/托盘并调用 `ShowHiddenAtStartup()`；静态检查未发现连接、挂载、rclone 或配对记录恢复入口。
- 17 个已知过期当前状态模式均为 0 命中；7 份核心文档中的 147 个本地 Markdown 链接全部存在；P1-007 至 P1-023 的 17 份验收记录齐全。

机器可读证据：`.audit/runs/P1-033/current-state-doc-verification.json`。

## 未验证与边界

本任务没有修改 Android 或 Windows 产品代码，没有重复构建、真机、睡眠或重启测试。当前二进制行为仍以 P1-030 至 P1-032 的最终构建与交付证据为准。

独立密码学安全审计、API 27/28、更多 OEM、长期证书续期、磁盘满和强制终止仍未完成；文档没有把这些边界写成已通过。二维码扫描和同时多设备活动挂载不是当前已实现能力。
