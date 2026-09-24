# PhoneBridge NG

中文优先的 Android ↔ Windows 无线文件访问工具。目标是在同一局域网内发现并验证手机，通过 HTTPS/WebDAV、rclone 和 WinFsp 将手机共享存储映射到 Windows 文件资源管理器。

**Phase 0 最小技术链路已通过：修复版完成真机 LAN、可信 TLS、盘符、双向 100 MB 和 Explorer 可见性验证。Windows C# 与 Android NG 已完成 Redmi 与 Samsung S23 Ultra 的首次配对、受保护凭据、三种访问模式、安全模式写入/删除确认、锁屏共享、断线安全卸载、同设备自动重连、托盘、用户选择自启动、脱敏诊断导出、手动地址恢复、1 GB 中断缓存恢复、5/10/20 GB 双向完整性、重启后大脏缓存恢复、视频播放/随机 Seek、常见文件名路径以及本地安装/升级/卸载的限定验收。Windows 自启动只进入托盘，盘符由用户手动连接。正式 Android 长期签名身份、独立 USB 备份、Samsung 迁移与同签名覆盖升级均已验证；当前唯一交付目录已经提升为正式第三方侧载版本，Windows 安装器已移除本地预览标签，并包含对应源码及许可材料。**

## 当前进度

| 阶段/任务 | 状态 | 证据 |
| --- | --- | --- |
| P0-001 上游审计 | 已完成 | [审计报告](docs/UPSTREAM_AUDIT.md)、[完成记录](docs/tasks/completed/P0-001-upstream-audit.md) |
| P0-002 项目结构与文档 | 已完成 | [完成记录](docs/tasks/completed/P0-002-project-foundation.md) |
| P0-003 原始技术链路验证 | 执行完成，验收失败；32 MiB 后读取内容错误 | [实测报告](docs/audit/P0-003-VALIDATION.md)、[任务记录](docs/tasks/completed/P0-003-original-chain.md) |
| P0-004 WebDAV 最小修复 | 已完成；12 单元、34 HTTPS 回归及 100 MB 双向实测通过 | [验收报告](docs/audit/P0-004-VALIDATION.md)、[任务记录](docs/tasks/completed/P0-004-webdav-stream-correctness.md) |
| P0-005 路径与上传安全 | 已完成限定修复；97 HTTPS、进程终止保护、100 MB 双向通过 | [验收报告](docs/audit/P0-005-VALIDATION.md)、[任务记录](docs/tasks/completed/P0-005-storage-safety.md) |
| P0-006 Android 兼容性 Lint | 已完成；0 errors，API 26/34 限定运行检查通过 | [验收报告](docs/audit/P0-006-VALIDATION.md)、[任务记录](docs/tasks/completed/P0-006-android-compatibility.md) |
| P0-007 Android 支持版本与前台共享方案 | 已完成限定任务；min 26、target 36、connectedDevice | [验收报告](docs/audit/P0-007-VALIDATION.md)、[任务记录](docs/tasks/completed/P0-007-android-support-policy.md) |
| P0-008 备用真机局域网文件链路 | 本轮未通过；mDNS 成功，TLS 缺少 IP SAN 阻断 | [验证记录](docs/audit/P0-008-VALIDATION.md)、[任务记录](docs/tasks/completed/P0-008-real-device-chain.md) |
| P0-009 Android 证书与 rclone 身份绑定 | 已完成限定任务；模拟器/真机 TLS 与真实 LAN 通过 | [验证记录](docs/audit/P0-009-VALIDATION.md)、[完成记录](docs/tasks/completed/P0-009-tls-identity.md) |
| P0-010 真机文件链路复验 | 限定链路通过；工具失败和补证分别保留 | [验证记录](docs/audit/P0-010-VALIDATION.md)、[阶段收口](docs/audit/PHASE-0-ACCEPTANCE.md) |
| P1-001 Windows 工程与设备发现 | 已完成；41 项测试、真机发现与移除通过 | [验收记录](docs/audit/P1-001-VALIDATION.md) |
| P1-002 Windows 只读挂载生命周期 | 已完成限定任务；74 项测试与 12 项真机检查通过 | [验收记录](docs/audit/P1-002-VALIDATION.md) |
| P1-003 配对与凭据生命周期设计 | 已完成设计；14 项库互操作与 11 项向量检查通过 | [验收记录](docs/audit/P1-003-VALIDATION.md) |
| P1-004 配对协议核心与帧校验 | 已完成限定核心；50 项 C#、15 项 Kotlin、59 项管道互操作通过 | [验收记录](docs/audit/P1-004-VALIDATION.md) |
| P1-005 Windows 受保护配对记录 | 已完成限定任务；60 项存储、184 项 Windows 回归通过 | [验收记录](docs/audit/P1-005-VALIDATION.md) |
| P1-006 Android 受保护配对记录 | 已完成限定任务；API 26/36 各 26 原生、3 进程恢复通过 | [验收记录](docs/audit/P1-006-VALIDATION.md) |
| P1-007 Android 首次配对与授权服务 | 已完成限定任务；两平台各 42 原生与 C# 联动通过 | [验收记录](docs/audit/P1-007-VALIDATION.md) |
| P1-008 Windows 首次配对与只读连接界面 | 已完成限定任务；215 项测试与 Redmi K40 真机 WPF 链路通过 | [验收记录](docs/audit/P1-008-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-008-windows-pairing-client.md) |
| P1-009 安全模式写入与删除保护 | 已完成限定任务；218 项测试与 Redmi K40 安全写入链路通过 | [验收记录](docs/audit/P1-009-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-009-safe-writes.md) |
| P1-010 锁屏共享与自动重连 | 已完成限定任务；221 项测试、API 36 锁屏读写和 Wi-Fi 恢复通过 | [验收记录](docs/audit/P1-010-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-010-auto-reconnect.md) |
| P1-011 Samsung 真机兼容验收 | 已完成限定任务；API 36 配对、锁屏读写、重连与退出清理通过 | [验收记录](docs/audit/P1-011-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-011-samsung-acceptance.md) |
| P1-012 Windows 托盘生命周期 | 已完成限定任务；227 项测试与 Samsung 实际隐藏、恢复、退出通过 | [验收记录](docs/audit/P1-012-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-012-windows-tray.md) |
| P1-013 Windows 开机启动生命周期 | 已完成历史限定任务；自动挂载行为已由 P1-030 按最终需求移除 | [验收记录](docs/audit/P1-013-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-013-windows-autostart.md) |
| P1-014 Windows 日志与诊断包 | 已完成限定任务；254 项测试与实际 WPF 白名单导出通过 | [验收记录](docs/audit/P1-014-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-014-windows-logging.md) |
| P1-015 Windows 手动地址排障入口 | 已完成限定任务；271 项测试与 Samsung 无 mDNS 手动连接/自动恢复通过 | [验收记录](docs/audit/P1-015-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-015-windows-manual-endpoint.md) |
| P1-016 1 GB 双向传输与中断完整性 | 已完成；273 项测试、双向哈希和中断后原缓存恢复通过 | [验收记录](docs/audit/P1-016-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-016-1gb-transfer-integrity.md) |
| P1-017 5 GB 双向传输完整性 | 已完成；双向长度/哈希、原子提交和安全卸载通过 | [验收记录](docs/audit/P1-017-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-017-5gb-transfer-integrity.md) |
| P1-018 10 GB 双向传输完整性 | 已完成；双向长度/哈希、上传边界修复和重启后缓存提交通过 | [验收记录](docs/audit/P1-018-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-018-10gb-transfer-integrity.md) |
| P1-019 重启后大脏缓存恢复挂载状态 | 已完成；279 项测试及 Samsung 4 GB 中断恢复通过 | [验收记录](docs/audit/P1-019-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-019-dirty-cache-recovery-mount-state.md) |
| P1-020 20 GB 双向边界完整性 | 已完成；双向长度/哈希、原子提交、缓存清零和安全卸载通过 | [验收记录](docs/audit/P1-020-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-020-20gb-transfer-boundary.md) |
| P1-021 视频播放与随机 Seek | 已完成；五段 Range、四次 Seek、解码帧、读取量和安全卸载通过 | [验收记录](docs/audit/P1-021-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-021-video-playback-random-seek.md) |
| P1-022 文件名与路径边界完整性 | 已完成；常见组合名、244 字节长名称、重命名、同名保护及安全模式缓存修复通过 | [验收记录](docs/audit/P1-022-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-022-path-name-boundaries.md) |
| P1-023 三种访问模式与用户设置收口 | 已完成；Android 每电脑设置、旧连接失效、Windows 手机确认模式及 Samsung 三模式行为通过 | [验收记录](docs/audit/P1-023-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-023-access-mode-settings.md) |
| P1-024 本地可安装交付包 | 已完成；本地安装/升级/卸载、配对保留及 Samsung 覆盖安装通过 | [验收记录](docs/audit/P1-024-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-024-installable-delivery.md) |
| P1-025 MVP 证据映射与常用目录验收 | 已完成；10 项通过、1 项部分通过、4 项未完成 | [验收记录](docs/audit/P1-025-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-025-mvp-evidence-and-common-folders.md) |
| P1-026 Android 重启恢复验收 | 已完成；实际 boot ID 变化、原配对和文件基线恢复通过 | [验收记录](docs/audit/P1-026-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-026-android-restart-recovery.md) |
| P1-027 大量照片目录性能验收 | 已完成；10,000 项挂载、完整枚举、Explorer 响应和资源清理通过 | [验收记录](docs/audit/P1-027-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-027-large-directory-performance.md) |
| P1-028 手机 IP 变化恢复验收 | 已完成；真实 IPv4 变化、mDNS 同身份恢复及唯一盘符通过 | [验收记录](docs/audit/P1-028-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-028-phone-ip-change-recovery.md) |
| P1-029 PC 睡眠/唤醒恢复验收 | 已完成；实际 S3、4.550 秒恢复、实际读取及唯一资源通过 | [验收记录](docs/audit/P1-029-VALIDATION.md)、[完成记录](docs/tasks/completed/P1-029-pc-sleep-wake-recovery.md) |
| P1-030 安装版自启动收口 | 已完成；自启动只进入托盘，不自动连接或挂载，旧恢复逻辑和测试已删除 | [完成记录](docs/tasks/completed/P1-030-installed-autostart-windows-reboot.md)、[验收记录](docs/audit/P1-030-VALIDATION.md) |
| P1-031 最终本地交付包刷新 | 已完成；标准安装器已包含 P1-030 手动挂载行为，APK、散列和 manifest 已刷新并核对 | [完成记录](docs/tasks/completed/P1-031-final-local-delivery-refresh.md)、[验收记录](docs/audit/P1-031-VALIDATION.md) |
| P1-032 本地交付入口与最短使用说明 | 已完成；当前交付目录包含安装器、APK、README、三项散列和 manifest | [完成记录](docs/tasks/completed/P1-032-local-delivery-entry.md)、[验收记录](docs/audit/P1-032-VALIDATION.md) |
| P1-033 当前协议与安全状态收口 | 已完成；核心规范与 v3 配对、严格 TLS、三种模式和手动挂载现状一致 | [完成记录](docs/tasks/completed/P1-033-current-protocol-security-state.md)、[验收记录](docs/audit/P1-033-VALIDATION.md) |
| P1-034 最终产品必要缺口审计 | 已完成；主链路无新运行缺陷，确认 Android 长期签名和干净 WinFsp 安装证据为剩余交付重点 | [完成记录](docs/tasks/completed/P1-034-final-product-gap-audit.md)、[验收记录](docs/audit/P1-034-VALIDATION.md) |
| P1-035 Android 长期第三方发行签名路径 | 已完成；测试/发行模式分离，项目外密钥、环境密码和证书摘要绑定已验证 | [完成记录](docs/tasks/completed/P1-035-android-release-signing-path.md)、[验收记录](docs/audit/P1-035-VALIDATION.md) |
| P1-036 Android 长期签名身份初始化工具 | 已完成；项目外主副密钥、固定身份、ACL、散列与证书一致性已用临时身份验证 | [完成记录](docs/tasks/completed/P1-036-android-signing-identity-provisioning.md)、[验收记录](docs/audit/P1-036-VALIDATION.md) |
| P1-037 干净 Windows 首装验收入口 | 已完成工具；真实无 WinFsp 首装已由 P1-046 关闭证据缺口 | [完成记录](docs/tasks/completed/P1-037-clean-windows-install-acceptance-runner.md)、[验收记录](docs/audit/P1-037-VALIDATION.md) |
| P1-038 发行构建绑定签名身份记录 | 已完成；发行构建从身份记录绑定 keystore 散列、别名、证书摘要/主题/有效期，八项错误配置失败关闭 | [完成记录](docs/tasks/completed/P1-038-release-build-signing-identity-binding.md)、[验收记录](docs/audit/P1-038-VALIDATION.md) |
| P1-039 当前产品就绪度复核 | 已完成阶段复核；当时列出的正式身份和干净首装缺口现已由 P1-041/P1-046 关闭 | [完成记录](docs/tasks/completed/P1-039-current-product-readiness-reassessment.md)、[验收记录](docs/audit/P1-039-VALIDATION.md) |
| P1-040 签名备份物理独立性检查 | 已完成；同一物理磁盘备份失败关闭，P1-041 已补充独立 USB 正向验证 | [完成记录](docs/tasks/completed/P1-040-signing-backup-physical-independence.md)、[验收记录](docs/audit/P1-040-VALIDATION.md) |
| P1-041 正式 Android 长期签名身份 | 已完成；正式身份、独立 USB 备份、APK签名及交付清单一致性已验证 | [完成记录](docs/tasks/completed/P1-041-formal-android-signing-identity.md)、[验收记录](docs/audit/P1-041-VALIDATION.md) |
| P1-042 正式 APK迁移与覆盖升级验收 | 已完成；Samsung 正式迁移、锁屏链路、配对保留和同签名覆盖升级通过 | [完成记录](docs/tasks/completed/P1-042-formal-apk-migration-acceptance.md)、[验收记录](docs/audit/P1-042-VALIDATION.md) |
| P1-043 提升正式签名默认交付 | 已完成；默认交付已切换为经真机验收的正式 APK，清单、散列和证书一致 | [完成记录](docs/tasks/completed/P1-043-promote-formal-delivery.md)、[验收记录](docs/audit/P1-043-VALIDATION.md) |
| P1-044 正式对应源码交付包 | 已完成；默认交付包含逐文件核验的对应源码 ZIP、GPL、NOTICE 和第三方许可 | [完成记录](docs/tasks/completed/P1-044-corresponding-source-delivery.md)、[验收记录](docs/audit/P1-044-VALIDATION.md) |
| P1-045 Windows 正式安装器元数据 | 已完成；移除 local preview/local installer 标签，当前机覆盖安装和配对保留通过 | [完成记录](docs/tasks/completed/P1-045-formal-windows-installer-metadata.md)、[验收记录](docs/audit/P1-045-VALIDATION.md) |
| P1-046 Windows Sandbox 干净首装 | 已完成；无 WinFsp/PhoneBridge 的 Windows 11 隔离环境中，内置 WinFsp 和客户端安装检查全部通过 | [完成记录](docs/tasks/completed/P1-046-clean-windows-sandbox-install.md)、[验收记录](docs/audit/P1-046-VALIDATION.md) |
| Phase 1 新版 Windows 客户端 | 配对、挂载、文件操作、生命周期与本地安装交付主链路已验收 | [开发规则](DEVELOPMENT_RULES.md) |
| MVP 验收 | 15 项通过 | [15 项产品验收](docs/PRODUCT.md)、[P1-030 最终需求验收](docs/audit/P1-030-VALIDATION.md) |

