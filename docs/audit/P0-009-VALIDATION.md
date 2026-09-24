# P0-009 TLS 身份与真实 rclone 验证

日期：2026-09-19。**P0-009 限定任务通过：Android Keystore 身份和真实 rclone TLS 校验已在 API 26/36 模拟器及备用真机验证，真机 LAN 连接成功。D-04 的传输身份机制确定为 ADR-014；Phase 0、正式配对、Windows 客户端及 MVP 尚未完成。**

## 改动与构建

固定上游 `a378fec40561a4d18be2334f4de92ee02a0e7d0c`，独立副本 `.audit/p0-009-worktree`。相对 P0-007 修改 10 个文件；[累计补丁](../../android/patches/P0-009-tls-identity.patch)含旧修复，直接用于干净固定基线，不叠加旧补丁。完整 SHA、文件清单及构建产物字节数见 [manifest](p0-009/manifest.json)。未改构建依赖、存储操作、范围解析或传输方法实现。

- Android Keystore 保存独立的身份 CA 密钥与 TLS 密钥，两者不能通过 encoded 导出；公有 CA 原子写入 noBackupFilesDir。
- 身份 CA 固定，动态叶证书含当前接口 IPv4/IPv6 SAN，仅用于服务器认证；新握手检查地址变化与过期，合法叶证书轮换不改变身份。身份 CA 不自动换证。
- 旧 PKCS12、部分材料丢失、损坏或过期会停止共享。移除原先 TLS 失败后退回 HTTP 的分支，错误提示来自中英文资源。
- status 用新字段区分身份指纹与叶证书，旧指纹字段为 null；状态响应和发现信息不作为首次信任来源。具体约定见 [PROTOCOL](../PROTOCOL.md#7-p0-009-实验身份语义)。
- 实验仅通过已授权 USB 取得公有 CA；rclone 每个实际连接使用专属 ca-cert 信任池，并校验 IP/主机名。不安装系统根证书，不修改 hosts，不关闭证书验证。

相同隔离 JDK 21、Gradle 9.3.1、Android SDK 与 GRADLE_USER_HOME，设置 ANDROID_USER_HOME 后运行 `assembleDebug assembleDebugAndroidTest testDebugUnitTest lintDebug`：退出 0；**25 个单元测试通过，Lint 0 errors / 117 warnings**。最终 APK：`a4383ddac27dccc3f2cc46704f1c389c8ad961d1d8e8205894d8cc8aebb2b88a`。测试 APK：`e6c62d4fd05367feecb61f8640cf5263bc14c8c4d3bef648b1a3c34178587a5d`。补丁正向/反向 apply-check、diff-check 均退出 0。

## 已运行的验证

| 环境 | 原生 Keystore / 真 TLS sockets | 实际服务 / rclone 1.75.1 |
| --- | --- | --- |
| 专用 API 26 AVD | 8/8 通过；最终 APK 已复测 | 正确身份成功；错误身份/地址拒绝；进程重启保持身份；损坏材料停止且没有 HTTP，恢复同一公有证书后成功。状态连接 TLS 1.2 |
| 专用 API 36 AVD | 8/8 通过 | 同上；最终构建完整入口退出 0。状态连接 TLS 1.3；旧 PKCS12 拒绝路径也通过 |
| 已授权 Redmi K40 / Android 16 | 8/8 通过，最终 APK 已安装 | 最终完整入口退出 0，18 个通过记录（含 5 次前台启动）；正确身份通过 loopback 和真实 LAN 两个端点，错误身份/地址拒绝、进程重启、旧身份/损坏材料停止和恢复通过。状态连接 TLS 1.3 |

原生测试覆盖非导出私钥、重载身份一致、缺失公有证书/服务器密钥、损坏/过期材料、旧身份拒绝，以及正确身份握手、错误信任锚拒绝、可变地址集合的 SAN 更新及旧地址拒绝。测试使用 UUID 隔离 key alias 和私有缓存，不改默认身份或共享文件。地址集合变更测试是真 TLS socket，但由测试注入地址集合，**不是实际 DHCP 换 IP**。

rclone 负向请求返回 x509 错误；前后可信 status 请求计数只增加 1（该次 status 本身），确认失败没有到达 HTTP 认证/请求处理。正确、错误和重启连接都是独立 rclone 进程，未以前置探针替代实际校验。API 26 旧身份拒绝/显式实验迁移证据来自先前局部运行；最终运行保留同一新身份，不能把该局部运行整体称作通过。

证据：[API 26 原生](p0-009/api26-native.json)、[API 26 运行](p0-009/api26-runtime.json)、[API 26 迁移局部记录](p0-009/api26-migration-partial.json)、[API 36 原生](p0-009/api36-native.json)、[API 36 运行](p0-009/api36-runtime.json)、[真机原生](p0-009/phone36-native.json)、[真机运行](p0-009/phone36-runtime.json)、[最终状态](p0-009/phone36-final-state.json)、[先前未完成记录](p0-009/phone36-runtime-pending.json)。原始输出在 `.audit/runs/P0-009`；JSON 内单步 PASS 不替代入口整体退出状态。

真机使用同一身份 CA 访问 `127.0.0.1:18279`（USB 转发）与 `192.168.100.197:8273`（真实 LAN）；后者是手机无线到 PC 有线同网段，不是两端同时 Wi-Fi。错误信任锚和错误主机名经前一端点到达同一真实服务，在 TLS 阶段拒绝。身份 SHA-256 为 `65b52a178912e36cc73dc820f4029e85efa75a3427c2a341902326a8d2480903`。旧实验 PKCS12 的散列和精确删除范围写入运行记录；没有删除正式配对，也没有静默迁移产品身份。

## 失败、定位和修正

1. 首轮 Lint：品牌 app_name 缺英文翻译标记；改为不可翻译品牌名，重跑后 0 errors。
2. API 26 正向握手失败，日志明确 `Incompatible digest` / `NONEwithRSA`。根据官方 KeyGenParameterSpec 文档，为 **TLS 密钥**允许 DIGEST_NONE，由 TLS 栈计算握手摘要；CA 仍只允许 SHA-256。修正后 8 项原生及 rclone 正向通过。
3. API 36 正向握手失败，日志明确 `Incompatible padding mode` / `RSA/ECB/NoPadding`。Conscrypt 的 TLS RSA-PSS 路径需要自行填充的私钥运算；仅为 TLS 密钥补齐权限。修正后 API 26/36/真机原生均通过，API 36 状态连接 TLS 1.3。
4. 验证工具问题：一次遗漏 ANDROID_USER_HOME 造成测试包签名不匹配，使用原签名重建，未清应用数据；API 26 的 shell builtin 和 ADB 空行解析已修正；ActivityScenario 等待 UI idle 与共享动画冲突，改为有界服务就绪检查，并在关闭界面前停止服务；旧 ADB binary stdin 写入损坏公有证书，改用 ASCII Base64 传输并逐字节回读，原公有证书已恢复、同一身份连接复测通过。这些失败记录保留，不混作产品通过证据。
5. 累计补丁首次生成时错误关闭 autocrlf 导致整文件换行差异；恢复正确规范化后重新生成，最终补丁双向检查通过。
6. 真机先后出现息屏启动超时与 USB 掉线。Windows PnP 可见不等于 ADB 在线；一次 ADB 重启/重连未恢复，用户重新插拔后恢复在线，掉线根因未确定。[此前重连状态](p0-009/phone36-reconnect-pending.json)保留为历史证据。
7. USB 在线、亮屏且未锁定后，前台入口仍超时。只读日志显示 `MIUILOG- Permission Denied Activity`，ActivityTaskManager 对当前测试进程的 MainActivity 启动返回 102；JDWP 调用栈停在 `Instrumentation.startActivitySync`，主线程处于消息轮询，不能归因于 TLS 或继续归因于息屏。官方 ADB `am start` 显式打开公开主界面后，原有测试宿主返回 `OK (1 test)`。脚本仅在当前进程该拒绝事件出现时启动一次前台入口，五次宿主均通过；未改权限、app-op、锁屏或系统后台策略，未改 APK。原生后台恢复/开机自启兼容性不因此获得通过结论。详见[启动诊断](p0-009/phone36-launch-diagnosis.json)。

## 代码审核与边界

核对了身份初始化的完整性检查、签名密钥匹配、CA 有效期、叶证书约束、服务启动顺序、错误停止与清理、专用信任池、认证前拒绝、测试参数和秘密输出路径。正确地址链校验与错误身份拒绝已在运行时验证；没有导出私钥或记录认证 Header/密码，没有通过这些测试写入共享存储。

仍保留上游 Basic 密码的明文 SharedPreferences；正式一次性配对、Windows 受保护信任材料/凭据、身份重置交互和安全模式均未完成。旧实验 PKCS12 迁移仅由显式测试动作执行，产品不能自动删除旧身份或更换信任。允许 TLS 层所需原始 RSA 操作不等于允许任意外部签名接口；密钥只在应用内使用。不能据此称整体产品已安全或可发布。

未验证：真实 DHCP/Wi-Fi 切换、完整手机重启、长期证书续期、锁屏后持续传输、三星、Explorer 挂载和本轮双向 100 MB。进程重启结果不能当作手机重启结果。

两个 AVD 已退出，缓存保留；手机更新为实验 APK、已停止共享，Music 选择和关闭自启未改。旧实验 PKCS12 已显式移除；当前公有 CA 散列与运行时一致，测试控制文件已清理。Music 仅保留原有两个缩略图元数据文件，本任务没有共享存储写入。最终回读 ADB 在线、无测试盘符/转发/rclone 进程。

**唯一下一任务：[P0-010](../tasks/completed/P0-010-real-device-chain.md)，使用本次已验证 TLS 机制复验真机完整挂载与双向 100 MB 复制链路。**

## 官方依据

- [Android Keystore](https://developer.android.com/privacy-and-security/keystore)：非导出密钥与系统签名能力。
- [KeyGenParameterSpec.Builder](https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec.Builder)：setDigests / setEncryptionPaddings 的 TLS 说明。
- [Conscrypt CryptoUpcalls](https://github.com/google/conscrypt/blob/master/common/src/main/java/org/conscrypt/CryptoUpcalls.java)：平台 TLS 对 RSA 私钥操作的调用路径；日志用于确认本机实际错误。
- [rclone v1.75.1 fshttp](https://github.com/rclone/rclone/blob/v1.75.1/fs/fshttp/http.go)：ca-cert 创建专属 RootCAs 池；[TLS 参数说明](https://rclone.org/docs/#ssl-tls-options)。
- [Go VerifyHostname](https://pkg.go.dev/crypto/x509#Certificate.VerifyHostname)：按 IP SAN / DNS SAN 验证端点。实际版本行为由真实 rclone 负向结果佐证。
- [AndroidX ActivityScenario](https://github.com/android/android-test/blob/main/core/java/androidx/test/core/app/ActivityScenario.java)：状态转换/关闭会等待 UI idle；据此修复验证宿主的退出顺序。
- [Android 真机连接排查](https://developer.android.com/studio/run/device#troubleshoot-device-connection)：确认 USB 调试、设备列表和重启 ADB 服务；不能凭 USB 可见就认定调试链路可用。

- [ADB Activity Manager](https://developer.android.com/tools/adb#am)：通过公开 Activity 启动入口进行已授权前台测试；不将前台测试当作后台启动兼容性证据。
