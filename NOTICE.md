# 来源与修改说明

PhoneBridge NG 的 Android 实验补丁基于 [ysachin26/PhoneBridge](https://github.com/ysachin26/PhoneBridge)，作者 Sachin Yadav，固定基线 `a378fec40561a4d18be2334f4de92ee02a0e7d0c`。

原始 LICENSE 声明 GPL 第 3 版或任意更新版本，原文完整保留于 [licenses/UPSTREAM-LICENSE.txt](licenses/UPSTREAM-LICENSE.txt)；完整 GPL 第 3 版文本见 [LICENSE](LICENSE)。本项目基于上游的修改同样按 GPL-3.0-or-later 提供。

2026-09-19，P0-004：修改 WebDAV 文件 Range 响应和请求正文处理，加入对应测试。补丁与说明保存在 `android/patches`；未修改基线副本、未删除原作者声明。完整参考源码仍来自上述固定提交；修复构建副本位于被忽略的 `.audit/p0-004-worktree`。

此阶段不发布安装包或 APK；本机实验 APK 使用 Debug 签名，仍包含未修复的上游风险。正式分发前必须提供完整对应源码、构建材料及依赖许可，不能仅把一个补丁当作安装包的完整对应源码。

2026-09-19，P0-005：增加受约束的共享路径/目录句柄操作、暂存上传提交和对应测试；修改 WebDAV/元数据输出与共享目录失败启动处理。累计补丁包含 P0-004，独立构建副本位于 `.audit/p0-005-worktree`，修改文件中保留来源和 GPL-3.0-or-later 声明。

根 LICENSE 原样取自 [SPDX license-list-data](https://github.com/spdx/license-list-data/blob/main/text/GPL-3.0-or-later.txt)，SHA-256 为 `fb981668c18a279e285fc4d83fba1e836cc84dd4daa73c9697d3cfd2d8aca6e0`。

2026-09-19，P0-006：修改 MainActivity 的应用内状态广播注册、拆分 API 27 主题资源并添加 Android 兼容性原生测试。累计补丁包含 P0-004/P0-005，独立构建副本为 `.audit/p0-006-worktree`，依赖与上游作者声明保留。

2026-09-19，P0-007：compileSdk/targetSdk 提升至 36，修改前台服务类型和系统重建处理、系统栏 Insets、开机日志及原生测试。累计补丁包含 P0-004–006，独立副本为 `.audit/p0-007-worktree`，保留 GPL-3.0-or-later 和作者声明；未更改依赖或协议/存储源文件。

2026-09-19，P0-009：修改 TLS 身份与动态地址证书、Keystore 密钥保存、失败停止共享及身份状态字段，增加中英文错误资源与相应测试。累计补丁包含 P0-004–007，独立副本为 `.audit/p0-009-worktree`；保留许可和作者声明，无正式发布。

2026-09-19，P1-004：新增本项目的 C#/Kotlin 配对协议核心；不复制第三方密码学源文件，调用下列固定依赖。实验 APK 未升级。包来源与散列见该任务审计；构建/测试依赖不构成产品功能。

| 依赖 | 用途与来源 | 许可材料 |
| --- | --- | --- |
| BouncyCastle.Cryptography 2.7.0 | Windows J-PAKE，[NuGet](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0) | 包内原样 [MIT](licenses/pairing-bouncycastle-csharp-LICENSE.md) |
| bcprov-jdk18on 1.86 | Kotlin J-PAKE/HKDF，[Maven Central](https://repo.maven.apache.org/maven2/org/bouncycastle/bcprov-jdk18on/1.86/) | JAR 内原样 [MIT](licenses/pairing-bcprov-jdk18on-LICENSE.md) |
| Kotlin stdlib 2.4.0、JetBrains annotations 13.0 | JVM 运行依赖，JetBrains；各 Maven POM 声明 Apache-2.0 | [Apache-2.0 文本](licenses/pairing-kotlin-LICENSE.txt)，原样取自 [Kotlin v2.4.0](https://github.com/JetBrains/kotlin/blob/v2.4.0/license/LICENSE.txt) |

Kotlin/Gradle 编译工具和 JUnit 4.13.2（EPL-1.0）、Hamcrest 1.3（BSD-3-Clause）、MSTest 4.0.2（MIT）只用于开发/测试；锁文件记录实际解析版本，不将测试宿主作为用户安装包。完整发布材料仍须随最终分发范围核对。

2026-09-19，P1-006：新增独立 Android 受保护配对存储，不复制第三方密码学源文件，AES-GCM 调用系统 Android Keystore/JCA。运行依赖为 Kotlin stdlib 2.2.10、JetBrains annotations 13.0（Apache-2.0）；[Maven POM](https://repo.maven.apache.org/maven2/org/jetbrains/kotlin/kotlin-stdlib/2.2.10/kotlin-stdlib-2.2.10.pom)声明许可，[原样许可文本](licenses/credentials-kotlin-LICENSE.txt)来自 [Kotlin v2.2.10](https://github.com/JetBrains/kotlin/blob/v2.2.10/license/LICENSE.txt)。未更改 P1-004 的 Kotlin 2.4.0 依赖。

AGP 9.1.1、AndroidX test ext:junit 1.1.5 / runner 1.5.2 及传递 AndroidX 依赖均为构建/测试用途（Apache-2.0）；来源为 Google Maven/Maven Central，实际版本与 SHA-256 分别固定在模块 gradle.lockfile 与 verification-metadata.xml。测试 runner 不进入产品 AAR，完整分发许可清单须按最终 App 依赖图再次核对。

2026-09-19，P1-007：建立 `android/app`。从上述固定上游与累计 P0-009 修改副本导入 DeviceCertificates、TlsIdentityStore、TlsHelper、SharedPath、SharedStorage、HttpByteRange、XmlResponseBuilder、RequestBody 八个文件；来源和导入散列保存在任务证据中。修改身份初始化接口以连接新的配对存储，新增服务、授权路由和界面仍按本项目 GPL-3.0-or-later 提供；没有导入上游明文密码回退路径。

NanoHTTPD 2.3.1 官方源文件来自 [Maven Central sources JAR](https://repo.maven.apache.org/maven2/org/nanohttpd/nanohttpd/2.3.1/nanohttpd-2.3.1-sources.jar)，JAR SHA-256 `71b57b309dd19ca7bfceb3b0fa97ac9048ab2cb902a2607373633150f1997a67`。其 Java 源码以原 BSD-3-Clause 许可保存在工程中，保留完整版权头及[通知文本](licenses/android-nanohttpd-LICENSE.txt)。本项目修改严格请求解析、重复头拒绝、固定错误、Locale 和日志；服务宿主另外限制并发和期限，没有更换 HTTP/WebDAV 协议。

正式 Android 工程统一使用 bcprov/bcpkix/bcutil-jdk18on 1.86，分别用于 PAKE、证书构造及依赖工具；来源为 [Bouncy Castle Maven](https://repo.maven.apache.org/maven2/org/bouncycastle/)。三个 JAR 内 LICENSE.md 内容相同，SHA-256 `0e01f1549c9022f406392ac2947d32223b7c2e977d21ea2f8c182fdeb4dae5fd`，原样文本见[已保存 MIT 通知](licenses/pairing-bcprov-jdk18on-LICENSE.md)。打包合并重复许可而不删除。Kotlin stdlib 2.2.10 许可沿用上述存储模块材料；NanoHTTPD 的两个默认 MIME 属性资源未导入，文件类型由既有 XmlResponseBuilder 明确给出。旧 P0 APK/BC 1.78 和独立 Kotlin 2.4 核心构建不改变。

2026-09-22，P1-024：生成 PhoneBridge NG 0.1.0 本地预览 Windows 安装程序和 Android 侧载 APK，不作公开 Release 或应用商店发布。Windows 包自包含 .NET 10.0.12，分发固定 rclone 1.75.1，并内置未修改、数字签名有效的 WinFsp 2.1.25156 官方 MSI；缺少 WinFsp 时才由用户确认安装。相关原始许可分别保存在 `licenses/rclone-1.75.1-LICENSE.txt`、`licenses/winfsp-2.1-LICENSE.txt` 和随包的 .NET 许可/第三方通知。安装界面保留 WinFsp 归属与项目链接。

2026-09-23，P1-045：正式第三方侧载交付沿用上述依赖和安装边界，移除 Windows 安装器显示版本名与文件描述中的 `local preview/local installer` 开发标签；未增加商业 Authenticode 签名，manifest 继续如实标记 `NotSigned`。

Windows 安装程序由 Inno Setup 7.1.0 生成，编译器为 Pyrsys B.V. 有效签名版本，许可见 `licenses/innosetup-7.1.0-LICENSE.txt`。Android `local-test` APK 使用当前开发用户已有的标准 Debug 测试证书签名，只用于同签名覆盖安装与本机验收；签名私钥不进入源码或安装包。Windows 安装程序与 APK 均未使用正式产品代码签名，不能表示为正式签名发行版。完整构建边界见 `docs/LOCAL_DELIVERY.md`。

2026-09-23，P1-035：交付脚本新增与 `LocalTest` 分离的 `ThirdPartyRelease` Android 签名模式，不增加运行依赖。发行模式只接受项目目录外的专用 keystore，通过进程环境读取密码，并在签名前后绑定同一证书 SHA-256；manifest 不记录密钥位置、别名、密码或环境变量名称。一次性测试密钥只用于验证构建入口，验收后已删除；本项目尚未生成正式长期私钥。