当前唯一交付目录是 `.audit/delivery/output/`；正式交付包含安装器、正式 APK、对应源码 ZIP、README、SHA256SUMS 和 manifest。二进制构建后运行 `scripts/New-SourceDelivery.ps1` 生成并核验对应源码包。`.audit/runs/` 中的文件只用于历史验证，不作为当前安装入口。先阅读交付目录内的 `README.txt`。

P0-001 的 107 个通过测试、1 个跳过用例及 7 项离线问题复现仅为上游审计证据，不能代表真实手机、Explorer、大文件或断网恢复通过。

## 技术与范围

- Android：Kotlin 原生应用，遵守系统存储权限，前台服务承载共享。
- Windows：C#、稳定 LTS .NET、WPF；2026-09-19 核对的 LTS 主版本为 .NET 10，工程已锁定 SDK 10.0.401 / runtime 10.0.12。[官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- 文件链路：HTTPS/WebDAV → rclone VFS → WinFsp → Explorer；mDNS 自动发现，手动 IP 为排障备用入口。
- 不引入云账号、遥测、通知同步、投屏、剪贴板同步、AI、NAS、Root 或自研文件系统驱动。自动更新及专用缩略图优化不在首版实现范围。

目标设备为 Windows 11 与 Samsung Galaxy S23 Ultra；型号不写死。Android 安装下限 API 26，本阶段 compile/target 36；已验证 API 26/36、备用 Redmi K40 与 Samsung SM-S9180/API 36 的限定行为，其他厂商兼容仍需实测。

