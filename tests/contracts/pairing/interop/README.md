# 离线 J-PAKE 库互操作探针

这是 P1-003 方案证据，不是产品配对服务。无网络 socket、手机连接、真实码、真实 CA/凭据、DPAPI 或 Keystore 写入。程序只接收三个固定合成码，并经 stdin/stdout 传递公开库 payload；真实密钥只在短命进程内存，结果仅比较其散列。

固定依赖：C# BouncyCastle.Cryptography 2.7.0（release-2.7.0，4007498b13582d90ee1eda5d9920c324428b98b3）；Java bcprov-jdk18on 1.86（r1rv86，eacda831fbe15b272933e40841d8b0827b556fae）。[官方 C# 下载](https://www.bouncycastle.org/download/bouncy-castle-c/)、[官方 Java 下载](https://www.bouncycastle.org/download/bouncy-castle-java/)、[MIT 许可](https://www.bouncycastle.org/about/license/)。NuGet 作者/仓库签名已验证；JAR 来自 Maven Central，并与同站 SHA-256 一致。此校验不等于独立安全审计；Android APK 仍用旧 1.78，未升级。

用固定 SDK `.audit/tools/dotnet-10.0.401/dotnet.exe` 构建 `dotnet/PairingInterop.csproj`（后续 restore 用 `--locked-mode`）；NuGet 缓存沿用 `.audit/nuget-packages`，CLI 遥测退出。用既有 JDK 21 javac，classpath 为 `.audit/p1-003/bcprov-jdk18on-1.86.jar`，输出到 `.audit/p1-003/java-classes`；只在官方来源和散列核对后编译/执行。然后从项目根用 `.audit/venv/Scripts/python.exe tests/contracts/pairing/interop/run.py`。

14 项包含 8 组随机同码握手、全零数字码、错误码、上下文不同、gx2=0、证明被改、服务端确认被改。MAC 用固定 32 字节有符号整数往返，群元素/标量用固定无符号编码。探针的固定上下文是合成输入，不包含实际 Hello；双方提前生成公开 round3 以便双向故障注入，不能复制其发送时序作为产品服务端流程。

通过仅证明这两个锁定版本的算法/编码互操作与列明拒绝行为；不证明 Android API 26 性能、完整帧协议、定时/重放/批准、TLS/HTTP、持久化和撤销。实施必须按 [PAIRING.md](../../../../docs/PAIRING.md) 的顺序、预算和权限边界。`build_vectors.py` 生成合成编码/KDF参考，`verify_contract.py` 对 RFC 5869 公开已知答案及规范一致性作检查；22 项产品验收均仍为 required-not-run-on-product。
