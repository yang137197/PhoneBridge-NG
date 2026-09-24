# P1-003 配对设计验收

结论：设计任务完成；D-03 的协议选择明确为 ADR-017。14 项离线 Java/C# 库互操作与 11 项设计向量检查通过。**未实现或验收产品配对、安全存储、批准/撤销及新认证挂载；不表示 MVP 可用。** 本轮没有手机操作、网络监听或系统设置变更。

## 交付与复核

[PAIRING](../PAIRING.md) 定义一次性码、限时公共 PAKE 通道、CA/角色绑定、严格 HTTPS 授权、每电脑独立随机 token、DPAPI/Keystore 边界、批准/取消竞争、回执丢失与撤销。固定帧和合成向量见 [contract.json](../../tests/contracts/pairing/contract.json)，22 项产品验收全部保留为 `required-not-run-on-product`。

源码复核发现两端 Round3 MAC 采用有符号 BigInteger，契约固定为 32 字节二补码；最终随机运行实际覆盖 9 个负值 MAC。C# 2.7.0 ValidateMacTag 使用 BigInteger.Equals，探针先以库 CalculateMacTag 算预期值，再以 FixedTimeEquals 比较固定字节，成功后调用库推进状态。Java 1.86 使用固定宽度比较。这不证明整个 BigInteger/模幂实现为常数时间，也不是独立密码学安全审计。

恢复边界复核：Windows 先持久保存 Pending 才能发送 token；Android 批准先持久提交；丢回执用新 token 回读身份。撤销保存失败停止共享并报告未完成，不能保证尚未落盘的撤销在重启后有效。成功撤销必须经持久提交及活动写提交屏障，仍待实际故障注入验证。

## 依赖与来源

| 项目 | 固定版本/来源 | 实际核验 |
| --- | --- | --- |
| C# 库 | BouncyCastle.Cryptography 2.7.0，tag release-2.7.0，commit `4007498b13582d90ee1eda5d9920c324428b98b3` | NuGet restore/build 成功；`dotnet nuget verify --all` 退出 0，作者和 NuGet 仓库签名通过；packages.lock.json 已保存 |
| Java 库 | bcprov-jdk18on 1.86，tag r1rv86，commit `eacda831fbe15b272933e40841d8b0827b556fae` | Maven Central HTTPS 获取，与同站 SHA-256 一致；未进行独立 PGP 验签 |
| 环境 | .NET SDK 10.0.401 / runtime 10.0.12；既有 JDK 21.0.10；Python 3.12.14 | C# build 0 warnings / 0 errors，javac 退出 0；探针是本机 JVM，并非 Android 运行时 |

包散列及源码来源见 [dependencies](p1-003/dependencies.json)，本次源码/构建/材料散列见 [manifest](p1-003/manifest.json)。参考：[BC C# 官方下载](https://www.bouncycastle.org/download/bouncy-castle-c/)、[Java 官方下载](https://www.bouncycastle.org/download/bouncy-castle-java/)、[Maven 制品](https://repo.maven.apache.org/maven2/org/bouncycastle/bcprov-jdk18on/1.86/)、[C# 固定源码](https://github.com/bcgit/bc-csharp/blob/release-2.7.0/crypto/src/crypto/agreement/jpake/JPakeUtilities.cs)、[Java 固定源码](https://github.com/bcgit/bc-java/blob/r1rv86/core/src/main/java/org/bouncycastle/crypto/agreement/jpake/JPAKEUtil.java)、[MIT 许可](https://www.bouncycastle.org/about/license/)。仅在独立测试项目引用，未导入第三方源码到产品，未升级 Android 实验的 1.78；正式引入需保留完整许可通知。

密码/传输依据：[RFC 8236 J-PAKE](https://www.rfc-editor.org/rfc/rfc8236.html)（Informational）、[RFC 5869 HKDF](https://www.rfc-editor.org/rfc/rfc5869.html)、[RFC 7617 Basic](https://www.rfc-editor.org/rfc/rfc7617.html)。存储边界依据：[CurrentUser DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)、[Android Keystore](https://developer.android.com/privacy-and-security/keystore)、[Android Backup](https://developer.android.com/identity/data/autobackup)。官方规则说明能力与限制，不能替代本应用的恢复/损坏/备份实验。

## 运行结果

从项目根使用既有 `.audit/venv/Scripts/python.exe`，按顺序执行：

```text
tests/contracts/pairing/interop/run.py
tests/contracts/pairing/build_vectors.py
tests/contracts/pairing/verify_contract.py
```

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| 8 次随机同码 + 全零前导码 | 9 次两端确认、相同 K hash、进程均退出 0 | [interop-results](p1-003/interop-results.json) |
| 错码、上下文不同、gx2=0、证明改动 | 4 次拒绝，两端退出 2，无强杀/异常 stderr | 同上 |
| Android MAC 被改 | Windows 拒绝退出 2；Java 已验证其收到的原消息，退出 0，不能把单边成功当双方确认 | 同上 |
| 编码/KDF 一致性 | 11 项通过；包含 RFC 5869 case 1 已知答案、Hello/角色、固定 MAC/帧长度与未验收状态 | [contract-results](p1-003/contract-results.json) |
| 文档/源码证据 | 链接与 JSON/Python/XML 可解析、唯一后续任务、P1-002 产品源码/二进制未改、固定上游干净 | [final-check](p1-003/final-check.json) |

最终入口退出 0。此前探针运行也通过，复核后仅为退出码/强杀检查补证再运行；不是反复重跑旧产品测试。交互程序只接受固定合成码，公开 payload 经 stdin/stdout；没有真实密码/文件/CA/私钥。保留的 [公开合成 payload](p1-003/synthetic-public-payloads.json) 只用于复核库编码，其 context 是合成 hash，**不是产品 Hello transcript**。

首次文档检查退出 1：检查器在写出自身 final-check.json 之前，误把该结果链接记为缺失；检查器已修正为写出结果后验证自身文件。该失败不涉及产品或互操作断言，原始结果保留在本地 `.audit/p1-003/final-check-first.json`。

## 限制与后续

探针双方提前生成第三轮，便于双向故障注入；它不是正式网络时序。未运行完整 wire 网络、窗口计时/限流、手机 UI 确认、真实严格 HTTPS 授权、DPAPI/Keystore 新存储、故障恢复/撤销屏障、新 token 的 rclone 挂载、Android API 26 性能或独立安全审计。已有 P1-002 真机证据不能转记为本轮配对成功。正式产品源代码、Windows solution 与 Android APK 均未改变。

本轮交接任务：[P1-004 配对协议核心与帧校验](../tasks/completed/P1-004-pairing-core.md)（现已完成限定核心），只实现并验证两端纯协议核心，之后再接网络授权和安全存储。
