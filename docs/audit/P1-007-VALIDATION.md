# P1-007 Android 首次配对与授权服务验收

结论：限定 Android 授权闭环通过。API 36 首轮 41/42 的活动下载撤销失败已修正，最终 API 26/36 各 42/42 和 C# 联动通过。此报告只覆盖 Android NG 应用与 Windows 测试宿主的授权闭环，**不表示可正式使用、完整安全模式或 MVP 完成**。

## 实现与复核

`android/app`（`org.phonebridge.ng`）接入已有 PAKE 核心和 Keystore 存储源文件。新应用与实验包分离，Keystore CA 初始化与配对库创建绑定，旧材料缺失不自动迁移/替换。复用固定 P0-009 的八个 TLS/文件辅助文件，记录逐文件来源；核心模块及 P0 产物保留。正式 App 统一 AGP 9.1.1 内置 Kotlin 2.2.10 与 BC 1.86，不读取原独立 Kotlin 2.4 metadata。

MainActivity 使用资源化中英文文案和 FLAG_SECURE，非导出前台 Service 承载共享；手机按钮开启 120 秒窗口并批准确定的请求，离开前台立即关闭窗口。前台 generation 防止排队开启晚于后台取消；倒计时只更新文本，不重建批准按钮。客户端快照只接受更新的 revision。

限时 TCP 使用真实 8 帧、全局 5 次预算、单密码学工作者、30 秒握手和 5 秒帧期限。最后一帧前检查 Windows 输出 EOF，双方最后完整 EOF 后才转 HTTPS。临时授权只保留 grant 哈希至原期限，Pending token 不落盘；严格有界 JSON，批准持久完成后才能 Active。同样提交幂等，改变提交冲突；取消/批准共用锁。

NanoHTTPD 原版会覆盖重复请求头，故保留官方 BSD 源码并修正入口：严格 CRLF/request-line/UTF-8、重复头、512 字节认证头、8 KiB 头、64 字段、有界线程和请求期限；固定错误不回显 peer 输入。拒绝查询/fragment、压缩与分块请求，不使用 parseBody 临时文件。文件使用现有安全路径和 XML/Range；只放行 OPTIONS/PROPFIND/GET/HEAD，GET 目录无 HTML 列表，`/phonebridge` 保留空间不落入文件路由。授权记录默认仍为 SAFE，当前所有写方法拒绝。

Basic 每请求从 Keystore 记录重新验证。撤销先禁止该电脑新请求，再持久保存并关闭其活动 socket，保留 self 请求以发 204；写失败停止授权并触发宿主停止共享，可能直接关闭当前连接，绝不把缺失回执当作成功。已下载/缓存内容不受撤销抹除。检查了 response 的 finally 释放、原有文件描述符所有权及关停路径。

这是实现者逐文件复核，不是独立安全审计。产物散列见 [manifest](p1-007/manifest.json)，依赖、实现复核及补丁分别见 [dependencies](p1-007/dependencies.json)、[review](p1-007/review-results.json)、[NanoHTTPD patch](p1-007/nanohttpd.patch)；测试控制只编入 androidTest，C# 宿主为独立测试项目，不是 Windows 正式配对界面。

## 实际验证

| 检查 | 结果 | 边界 |
| --- | --- | --- |
| Debug / 未签名 Release / test APK | 严格锁与散列校验构建通过 | Verify-AndroidApp；未签名 Release 不分发 |
| Debug / Release Lint | No issues found | 两类版本提示和下述精确依赖例外 |
| 同源 Kotlin 协议单元 | 15/15 | App 内 Kotlin 2.2.10 + BC 1.86 |
| API 26 原生 | 42/42 | 16 项新服务 + 26 项受保护存储 |
| API 36 原生 | 42/42 | 同一最终产品/测试 APK；首轮失败保留 |
| API 26 C# ↔ Android UI/Service | 通过 | 真实 DPAPI、TLS、Activity 按钮；三个不同 PID |
| API 36 C# ↔ Android UI/Service | 通过 | 三个不同 PID；不等于真机 Wi-Fi |

16 项服务用例覆盖批准前拒绝、提交幂等/变更、写方法拒绝、Range/PROPFIND、严格 JSON/重复认证头、错误码和尾帧、五次预算、拒绝/取消/关闭窗口、自身撤销、64 MiB 活动下载被撤销终止、存储损坏、批准/取消竞争、真实等待 120 秒到期且轮询不延长、错误 CA/SAN、两个电脑隔离、批准/撤销写失败、慢 HTTP 头和不完整 PAKE 的实际截止。子断言不重复计为用例。原生失败存储夹具确认 onFatal 信号及请求阻断，不冒充真实磁盘满或断电。

C# 宿主在内存接收短码，用已有核心完成配对，经 PAKE 绑定的 CA 配置严格链/SAN 检查；真正 CurrentUser DPAPI 保存 Pending 成功后才 POST。批准前 session 为 401；点击手机批准按钮后 session 为 200/Active，合成文本读取正确。离开界面后 grant 为 401，长期凭据仍为 200。停止应用并在新 PID 中开始共享，旧 token 仍为 200；自身撤销 204 后 session 为 401，再换新 PID 仍为 401。停止按钮后 HTTPS/配对/测试控制监听均关闭。两平台还在真实 Service 中注入配对密文损坏，C# 收到 503 或连接断开，engine 被关闭且 HTTPS 停止监听。测试 finally 只恢复自己的合成密文以便复跑，产品没有自动修复/授权回退。

