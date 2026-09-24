# 配对协议核心

`PairingSession` 实现 [PBNG Pairing v1](../../../docs/PAIRING.md) 的 8 帧状态机、库证明验证和 transcript/HKDF。依赖 BouncyCastle.Cryptography 2.7.0，版本与 NuGet 锁文件固定。

Windows 用 `CreateWindows(windowId, code)`；协议测试也可创建 Android 角色。按交替顺序调用 `CreateNextFrame` / `AcceptFrame`，错误后丢弃实例。`FrameAccumulator` 为一个预期类型接收分片；EOF 必须调用 `EndOfInput`，同帧尾随数据、后续多余帧均拒绝。宿主须在有界工作者运行密码学运算；CancellationToken 在操作前后检查，不保证中断库内部一次模幂。

`Confirmed` 只表示本端完成 PAKE。宿主确认最终帧已发送/接收并检查协议结束后，才能 `TakeConfirmation`；其 `CandidateCaDer` **尚未解析/验证为 CA**。验证证书、当前网络端点、TLS、DPAPI 保存与手机批准均由后续集成完成，不得直接生成挂载请求。grant 只用于本次 HTTPS 授权，并非文件访问 token。

会话和结果均须 Dispose；CopyGrant 的返回副本由调用方及时清零，调用方传入的码数组也由其清零。库无完整 abort/zeroize API，释放引用不保证 GC 对象立即擦除。原始异常不向外传播，只输出固定协议错误；不提供日志/网络/文件/序列化凭据功能。

协议行为测试位于 `windows/tests/PhoneBridge.Pairing.Tests`；合成测试宿主 PairingFixture 仅供管道互操作，不是产品入口。复验入口见 [核心测试说明](../../../tests/contracts/pairing/CORE-TESTING.md)。
