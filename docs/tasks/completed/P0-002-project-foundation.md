# P0-002 — 项目结构与开发文档

状态：已完成文档与结构验收；尚未执行原始链路验证。日期：2026-09-19。

## 目标

完成 AGENTS.md 第 33 节的 Phase 0 第二任务，为后续链路验证提供一致的范围、架构约束、风险及验收记录。

## 范围

建立第 24 节要求的目录；编写 README、ARCHITECTURE、DEVELOPMENT_RULES、DECISIONS 及 PRODUCT、SECURITY、PROTOCOL、TESTING；记录已确定约束和待验证方案。

## 不做什么

不改 AGENTS 原文、P0-001 审计或上游源码；不创建应用工程、不修复核心代码、不安装运行依赖/驱动、不连接设备、不进入第三任务或 Phase 1。

## 涉及文件

项目根 4 份新文档、docs 下 4 份规范、4 个用途 README、任务模板/验收记录模板、active 目录占位及本任务记录。

## 实现

以用户交接和固定提交的 P0-001 审计为依据，区分产品要求、上游观察、设计待定和实际验收。已完成：

- README 作为当前状态/文档入口，明确现在没有 NG 可运行应用。
- ARCHITECTURE 记录固定链路、职责、状态与数据边界；DECISIONS 整理 12 项既定约束和 8 项待定问题，没有提前冻结配对/TLS/删除实现。
- DEVELOPMENT_RULES 落实一次一个任务、证据分类、阶段收口及保留已有修改。
- PRODUCT 保留 MVP-01–15；SECURITY/PROTOCOL 分开写上游事实、正式要求与未决实现。
- TESTING 准备 P0-003 的隔离环境预检、CHAIN-01–06、专项测试和停止条件；主手机共享根未隔离时不能直接做危险写入。
- android/windows/scripts/tests 用 README 保留目录；补充任务/验收模板和 active 目录占位。

下一任务仅准备为 P0-003 原始技术链路验证，没有自动执行。既有审计及用户 AGENTS 原文未修改。

## 测试

本机 Python 3.12.14 单次只读检查，使用 pathlib/re/hashlib/json 和 Git，核对：必需文件/目录、所有 Markdown 相对文件链接、代码围栏/尾随空白、任务 7 个字段、MVP 编号唯一完整、旧文件 SHA-256，以及上游固定提交与工作区。

结果：19 份 Markdown、61 个本地链接均有效；15 项 MVP 齐全；任务字段及 6 个要求目录通过；本轮之前的 10 个文件 SHA-256 全部不变；上游仍为 `a378fec40561a4d18be2334f4de92ee02a0e7d0c`，工作区干净。android/windows/scripts/tests 仅含用途 README，没有产品源码或脚本混入。

官方文档复核 .NET 支持策略、Android Keystore 与 rclone mount；新文档未把未定方案写成已实现。没有重跑 P0-001 旧测试，未执行应用构建/运行或任何设备测试。

## 验收结果

已完成 Phase 0 第二任务的文档/结构交付。12 项 ADR 整理用户已有决策，8 项未决问题保持待验证；文档完成没有修复审计中的安全与数据问题。

未验证：Android 构建、真实 rclone/WinFsp/Explorer 链路、手机访问、性能和故障恢复。Phase 0 整体与 MVP 仍未通过。

唯一下一任务：P0-003 原始技术链路验证；先核对开发环境、依赖与可丢弃 Android 测试设备/数据，再按 TESTING 执行。本轮不进入该任务或 Phase 1。
