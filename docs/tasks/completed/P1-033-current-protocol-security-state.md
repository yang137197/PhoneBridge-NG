# P1-033 — 当前协议与安全状态收口

状态：已完成。日期：2026-09-23。

## 目标

消除核心规范仍将已实现的 v3 配对、严格 TLS、受保护凭据和安全模式写成“待实现”的冲突，使后续开发以当前产品状态为准，同时保留历史阶段和未验证边界。

## 范围

以当前 Android/Windows 源码、P1-007 至 P1-023 验收及最终 Windows 回归为依据，更新协议、配对、安全、架构、决策和 README 的现状描述；明确实验 v2、当前 v3、历史阶段记录和真正未验证项。

## 不做什么

不修改产品运行代码，不新增协议或安全功能，不把限定验收扩大为独立密码学审计、完整安全扫描或公开发行结论，不重复真机与传输测试。

## 涉及文件

- `docs/PROTOCOL.md`、`docs/PAIRING.md`、`docs/SECURITY.md`：当前规范与证据边界。
- `ARCHITECTURE.md`、`DECISIONS.md`、`README.md`：交叉引用和阶段状态。
- 本任务与验收记录。

## 实现

更新 `PROTOCOL.md`、`PAIRING.md` 和 `SECURITY.md`，明确当前 Android 公告 v3/`paired-v1`，Windows 只允许 v3 进入正式配对与连接，v2 仅保留为不可连接的实验候选；当前首次配对使用手动 8 位码，二维码只保留格式，没有扫描界面。规范同步记录 J-PAKE、严格 HTTPS、每电脑随机 token、DPAPI/Android Keystore、三种访问模式及撤销边界。

更新 `ARCHITECTURE.md`、`DECISIONS.md` 和 `README.md`，把 P1-003 至 P1-016 的“未实现/未验证”表述标为任务当时的阶段记录，并补充 P1-008、P1-011、P1-016 至 P1-020、P1-026、P1-028、P1-029 的后续证据。仍未验证的独立密码学审计、API 27/28、更多 OEM、长期证书续期、磁盘满和强制终止继续保留。

同步修正根 `AGENTS.md` 的最终体验和 MVP-03：Windows 自启动只启动唯一客户端并进入托盘；电脑重启、重新登录或客户端重新启动后均由用户手动点击连接，不启动 rclone、不创建盘符。活动连接中的短暂网络中断仍可按原策略恢复。

## 测试

- 源码事实核对通过：Android 公告 `version=3`/`auth=paired-v1`；Windows 区分 `PairedV3` 与不可连接的 `ExperimentalV2`；Android 服务端存在 READ_ONLY/SAFE/READ_WRITE 三种授权；Windows 使用受保护认证头和 `CustomRootTrust`。
- 自启动静态边界通过：`App.xaml.cs` 的 `--startup` 路径只创建客户端并调用 `ShowHiddenAtStartup()`，不包含连接、挂载、rclone 或配对记录恢复入口。
- 17 个过期当前状态模式在 7 份核心文档中均为 0 命中。
- 7 份核心文档的 147 个本地 Markdown 链接全部可解析。
- P1-007 至 P1-023 的 17 份验收记录全部存在。
- 机器可读证据：`.audit/runs/P1-033/current-state-doc-verification.json`。

## 验收结果

已完成并通过限定验收。核心规范现在区分原版上游、历史任务阶段和当前 NG 产品，不再把已实现的 v3 配对、严格 TLS、安全存储、三种模式或最终手动挂载行为描述成待实现。

本任务只修改文档，没有改变产品二进制，因此没有重复 Windows 构建、Android 构建、真机、睡眠或重启测试。下一任务应做一次只读的最终产品缺口审计，只从当前 MVP、实际交付和未验证边界中筛出仍影响第三方本地使用的必要项，避免继续扩展非需求功能。