测试传输为模拟器 TCP redirection（PAKE）和 ADB loopback（HTTPS/测试控制），不是 Wi-Fi/真实 LAN。最终复验必须使用最新产品/测试 APK，安装后回读哈希；首轮和中间修正的 APK 哈希分别保留。不会安装或操作备用真机，也不启动 rclone/WinFsp。

## 保留的失败和依据

1. BC 1.86 的三个 JAR 含重复 LICENSE.md，首轮打包失败；核对内容相同后合并许可资源，没有删除通知。
2. 首轮 Lint 报默认 Locale、英文数量文案、缺少图标和 bcpkix 的 TrustAllX509TrustManager。前三项修正；后一项定位到未调用的 EST 工具方法，只对固定 `bcpkix-jdk18on-1.86.jar` 作规则路径例外。项目安全检查保留，TLS 错误 CA/SAN 原生拒绝通过。[官方 BC 源码](https://github.com/bcgit/bc-java/blob/r1rv86/pkix/src/main/java/org/bouncycastle/est/jcajce/JcaJceUtils.java)
3. 失败构建未持久新锁，下一次 strict build 因缺锁拒绝；完成初次锁生成后，最终脚本正常严格构建。Java 8 source/target 在 JDK 21 的弃用提示及 SDK XML 工具版本提示保留，不称整个工具链零警告。
4. C# 首轮最后一帧 EndOfStreamException：ADB shutdown 对应关闭流，无法保留此协议的 TCP 半关闭。改模拟器 redirection；API 26 Wi-Fi 网段与传统 redirection 目标不同，读取实际地址后仅重启专用 AVD 使用 `-feature -Wifi` 的模拟以太网。生产协议/TLS 不变。[ADB 官方流说明](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/docs/dev/asocket.md)、[模拟器重定向](https://developer.android.com/studio/run/emulator-networking-interconnect)
5. 重启联动首次在第二次停止时等待超时：测试 runner 对仍存活 Activity 调用 startActivitySync，等待新实例恢复；复用存活 Activity 后通过。保留失败结果，没有改动产品取消/停止逻辑。[Instrumentation API](https://developer.android.com/reference/android/app/Instrumentation#startActivitySync(android.content.Intent))
6. API 36 的活动下载撤销连续两次未能在 5 秒请求期限内得到 204：ConscryptEngineSocket.close 会先发送 TLS 关闭通知，阻塞写使普通关闭不能保证时限。增加 API 29+ 的公开 ParcelFileDescriptor 复制句柄与 Os.shutdown(SHUT_RDWR)，先中断底层传输再释放 TLS；API 26 使用已验证原生 close，避免 API 29 前 fromSocket 不复制描述符的陷阱。同一单项从重复失败变为通过（1.14 秒），之后两平台完整 42 项回归通过。[Conscrypt 实现](https://android.googlesource.com/platform/prebuilts/fullsdk/sources/+/refs/heads/androidx-constraintlayout-release/android-35/com/android/org/conscrypt/ConscryptEngineSocket.java)、[公开描述符 API](https://developer.android.com/reference/android/os/ParcelFileDescriptor#fromSocket(java.net.Socket))
7. 完整回归中撤销流已通过，存储失败用例却在收到连接断开后立刻读取 fatal 标志。代码次序是先中断传输、再触发回调；测试增加最多一秒的有界等待，仍核对停止回调和后续 503，不修改产品状态或放宽成功回执。该轮 41/42 保留。
8. C# build 不接受 restore 的 `--locked-mode` 开关；改用 `-p:RestoreLockedMode=true` 后成功，0 warnings/0 errors。C# HTTP 正文也受绝对期限约束，不能只给接收响应头设超时。

## 未覆盖与下一步

未验证真实 Wi-Fi、mDNS 发布/撤销传播、换 IP、OEM 后台/锁屏/Doze、整机重启首次解锁、备份迁移、断电/真实磁盘满；API 27/28 没有实测；前台 wake lock 六小时边界和长期运行待后续处理。没有网络变化关闭配对窗口的产品回调；没有正式诊断包或完整日志/进程秘密矩阵。测试日志只核对瞬时 PIN 不泄露，不能等同全部产品秘密扫描。

Windows 测试宿主撤销后本地仍为 RevocationPending 且禁用挂载；完整重试/忘记设备由产品任务实现。没有用新 token 挂载 rclone，没有正式 Windows 配对界面；安全模式写入、删除/覆盖确认、缓存和自动重连都尚未完成。22 项配对矩阵仅记录局部证据，不标完整通过。

下一任务：[P1-008 Windows 首次配对与只读连接界面](../tasks/completed/P1-008-windows-pairing-client.md)已完成。

原始结果、保留失败和最终检查见 [native-results](p1-007/native-results.json)、[build-results](p1-007/build-results.json)、[final-check](p1-007/final-check.json)。两个本次 AVD 已停止，本次转发已清除。历史模块与 P0 产物按散列核对；Windows 存储只增加测试宿主 friend assembly，不改存储逻辑。
