# Kotlin/JVM 配对核心

独立原生 Kotlin 模块，尚未接入 Android App。只实现 [PBNG Pairing v1](../../docs/PAIRING.md) 的固定帧、证明、三轮状态与 HKDF；不包含 Service、UI、socket、HTTPS 或安全存储，不改实验 APK。

固定 Kotlin 2.4.0、BC Java 1.86；既有 Gradle 9.3.1 / JDK 21 构建，JVM 1.8 + `-Xjdk-release=8` 限制本模块 Java API。Gradle lock 与 verification-metadata 保存固定解析结果和 SHA-256，普通复验不得加 `--write-locks` / `--write-verification-metadata`。工具配置还锁定了 ABI 兼容检查依赖 2.4.20，实际编译器与 runtime stdlib 为 2.4.0。Android API 26 的真实加载/D8/运行仍需后续验证。

手机宿主用 `PairingSession.createAndroid`，窗口/短码由后续限时服务提供。正常按 `acceptFrame` / `createNextFrame` 交替处理；FrameAccumulator 支持分片且只接受一个预期帧。宿主负责读取/发送截止时间、EOF/尾随帧、同窗口预算与单工作者调度；`cancel()` 在协议边界生效，不中断库内一次模幂。取消后关闭实例，不得重用。

最终发送完成后 `takeConfirmation()` 仅提供 PAKE 绑定的候选 CA 与短命 grant，仍须 X.509/严格 HTTPS/用户批准。结果 close 清除内部 grant；复制的 grant、调用方码数组由调用方清零。库内部 immutable BigInteger/密码副本仅能释放引用，不能声称可靠擦除堆内全部副本。

本轮测试是 JDK 上的 JVM 行为，不是手机配对。JUnit 与管道测试入口见 [CORE-TESTING](../../tests/contracts/pairing/CORE-TESTING.md)。许可见 [第三方通知](../../NOTICE.md)。
