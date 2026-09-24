# Android 受保护配对记录

独立 Android library，minSdk 26、compileSdk 36；使用系统 Android Keystore AES-256-GCM 保护 [PAIRING](../../docs/PAIRING.md) 的每电脑验证值与授权元数据。当前不包含 App、配对窗口、HTTP、WebDAV、UI 或后台服务，尚未接入实验手机。

## 调用边界

宿主仅在**本次新建 CA 身份**的事务中调用 `PairingStore.initializeForNewIdentity(context, caSha256)`。模块检查 key/记录都不存在，但不拥有 CA 私钥，也不能证明传入身份确为新建。既有身份一律 `openExisting`；加载失败不能回退初始化、替换密钥或抹掉记录。需要 credential-protected Context 与开机后首次解锁；之后息屏无需每次认证。

`snapshot` 返回不可修改的元数据与正修订号。手机明确批准后调用 `approve(clientId, clientName, token, expectedRevision)`，默认 SAFE；持久提交和回读成功后才能承认批准。`revoke` 用于远端 self-revocation，保留 Revoked 记录并禁止同 client_id 再批准；P2-004 新增的 `remove` 只用于手机用户明确的本地移除，会在 revision 校验后从库中精确删除整条记录，允许以后作为全新配对重新批准。`updateMode` 只更新 Active。最多 16 Active / 128 总记录；满时明确返回 CAPACITY，不自动删除记录。修订号冲突需重新加载并重新判断意图，不能盲目重放批准。

每个请求调用 `authenticate`；返回值只是该请求的授权快照。文件写入提交使用 `withAuthorizedCommit` 的短回调，在撤销共用锁内重新验证并检查当前模式。回调不能执行长网络传输、嵌套存储操作或等待 UI；模式执行、停止共享、活动流取消由后续宿主完成。存储成功不等于全链路撤销完成。所有方法同步，放在受控后台工作者上。

NEEDS_REPAIR / STORAGE_FAILURE 禁用同进程中该库的全部实例。不能在同进程创建新对象来绕过；新进程读取实际磁盘状态。BUSY、LOCKED、输入/认证/修订冲突不会自动修改记录。调用方持有的 token 副本自行清零；库仅短暂复制，不能承诺 JVM/密码提供者内部所有副本可靠擦除。

## 格式与文件

固定目录 `noBackupFilesDir/pairings-v1`（0700），记录 `records.bin` 和 `.lock`（0600）。校验 UID、类型、文件单链接、O_NOFOLLOW，原子设置 CLOEXEC；文件锁串行合作进程，同进程共享 gate。仅密文写入随机独占临时文件，sync → 同目录 rename → 目录 fsync → 解密回读。失败可能发生在替换前或后，因此不能把失败撤销声称为持久成功。进程崩溃遗留的密文临时文件不会加载，当前无自动清理。

| 层 | 固定格式（整数均 big-endian） |
| --- | --- |
| 外层 | `PBE1` 4 bytes、版本 1 byte、提供者随机 nonce 12 bytes、GCM 密文与 16-byte tag；总长度 80..65536 |
| AAD | ASCII `PhoneBridge NG\|android-records\|v1`、00、CA SHA-256 raw32 |
| 明文头 | `PBS1` 4 bytes、版本 1 byte、CA raw32、revision int64、count uint16，共 47 bytes |
| 每条记录 | client raw16、验证值 raw32、模式 1 byte、状态 1 byte、名称字节数 uint16、严格 UTF-8 名称；按 client_id 严格递增 |
| 模式/状态 | READ_ONLY=1 / SAFE=2 / READ_WRITE=3；ACTIVE=1 / REVOKED=2 |

名称 1..128 Unicode scalar、UTF-8 <=256 bytes、无控制/格式字符；拒绝非法 UTF-16/UTF-8、重复 client、未知枚举/版本和尾数据。验证值按契约 SHA-256 绑定用途、CA、client 和随机 32-byte token；记录中没有原始 token。该规则只适用于高熵随机 token。

备份 Manifest/XML 排除旧版 backup、cloud backup 和 device transfer；noBackupFilesDir 是主要边界。OEM 实际迁移/备份、真实开机未解锁、磁盘满/断电、Root/同 UID 恶意代码或有效旧密文回滚均不在通过声明内。`keyProtection` 如实返回 KeyInfo；模拟器结果不能证明硬件保护。

## 构建与测试

根目录执行 `./scripts/Verify-AndroidCredentials.ps1`，也可显式提供 GradlePath/JdkHome/AndroidSdk。脚本只构建和 Lint，不安装 APK、启动模拟器或连接手机。固定 AGP 9.1.1 / Gradle 9.3.1 / JDK 21 / build-tools 36.0.0 / 内置 Kotlin 2.2.10；不要自行改成配对核心的 Kotlin 2.4。最终 App 集成需单独验证版本兼容。

依赖严格锁定，SHA-256 verification metadata 在普通构建中强制检查；首次记录散列不等同独立签名认证。Lint 将警告视为错误，仅关闭两类已知版本升级提示 `AndroidGradlePluginVersion` / `GradleDependency`，API/安全检查保留。测试 AndroidX JUnit 1.1.5 / runner 1.5.2 仅进入独立测试 APK；产品不依赖它们。来源与许可见 [NOTICE](../../NOTICE.md)。

[原生复验入口](../../tests/integration/android_credentials/README.md)、[P1-006 证据](../../docs/audit/P1-006-VALIDATION.md)。API 26 的 O_CLOEXEC 使用 [AOSP bionic](https://android.googlesource.com/platform/bionic/+/refs/tags/android-8.0.0_r1/libc/kernel/uapi/asm-generic/fcntl.h) 中的数值，通过公开 Os.open 调用；API 27+ 使用公开常量。Android [FileOutputStream(fd)](https://android.googlesource.com/platform/libcore/+/refs/tags/android-8.0.0_r1/ojluni/src/main/java/java/io/FileOutputStream.java) 不拥有传入描述符，代码显式关闭并有原生泄漏回归。