## 文档入口

| 文件 | 用途 |
| --- | --- |
| [AGENTS.md](AGENTS.md) | 用户原始项目约束，每次开始工作先读 |
| [PRODUCT.md](docs/PRODUCT.md) | 用户、目标、非目标、MVP 与验收标准 |
| [INSTALL_LOCAL.txt](docs/INSTALL_LOCAL.txt) | 第三方本地安装、首次配对和日常使用的最短步骤 |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 固定技术路线、职责、信任边界及未定方案 |
| [DECISIONS.md](DECISIONS.md) | 已确定决策及未决问题，避免反复换方案 |
| [DEVELOPMENT_RULES.md](DEVELOPMENT_RULES.md) | 单任务流程、证据要求、范围与交付规则 |
| [SECURITY.md](docs/SECURITY.md) | 身份、凭据、文件安全和审计风险约束 |
| [PROTOCOL.md](docs/PROTOCOL.md) | 上游协议快照与 NG 协议要求，区分观察和规范 |
| [PAIRING.md](docs/PAIRING.md) | 当前首次配对、每电脑凭据、存储/恢复/撤销契约及验收边界 |
| [TESTING.md](docs/TESTING.md) | 原始链路验证、MVP、失败条件及证据模板 |

## 目录

```text
PhoneBridge-NG/
├─ AGENTS.md
├─ README.md
├─ ARCHITECTURE.md
├─ DEVELOPMENT_RULES.md
├─ DECISIONS.md
├─ android/                # 独立 NG App、实验补丁、配对核心与 Keystore 存储
├─ windows/                # C# 发现、配对/凭据、只读挂载、WPF 开发预览和诊断入口
├─ scripts/                # Windows 构建/测试脚本
├─ tests/                  # Phase 0 与 Windows 发现/挂载实机脚本
└─ docs/
   ├─ PRODUCT.md
   ├─ SECURITY.md
   ├─ PROTOCOL.md
   ├─ TESTING.md
   ├─ UPSTREAM_AUDIT.md
   ├─ audit/              # 已完成审计的证据和离线复现工具
   └─ tasks/
      ├─ active/
      ├─ completed/
      ├─ TASK_TEMPLATE.md
      └─ VALIDATION_TEMPLATE.md
```

