# P1-004 配对协议核心与帧校验

- 状态：已完成限定核心任务；产品配对仍未完成。
- 目标：将 ADR-017 的纯协议部分实现为可测试的 C# 与 Kotlin/JVM 核心，为后续受限网络会话和安全存储提供共同契约。
- 范围：固定 Hello/帧解析、角色与上下文绑定、Bouncy Castle 适配、三轮顺序与失败终止、固定编码、transcript/HKDF grant；限制内存/长度/非法值。依赖锁定与许可记录。测试项目通过内存/管道传输互操作，不接真实手机。
- 不做什么：不把离线核心称为完整配对；不启动 LAN 监听，不实现 UI、HTTP 授权、DPAPI/Keystore、撤销、自动重连或大规模 Android 工程迁移，不升级/部署当前实验 APK。
- 涉及文件：ARCHITECTURE、Windows 配对核心/行为测试、Android 独立 Kotlin/JVM 协议核心/测试、tests/contracts/pairing、依赖通知和本任务记录。

## 实现

建立 PhoneBridge.Pairing 与独立 Kotlin/JVM pairing-core，固定 8 帧顺序和边界、J-PAKE 适配、完整 transcript/HKDF、取消与失败终止、结果单次领取/释放。Kotlin 输出 JVM 1.8 并用 Java 8 API 约束；尚未验证 Android API 26 运行。未复制旧探针的同时发送第三轮时序，未输出长期凭据。版本/校验与第三方许可已记录。

## 测试

最终 Windows 124 项（含配对 50 项）、Kotlin 15 项、两端 59 项管道互操作通过。已复核握手顺序、身份/角色绑定、长度/元素/标量检查、取消/错误终止、秘密副本与日志边界，并补强输入快照和结果释放。源码/产物、失败入口与命令见 [验收](../../audit/P1-004-VALIDATION.md)。网络计时/连接预算、真实 Android 和授权存储仍未运行。

## 验收结果

限定范围通过；未连接手机、开启网络监听或修改实验 APK。尚无可用的正式配对功能，产品验收 22 项维持未运行。唯一下一任务 P1-005 Windows 受保护配对记录，先完成发送长期 token 前的持久 Pending。
