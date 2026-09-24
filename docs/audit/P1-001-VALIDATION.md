# P1-001 Windows 发现验收

日期：2026-09-19。结论：**当前限定任务通过**。交付 C# 候选模型、Windows mDNS 发现、手动 IP 及诊断入口；未交付配对、挂载、WPF 界面或日常可用客户端。

## 环境与来源

Windows 11 Pro 25H2 / 26200.8875，PC Ethernet 192.168.100.216，备用 Redmi K40（alioth / M2012K11AC，Android 16 / API 36）WLAN 192.168.100.197，同 LAN。Windows 同时有其他网卡；此次发现返回 IPv4 与带 scope 9 的 IPv6 link-local，未验证 IPv6 文件连接或跨网卡切换。

.NET 10 LTS，SDK 10.0.401 / runtime 10.0.12。官方 zip SHA-512 与 dotnet.exe Microsoft Authenticode 通过；本地隔离到 `.audit/tools`，未安装全局 SDK。global.json 禁止自动滚动，Windows targeting pack 10.0.19041.57 显式锁定，MSTest 4.0.2 与传递依赖由 lock 文件固定。[官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)、[SDK 来源/散列](p1-001/sdk.json)、[依赖说明](../../windows/DEPENDENCIES.md)。

APK / test APK 与 P0-009 完全一致；无 APK 安装、权限修改、驱动修改或文件传输。手机既有 P0-010 三个文件和空目录保留。仓库仍为无提交的 master，无远端；未提交、推送或发布。

## 实现与复核

- Windows 系统 DNS-SD AEP Service 枚举；正式不包含 Zeroconf/Makaretu 依赖。选型和源代码检查见 [ADR-015](../../DECISIONS.md)。
- Added/Updated/Removed 按系统服务 ID 处理，增量属性合并；IP/端口变化更新同一候选，不按名称合并。未知更新和过期 watcher 回调不能复活候选。
- TXT 单项 255 字节、总计 4096 字节、最多 32 项，拒绝重复键/畸形值/超长名/控制字符、非 HTTPS 和不支持 version。只存展示字段和端点，不持久保存广播字段或身份材料。
- IPv4/IPv6、端口、scope 校验与手动 IP 共用；拒绝回环/未指定/组播、模糊 IPv4、域名/URL/路径/认证字符串。所有候选均未认证，TXT 指纹和 device_id 不建立信任。
- 单循环处理、256 条事件队列与候选上限，超载/网络变化重建；停止解除网络和 watcher 订阅并等待 Stopped。停止超时输出 stop-failed，不能声称完成。中文/en-US 文案来自 resx。
- 代码逐文件复核了校验、去重、回调代次、取消/超载、异常退出、日志字段与依赖方向。未发现待修复的本任务阻断项；这不等于全面安全扫描或产品安全验收。

## 持续 watcher 移除缺陷与修复

原实现发现成功，但停止手机后 35 秒未收到 Removed，判失败。独立记录中 Python zeroconf 收到手机 remove，Windows 原生监听 175 秒期间（停止后约 160 秒）没有 Removed，且 Ttl 属性为 null。同机新 watcher 在手机离线时没有候选。

由此采用每 12 秒停止并重新枚举，候选跨扫描轮次保留，连续两轮没有有效广告才移除；明确 Removed/无效更新仍立即撤销。扫描只负责候选新鲜度，不是已认证连接活性探测。原始 API 行为有实际复现，未用替换协议/库回避诊断。[官方 CreateWatcher 初次枚举/更新语义](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformation.createwatcher?view=winrt-26100)、[IP 属性定义](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-ipaddress)。

失败尝试包括首次误用属性名导致 COM 0x8002802B、原生移除缺失、测试模板重复 Parallelize 编译失败；均保留于 [attempts](p1-001/attempts.json)，没有改写为通过。修复后的两次手机离线移除分别为 24.000 与 35.937 秒；产品不能承诺固定 30 秒以内离线移除。

## 实际命令与结果

| 检查 | 命令/入口 | 结果 |
| --- | --- | --- |
| 锁定还原 / Release 编译 / 单元行为 | `scripts/Verify-Windows.ps1 -ResultsDirectory .audit/runs/P1-001/unit-final` | exit 0；0 warnings / 0 errors；41/41，无跳过 |
| 验证包签名 | `dotnet nuget verify <Windows SDK pack> <MSTest package> --all` | exit 0，Microsoft 作者和 NuGet 仓库签名验证通过 |
| 已知 NuGet 漏洞查询 | `dotnet list windows/PhoneBridge.Windows.slnx package --vulnerable --include-transitive --format json` | exit 0，未报告受影响包；非全面安全结论 |
| 诊断命令 | `.audit/p1-001/cli_check.py` | 4/4：手动 IPv4/IPv6、HTTP 拒绝、无效端口拒绝、英文资源；拒绝 exit 2，正常 exit 0 |
| 真实发现/移除/停止 | `tests/integration/windows_discovery/real_device_discovery.py` | exit 0，6/6；正式保存脚本对应 run `real-6ee55d4c1371` |

真机结果：[real-device](p1-001/real-device.json)、[事件时序](p1-001/events.jsonl)。先启动 C# watcher，再启动既有 Android 测试宿主；从宿主启动计 3.890 秒出现，连续共享 32 秒跨扫描轮次没有重复 Added 或移除；手机宿主停止后 35.937 秒因两轮未发现撤销候选。诊断 95 秒有界运行后正常结束，共享已停止，ADB 无转发。

本地原始 TRX、SDK/签名输出、宿主日志与诊断失败日志在 `.audit/runs/P1-001/`、`.audit/p1-001/`。可公开的计数、源码/二进制 SHA-256、包 content hash 保存于 [unit-tests](p1-001/unit-tests.json)、[manifest](p1-001/manifest.json)、[CLI 结果](p1-001/cli-results.json)。没有写入密码、认证 Header、私钥或用户文件内容。

## 收尾及未验证范围

[手机最终状态](p1-001/final-phone-state.json)：身份 CA 不变，P0-010 三个样本 SHA-256 与原验收一致，Music 和自启 false 未变；app/前台共享均停止，宿主控制文件已清除、ADB 无转发。[PC 最终状态](p1-001/final-pc-state.json)：P: 不存在，rclone/诊断产品进程为 0。当前任务没有启动 rclone。

未验证：Samsung Galaxy S23 Ultra、两端同时 Wi-Fi、实际 DHCP/IP 切换、不同网卡间移动、睡眠/唤醒、重启/Doze、网络拥塞和长期运行；接口变化/端点更新仅做行为测试。mDNS 可被伪造，候选必须经过后续配对和真实 TLS 认证才能使用。Windows 系统缓存与重枚举导致的离线等待仍是已知限制。

本轮不重复大文件复制或 Phase 0 全套测试；不宣称 MVP-01 的全部目标环境通过。当时下一任务：[P1-002 只读挂载生命周期](../tasks/completed/P1-002-windows-readonly-mount.md)，现已完成限定验收，最新状态见根 README。
