# P1-006 Android 受保护配对记录验收

结论：限定存储任务通过。Android Keystore 保护的每电脑授权记录、原子提交和拒绝恢复行为已实现并经 API 26/36 模拟器验证。**正式配对窗口、HTTPS 授权、手机批准界面和可日常使用的 NG 客户端仍未完成。**

## 实现与代码复核

[credentials-store](../../android/credentials-store/README.md)为独立 Android library。系统 Keystore 创建不可导出的 AES-256-GCM key；提供者生成 nonce，AAD 绑定用途/版本和 CA。密文内只保存随机 token 的绑定验证值与元数据，不保存原始 token。默认 SAFE，16 Active / 128 总记录上限，撤销历史不自动驱逐或重新激活。

在 credential-protected noBackupFilesDir 中创建 0700/0600 私有目录和文件，检查 UID、类型、链接数及 O_NOFOLLOW/CLOEXEC；同进程 gate 与原生文件锁串行事务。仅将密文写入随机独占临时文件，sync → rename → 目录 fsync → 解密回读。写失败或损坏禁用本进程所有对应实例；新进程只依实际持久状态恢复。首次初始化与既有加载分离，key/file 任一已存在就拒绝重建；是否本次新建 CA 必须由宿主证明。

批准/模式更新/撤销比较修订号；授权每次重读并比较固定长度验证值。短小提交回调与撤销共享锁，未来宿主还须执行模式、取消活动流和停止共享。没有 HTTP 监听、INTERNET 权限、产品日志或 UI；测试用例、合成 token 和进程 runner 不进入 AAR。内部 checkpoint 钩子保留在库内，公开工厂固定传入 null；Kotlin internal 不作为同 UID 恶意代码的安全边界。备份 Manifest/XML 和元数据严校验已复核。

这是实现者逐文件代码复核，不是独立安全审计。检查了四个产品源文件的输入、状态、异常、Keystore、原生资源所有权与并发边界；复核确认并修复描述符泄漏。最终文件、依赖锁与产物散列见 [manifest](p1-006/manifest.json)，复核范围见 [review-results](p1-006/review-results.json)。

## 实际验证

本机固定 AGP 9.1.1、Gradle 9.3.1、JDK 21、build-tools 36.0.0、AGP 内置 Kotlin 2.2.10。未修改 P1-004 的 Kotlin 2.4 或 P0 APK。普通构建开启 strict dependency locking 与 SHA-256 verification；新验证脚本只构建/Lint，不操作设备。[官方构建组合](https://developer.android.com/build/releases/agp-9-1-0-release-notes)

| 检查 | 实际结果 | 边界 |
| --- | --- | --- |
| Debug / Release AAR、独立测试 APK | 构建退出 0 | `scripts/Verify-AndroidCredentials.ps1`，严格锁/散列 |
| Debug / Release Lint | 均 No issues found | warningsAsErrors；仅禁用两类固定版本升级提示，保留 API/安全检查 |
| API 26 原生测试 | 26/26，通过 | 真实 Android Keystore / 私有文件 |
| API 36 原生测试 | 26/26，通过 | 同一测试 APK；安装后哈希相符 |
| API 26 / 36 进程恢复 | 各 3/3，通过 | 正常撤销重开、rename 前杀进程、rename 后杀进程 |
| 句柄回归 | 两平台通过 | 20 次重开后持锁恰好 2 个 CLOEXEC 句柄，释放后 0 个 |
| 硬链接 | 两平台拒绝创建 | `platform_denied_creation`，未覆盖库内多链接分支 |
| KeyInfo | API 26：securityLevel 不可用；API 36：0；两者 insideSecureHardware=false | 不宣称硬件/StrongBox 保护 |

26 项原生用例包含固定验证值答案、CA/client/token 绑定、批准/撤销/模式与修订竞争、容量、24 个 nonce、损坏/缺失/替换 key、错误 CA、非法名称/编码/枚举、密文篡改/截断/超长、宽松权限/符号链接、同进程独立文件锁竞争、初始化/撤销各提交点故障、并发撤销屏障。多个用例包含多种输入，不把子断言重复计为测试总数。

进程夹具使用独立测试包，结束进程后新 PID 回读：成功撤销后为 Revoked，sync 后 rename 前中断为 Active，rename 后中断为 Revoked。**提交前中断的撤销没有持久成功。** 全部为合成材料、单独命名空间；无备用机 APK 安装、共享服务或本轮真机访问。两个 AVD 已停止，隔离测试密文保留。[原生结果](p1-006/native-results.json)、[构建与历史失败](p1-006/build-results.json)、[最终检查](p1-006/final-check.json)

## 保留的失败与定位

首轮编译因不存在的公开 O_DIRECTORY / Os.unlink API 失败；按实际 SDK 与官方 Os 文档改用类型校验和 Os.remove。之后 Lint 指出 O_CLOEXEC 字段需 API 27，尝试 fcntlInt 又被确认需 API 30；停止该路径，查 [Android Os](https://developer.android.com/reference/android/system/Os) 与 [AOSP API 26 bionic](https://android.googlesource.com/platform/bionic/+/refs/tags/android-8.0.0_r1/libc/kernel/uapi/asm-generic/fcntl.h)，以公开 Os.open 接收原子 CLOEXEC 数值常量。编译失败与两份 Lint 原始结果保留。

API 26 第一轮 23/25，通过数不改写。失败一为硬链接创建 EACCES，真实 SELinux audit 显示 app_data_file 的 link 被拒绝；拆出平台拒绝与库拒绝两个明确分支，未更改系统策略。失败二为预期 2 个句柄、实际 3 个；[AOSP FileOutputStream](https://android.googlesource.com/platform/libcore/+/refs/tags/android-8.0.0_r1/ojluni/src/main/java/java/io/FileOutputStream.java)确认公开 fd 构造器 isOwner=false。修复显式 fd 关闭，并增强 20 次重开/关闭后为零的断言。修复后 API 26 和 API 36 各 26/26 通过。

## 未覆盖

未运行真实手机/OEM 备份迁移、整机重启前首次解锁、真实磁盘满/断电、硬件 Keystore/StrongBox 或跨进程同时抢锁。进程中断和注入异常不能代替上述测试。不能防同 UID 恶意代码/Root 或有效旧密文回放；JVM 内部副本不可保证全部擦除。崩溃遗留密文临时文件不加载，自动清理尚未实现。

HTTP grant 生命周期、手机明确批准、真实 WebDAV 每请求授权/撤销流取消、Windows 正式挂载和自动重连尚未集成；22 项产品配对矩阵保持原状态，本任务不宣称 MVP 完成。历史 Windows/协议核心源码与产物按散列保全，未重跑未修改模块来增加测试数。

下一任务：[P1-007 Android 首次配对与授权服务](../tasks/completed/P1-007-android-pairing-service.md)。
