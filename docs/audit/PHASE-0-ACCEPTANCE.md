# Phase 0 技术链路门槛核对

2026-09-19。**允许进入 Phase 1 的单任务开发；不代表 MVP、正式安全体系、所有设备兼容或发布验收通过。** 使用修复版基线，原版 P0-003 的失败记录不变。

| 必须完成的前置项 | 可复核证据 | 判定 |
| --- | --- | --- |
| 上游 15 项审计、许可、模块/协议/安全/复用边界 | [UPSTREAM_AUDIT](../UPSTREAM_AUDIT.md)、固定上游提交，GPL 文本与 NOTICE 保留 | 完成 |
| 项目结构、开发规则、架构和决策 | 根 AGENTS、README、ARCHITECTURE、DEVELOPMENT_RULES、DECISIONS 与 docs；正式 Windows 工程此前未创建 | 完成 |
| Android 构建和支持策略 | [P0-007](P0-007-VALIDATION.md)：min 26、target 36、connectedDevice；[P0-009](P0-009-VALIDATION.md)：最终构建、25 单元、Lint 0 errors/117 warnings，API 26/36 和真机原生各 8 通过 | 限定范围完成 |
| TLS 真实连接而非仅探针 | [P0-009](P0-009-VALIDATION.md)：Keystore 身份、专属信任池、正确/错误身份与地址、重启和损坏恢复；[P0-010](P0-010-VALIDATION.md) 文件传输继续启用同一验证 | 完成当前机制验证 |
| 原始技术路线可发现、挂载、读、写、复制和退出 | [P0-010](P0-010-VALIDATION.md) CHAIN-01–06：真机 LAN、WinFsp/rclone、双向 100 MB/独立手机散列、用户确认 Explorer 可见、独立清理与收尾回归 | 完成修复版最小链路验证 |

原版不能原样用于产品。P0-004–009 的累计补丁保留来源和哈希；P0-010 未修改 APK。测试工具入口失败、复现和独立补证逐项保留，不能将“每项都有结果”改写成“所有脚本首次执行均成功”。

范围限制：备用 Redmi K40、手机 Wi-Fi 到 PC 有线同 LAN；三星、两端同时 Wi-Fi、1/5/10/20 GB、断网/休眠/真机重启/换 IP和长期后台未验收。Android ROM 的后台测试界面限制通过正常前台测试方式处理，没有证明厂商后台自启可靠。

正式一次性配对、Windows 受保护凭据/信任材料、默认安全模式、删除确认、自动重连、安装包和完整中文界面仍待实现。后续实现按 D-03、D-05–08 与 MVP 矩阵推进，不把临时 Basic 密码和 Python 实验入口作为产品接口。本门槛通过后首先进行[Windows 设备发现任务](../tasks/completed/P1-001-windows-discovery.md)，不发布、不合并或更新其他设备。

