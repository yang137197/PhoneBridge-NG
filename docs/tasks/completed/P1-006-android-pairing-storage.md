# P1-006 Android 受保护配对记录

- 状态：已完成限定任务，2026-09-19；不代表完整产品完成。
- 目标：落实 ADR-017 的 Android Keystore AES-GCM 配对记录集，在手机承认批准/撤销前可靠保存授权状态，逐电脑验证值不包含可还原长期 token。
- 范围：独立、可供正式 Android 工程引用的 Kotlin Android 存储模块；严格有界记录模型、绑定 CA/client/token 的验证值、Keystore 密钥、credential-protected noBackupFilesDir、原子密文提交、批准/撤销/恢复/容量与异常状态；API 26/36 本地模拟器原生验证。
- 不做什么：不实现 HTTP/WebDAV 授权、PAKE 网络监听、Android 配对 UI、Windows 挂载集成；不自动迁移实验明文密码，不替换备用机现有 APK，不发布/更新安装包，不清空或改变本机账号/系统策略。
- 涉及文件：ARCHITECTURE、Android 模块/构建依赖、原生与模型测试、PAIRING/SECURITY/测试及任务记录。

## 实现

先读 [PAIRING](../../PAIRING.md) 第 6、7 节、ADR-014/017 与现有 P0 Keystore 补丁；核实官方 Android 构建兼容组合，选择最小独立模块，不为引入存储重构已验证 APK。实施前固定记录 schema、AAD、nonce/tag、容量与状态一致性。密钥只存 Keystore，记录仅含随机 token 的绑定验证值和授权元数据。

首次初始化须显式区分 key/身份/记录的存在状态；既有 key 而记录丢失或解密失败不能自动初始化为可共享。写失败禁用当前实例授权，重新加载按真实持久状态恢复，不能把内存拒绝宣称成持久撤销。调用方取得验证材料和实际请求提交屏障的边界必须清楚，HTTP 集成由后续任务落实。

## 测试

实际 AES-GCM Keystore 往返；随机 nonce、错误 AAD/key/CA、篡改/截断/超长、绑定身份/重复 client/容量、批准与撤销持久失败、进程重启、文件/alias 丢失、无明文 token/密钥与备份配置。优先 API 26/36 已有空白模拟器，记录实际 KeyInfo，不推断 StrongBox。模拟器结果不能代替 OEM 备份迁移/真实手机重启或硬件安全证明。

## 验收结果

独立 Android library、严格锁/散列、Debug/Release 构建和 Lint 通过；API 26/36 各 26 项原生与 3 项进程恢复通过。首次编译/Lint 失败、原生 23/25 及 fd 泄漏修复证据保留；两平台硬链接创建被系统拒绝，不标作库内多链接分支覆盖。实现者已复核四个产品源文件，未作独立安全审计。

备用机 APK、Windows 模块、配对核心和上游基线保全。模拟器/测试包已停止；未验证真机/OEM 备份迁移、首次解锁、硬件密钥、断电/磁盘满或网络授权。完整结果见 [P1-006 验收](../../audit/P1-006-VALIDATION.md)。

下一任务：[P1-007 Android 首次配对与授权服务](P1-007-android-pairing-service.md)。
