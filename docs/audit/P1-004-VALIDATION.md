# P1-004 两端配对核心验收

结论：限定核心任务通过。新增 C# 与 Kotlin/JVM 的 8 帧状态机、分片校验、J-PAKE 适配、身份上下文与 HKDF grant；未接手机、网络服务或安全存储，**仍不能完成用户首次配对或日常挂载**。

## 实现与复核

- [Windows 核心](../../windows/src/PhoneBridge.Pairing/README.md)加入正式 solution，BC 2.7.0 精确版本与锁文件。Android 为独立 [Kotlin 核心](../../android/pairing-core/README.md)，未升级/部署实验 APK。
- 严格交替发送/接收 8 帧；头部先检查 magic/type/固定长度，再分配有界缓冲。输入快照与 transcript 一致；群元素、gx2/A 非 1、标量范围在库证明验证前检查，证明检查不能被结构检查代替。
- Android 必须验证 Windows 第三轮后才生成最终发送帧；C# 在库状态推进前使用固定 32 字节 MAC 恒定时间比较。任意错误/取消/乱序都终止，不能取半完成结果或重用 participant。
- grant 从固定宽度 K 与完整 transcript 派生，结果须单次领取并释放。补强输入快照、派生后取消检查和 CopyGrant/Dispose 并发保护；调用方副本仍须清零，库 immutable BigInteger/密码副本不能保证立即擦除。
- 核心不包含控制台日志、socket、HTTP 或持久化调用；仅测试宿主通过管道交换公有帧与合成 grant 的散列。结果名 CandidateCaDer 明确表示还需 X.509 校验，不直接构造挂载信任或文件授权。

这是实现者对协议、异常和材料生命周期的代码复核；不是独立密码学安全审计。复核后的最终源码与构建散列见 [manifest](p1-004/manifest.json)。

## 实际验证

复验命令和环境变量见 [CORE-TESTING](../../tests/contracts/pairing/CORE-TESTING.md)。本机 .NET SDK 10.0.401、JDK 21.0.10、Gradle 9.3.1、Python 3.12.14。

| 检查 | 最终结果 | 范围 |
| --- | --- | --- |
| .NET locked restore / Release build | 退出 0；0 warnings / 0 errors | solution 加入配对项目与测试宿主 |
| Windows 测试 | 124 通过、0 失败、0 跳过 | 41 发现 + 33 挂载/进程 + 50 配对；只在本机构建/行为测试，没有重跑真机 |
| Kotlin 严格锁定/校验构建与 JUnit | 15 通过、0 失败、0 跳过 | JVM 运行；JVM 1.8 字节码和 Java 8 API 编译约束 |
| C# ↔ Kotlin 完整帧管道互操作 | 59 通过，入口退出 0，无强杀 | 4 次同码/前导零；55 次错误码或帧/身份/证明攻击拒绝 |
| 来源/结构/历史保全 | 见 final-check | 检查链接、结构、源码/产物绑定、固定上游与实验 APK/补丁未改变 |

结果见 [unit-results](p1-004/unit-results.json)、[core-interop-results](p1-004/core-interop-results.json)、[final-check](p1-004/final-check.json)。[公开合成 8 帧](p1-004/core-public-frames.json)中 CA 来自项目合成证书，代码仅允许固定测试短码，无实际手机材料。

互操作拒绝包括每个位置的错误 magic/type/长度、尾随/截断、CA/端口/attempt 修改、suite/window/client 不符、反射/跨会话重放、gx2=0/1、标量越界、第二轮证明与双方第三轮 MAC 修改。篡改最后一帧时，Android 已验证自身收到的原消息而退出 0、Windows 拒绝退出 2；测试要求不能出现双方成功，未将单边确认当完整通过。耗时仅为本机测试，不作 Android 性能承诺。

## 依赖与失败记录

Kotlin 2.4.0 与 Gradle 9.3.1 位于 [官方兼容范围](https://kotlinlang.org/docs/gradle-configure-project.html)，版本在 Maven Central 核实。BC 两端沿用 P1-003 固定版本；C# NuGet 作者/仓库签名证据沿用该轮。Gradle 严格 lock 与 SHA-256 verification 保存实际解析；首次生成校验清单不是独立签名认证。BC Java 与同站 SHA-256 一致；Kotlin stdlib/annotations 官方目录只提供 SHA-1，核对该校验并另记录 SHA-256 固定值，未宣称 SHA-1 等同现代签名验证。[运行依赖](p1-004/runtime-dependencies.json)、[许可通知](../../NOTICE.md)。

保留失败与修正：

1. 首次 Gradle 退出 1：新增空 `kotlin.build.report.output` 被插件拒绝，移除这一无效配置后编译成功；没有更换框架。
2. 首次 .NET build 退出 1：MSTest 分析器要求显式并行策略；测试程序集补充 DoNotParallelize，随后构建通过。
3. 依赖材料辅助脚本先假定每个 Maven 包都有 .sha256，遇 Kotlin 404；核查官方目录后按实际发布算法核对。文件名筛选还曾误选三个 BC class，已在本次新建目录中精确移除，最终只保留许可文本。此脚本失败不涉及产品协议测试。

首轮 48 项 C#、13 项 Kotlin、59 项管道已通过；代码复核补强后执行上表最终测试。日志散列及失败分类见 [build-results](p1-004/build-results.json)，未把失败入口改写为原始成功。

## 尚未覆盖

网络 read/write 截止时间、120 秒窗口/5 次预算、实际连接半关闭/EOF、Android API 26 的 D8/运行/性能、CA 解析/严格 HTTPS 授权、DPAPI/Keystore、手机批准、重启恢复/撤销、mDNS v3、真实 rclone 新凭据和 UI 均未验证。15/50/59 不能替代 [22 项产品配对验收](../../tests/contracts/pairing/contract.json)，该矩阵仍全部为产品未运行。

下一任务为 [P1-005 Windows 受保护配对记录](../tasks/completed/P1-005-pairing-storage.md)，先落实发送 token 前必须持久保存 Pending 的条件。
