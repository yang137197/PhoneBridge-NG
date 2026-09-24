# Windows 依赖与来源

核对日期：2026-09-22。产品代码不包含第三方 mDNS 库，也不引用测试框架或遥测包。

| 组件 | 锁定版本 | 用途与来源/许可 |
| --- | --- | --- |
| .NET SDK / Runtime | 10.0.401 / 10.0.12 | .NET 10 LTS；[支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)、[官方 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)。SDK archive SHA-512 与 dotnet.exe Microsoft 签名已核验；Windows 本地安装包自包含 10.0.12 runtime，并随包提供 SDK 内附许可与第三方通知。 |
| Microsoft.Windows.SDK.NET.Ref | 10.0.19041.57 | 桌面 WinRT 投影；由 Windows TFM 引用，`WindowsSdkPackageVersion` 显式锁定。[NuGet](https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref/10.0.19041.57)、包声明的 [Windows SDK 许可](https://aka.ms/WinSDKLicenseURL)。不将整个 targeting pack 误写为 MIT。 |
| MSTest | 4.0.2 | SDK 模板选定的测试依赖，MIT；[NuGet](https://www.nuget.org/packages/MSTest/4.0.2)、[Microsoft testfx](https://github.com/microsoft/testfx)。只在 tests 工程。 |
| BouncyCastle.Cryptography | 2.7.0 | P1-004 配对核心 J-PAKE、P1-005 CA 验证，精确版本/锁文件；[NuGet](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0)，包内 [MIT](../licenses/pairing-bouncycastle-csharp-LICENSE.md)。P1-003 作者/仓库签名已验证。 |
| Windows DNS-SD | 操作系统组件 | Windows.Devices.Enumeration；Windows 11 为产品目标，本轮 25H2 实测；较低 TFM 是 API 选择，不代表验证了 Windows 10。 |
| rclone | 1.75.1 Windows amd64 | [官方](https://rclone.org/)、[源码](https://github.com/rclone/rclone)；本地安装包分发已核验二进制，SHA-256 `033EEE51C9AD47C2DE2624B6674D355274BCD6CF0027A5F85DB4437BA24AE81C`，MIT 许可见 [原文](../licenses/rclone-1.75.1-LICENSE.txt)。 |
| WinFsp | 2.1.25156.ddca7bd | [官方源码/许可](https://github.com/winfsp/winfsp)。本地安装包内置未修改的官方 MSI，仅在未检测到系统组件时显示管理员确认后运行；SHA-256 `073A70E00F77423E34BED98B86E600DEF93393BA5822204FAC57A29324DB9F7A`，NAVIMATICS LLC 数字签名有效。GPLv3 FOSS Exception 原文见 [许可](../licenses/winfsp-2.1-LICENSE.txt)，客户端保留所需归属与项目链接；卸载 PhoneBridge 不移除共享驱动。 |
| Inno Setup | 7.1.0 | 只用于生成 Windows 安装程序；使用官方 Pyrsys B.V. 有效签名编译器，许可原文见 [文件](../licenses/innosetup-7.1.0-LICENSE.txt)。不作为客户端运行依赖。 |
| Windows DPAPI / 文件 ACL | 操作系统组件 | P1-005 CurrentUser + UI_FORBIDDEN，直接调用 crypt32/advapi32/kernel32；不新增安全存储包，不使用 LocalMachine。 |
| Windows Job / CreateProcessW | 操作系统组件 | 创建时 JOB_LIST + HANDLE_LIST，隐藏运行并限定继承句柄；本轮仅 Windows 11 x64 实测。 |

直接/传递测试依赖的版本与 contentHash 在各 `packages.lock.json`；Windows SDK targeting pack 由 SDK 框架解析单独固定，包 SHA-512 见验收 manifest。Windows SDK 与 MSTest 包的作者/仓库签名通过 `dotnet nuget verify --all`。固定 SDK 与 locked restore 共同保证此次解析可重现。

MSTest 的传递测试工具包含 Microsoft.Testing.Extensions.Telemetry / ApplicationInsights。它们只在测试输出，不进入诊断/业务输出；验证脚本设置 `DOTNET_CLI_TELEMETRY_OPTOUT=1` 与 `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`。项目不添加应用遥测、数据分析或网络上报功能。NuGet 已知漏洞查询未报告受影响包，不替代源码/协议安全审查。

评估但未采用：Zeroconf 3.7.16（MIT）与 Makaretu.Dns.Multicast 0.27.0。原因与系统 watcher 的移除限制见 [ADR-015](../DECISIONS.md)。它们不在 Windows 构建依赖中。

当前正式第三方侧载安装包将本表、项目 NOTICE、GPL、rclone、WinFsp、Inno Setup、.NET 许可和第三方通知一并放入安装目录。对外分发时必须同时提供默认交付目录中的该版本完整对应源码 ZIP；签名或依赖版本变化后必须重新生成并核对材料。