`.audit/` 是被 Git 忽略的本地上游副本、工具和隔离实验环境，不是正式 Android 或 Windows 工程。原版 APK 仅供实验；复现入口见 [实验脚本](tests/integration/upstream_chain/README.md)，不能当作 NG 产品启动命令。

## 已知边界

原版上游问题及证据等级见[审计报告](docs/UPSTREAM_AUDIT.md)；原版仍保留明文凭据、身份与挂载等问题，不能作为 NG 产品使用。NG 独立应用已通过 P1-007 至 P1-023 实现 v3 配对、受保护凭据、严格 TLS、Windows 身份接入和三种访问模式；当前边界以 [协议](docs/PROTOCOL.md)、[配对契约](docs/PAIRING.md)和[安全要求](docs/SECURITY.md)为准。

P0-005 已取得 97 项 HTTPS、上传保护及 P: 双向 100 MB 证据。此前 P0-007 构建、20 个单元通过，Lint 为 0 errors/126 warnings；API 26/36 各 16 个原生通过、1 个跳过，各 11 项运行与 2 项失败启动通过，API 36 另有 7 项生命周期断言通过。测试工具最终 UI 清理失败和独立恢复回读如实记录于报告；模拟器已退出，缓存保留。

P0-008 真机 mDNS 成功，但无 IP SAN 的旧证书阻断 rclone；P0-009/010 已修复身份绑定并完成真机最小链路。P1-001 至 P1-029 已完成发现、挂载、配对、三种访问模式、安全写入、自动重连、Samsung 锁屏链路、托盘、自启动、脱敏诊断导出、手动地址恢复、1 GB 中断缓存恢复、5/10/20 GB 双向完整性、大脏缓存恢复、视频 Seek、常见文件名路径、本地安装交付、常用目录浏览、Android 实际重启恢复、10,000 项目录性能、真实手机 IP 变化及 PC 睡眠/唤醒恢复的限定验收。P1-030 根据最终需求移除登录自动挂载和进程恢复；自启动只进入托盘，用户手动连接后才出现盘符。P1-031 至 P1-033 收口最终本地交付和当前协议；P1-035 至 P1-041 完成长期开源侧载签名工具链、失败关闭验证、正式身份和独立 USB 备份。P1-042 已在 Samsung 上完成正式迁移、锁屏小文件链路和同签名覆盖升级；P1-043 已把同一正式产物提升为默认交付；P1-044 已把完整对应源码和许可材料纳入同一交付；P1-045 已移除 Windows 安装器开发标签并通过当前机覆盖安装；P1-046 已在无 WinFsp/PhoneBridge 的 Windows Sandbox 完成正式首装。项目只作第三方 App 分发；显式停止或移除任务后不得保留 PhoneBridge 后台服务。当前 Windows 客户端一次只允许一个活动手机挂载。MVP-01–15 已通过。

## 上游与许可证

参考 [ysachin26/PhoneBridge](https://github.com/ysachin26/PhoneBridge)，固定提交 `a378fec40561a4d18be2334f4de92ee02a0e7d0c`。上游声明 GPL-3.0-or-later，P0-004 已补齐完整 [LICENSE](LICENSE)，保留原许可证、作者与修改说明，见 [NOTICE](NOTICE.md)。源码修复以实验补丁保存；正式交付已包含完整对应源码及依赖许可。

