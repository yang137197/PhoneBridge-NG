# 技术决策记录

更新日期：2026-09-26。状态解释：**已确定**来自用户交接或已记录依据的工程决定；**待定**表示证据不足。决策确定不等于功能全部实现或兼容性验收通过。

## 已确定决策

| ADR | 决策 | 依据/原因 | 后果与边界 |
| --- | --- | --- | --- |
| ADR-001 | 保留 HTTPS/WebDAV | AGENTS 第 2 节，保留可验证的上游路线 | 不换 SMB/FTP/SFTP/MTP；服务端的具体库可先沿用参考，缺陷必须验证，不能宣称协议兼容已完成。 |
| ADR-002 | 使用 rclone VFS | AGENTS 第 5 节 | 不自行实现 Windows 缓存/文件系统；版本、参数和 TLS 绑定方案待测试，不照搬不安全参数。 |
| ADR-003 | 使用 WinFsp | AGENTS 第 5、20 节 | 不自建驱动；P1-024 按 ADR-027 固定官方 MSI、校验和安装边界，无 WinFsp 的干净系统分支仍须实机验证。 |
| ADR-004 | Windows 使用 C# + 稳定 LTS .NET + WPF | AGENTS 第 4 节；P0-001 核对 .NET 10 为 2026-09-19 的 LTS | Python 只作功能参考；具体 SDK/补丁版本在工程任务核对后锁定，不在仅文档阶段创建 global.json。 |
| ADR-005 | Android Kotlin 原生并遵守系统权限 | AGENTS 第 3、16 节 | 不 Root、不用厂商私有 hack；最低版本、targetSdk 和后台实现由 Phase 0 调研决定。 |
| ADR-006 | mDNS 自动发现，手动 IP 仅作备用 | AGENTS 第 14 节 | 发现信息不作为可信身份；地址变化必须仍可识别同一已配对设备。 |
| ADR-007 | 中文优先，资源文件管理 zh-CN/en-US | AGENTS 第 10 节 | 从首版提取 UI/通知/错误文案；不先硬编码再国际化。 |
| ADR-008 | 系统安全存储与完整 TLS 身份校验 | AGENTS 第 7、8 节；审计 S3–S5 | Windows 使用 Credential Manager/DPAPI；Android Keystore 保护密钥。生产传输不能跳过证书检查或退回 HTTP。 |
| ADR-009 | 默认安全模式，删除需明确确认 | AGENTS 第 9 节；审计 S1、S2、S6 | 服务端执行权限边界；确认协议未定，不能仅依赖 Explorer 弹窗；回收站自动清理不在首版。 |
| ADR-010 | 每次只做一个可验证任务，Phase 0 先于 Phase 1 | AGENTS 第 26、32–35 节 | 单元测试/文档完成不能代替原始链路挂载、读、写、复制验收。 |
| ADR-011 | 无账号、遥测及综合互联功能 | AGENTS 第 1、22、31 节 | 不移入 Tailscale 远程访问；自动更新、专用缩略图优化留在 MVP 后。 |
| ADR-012 | 保留上游授权及来源 | AGENTS 第 23 节；P0-001 LICENSE 核对 | 上游为 GPL-3.0-or-later，导入时保留通知并补齐完整文本；分发对应源码/依赖义务不得省略。 |
| ADR-013 | Android 安装下限 API 26，本阶段 compile/target 36；共享使用 connectedDevice 前台服务 | P0-007 官方规则、API 26/36 实测和基线缺陷复现，解决 D-02 | 系统重建恢复共享，状态查询/用户停止不遗留 sticky 服务；自启需用户开启并满足系统权限。Doze、厂商限制和 Android 17 不在通过声明内，正式发布前另核对。 |
| ADR-014 | 稳定设备 CA 与 TLS 私钥存 Android Keystore；动态 IP SAN 服务器证书，rclone 每次使用设备专属 ca-cert 并校验端点 | P0-009 在 API 26/36 与真机的正向/错误身份/错误地址/重启/损坏恢复通过；P1-008 后续完成正式配对与 Windows 信任接入 | 身份 CA 变更必须重新配对，不信任发现消息，不退回 HTTP；P1-028 已验证一次真实 DHCP 地址变化。长期证书续期仍未单独验收。 |

许可证和 .NET 查询日期见 [UPSTREAM_AUDIT.md](docs/UPSTREAM_AUDIT.md)。ADR-013 的限定结果、失败入口及独立清理回读见 [P0-007](docs/audit/P0-007-VALIDATION.md)，不等于 Phase 0 全部通过。

## ADR-015 Windows 使用系统 DNS-SD 发现

实施前决定（2026-09-19）：选择 Windows.Devices.Enumeration.DeviceWatcher（DNS-SD AEP Service），不引入第三方 mDNS 解析库。Microsoft 官方列出 DNS-SD 协议 ID，并说明现代 .NET 桌面项目通过 Windows TFM 调用 WinRT；它提供服务增改删和停止事件，适合现有 Windows 专用架构。[网络枚举](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/enumerate-devices-over-a-network)、[桌面 WinRT](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)。

核查的候选：Zeroconf 3.7.16（MIT，2024-12-17 发布，tag bd4bd55e044a9aa4b8656741214c989a5ee50ded）Windows 发送仅 IPv4，现有 Issues 报告接口异常；源码 RecordReader.ReadDomainName 递归处理压缩指针，没有循环/深度上限，故当前不采用其网络解析器。此为本地源码审查结论，未对外作漏洞披露。Makaretu.Dns.Multicast 0.27.0 的旧版本路线也没有足够依据优于系统 API。[Zeroconf 包](https://www.nuget.org/packages/Zeroconf/3.7.16)、[源码](https://github.com/novotnyllc/Zeroconf/blob/v3.7.16/Zeroconf/Dns/RecordReader.cs)、[Issues](https://github.com/novotnyllc/Zeroconf/issues)、[Makaretu 包](https://www.nuget.org/packages/Makaretu.Dns.Multicast/0.27.0)。

不采用被官方标为不支持的 DnssdServiceWatcher；使用 Windows.Devices.Enumeration。依赖仅为 .NET/Windows SDK 投影和测试框架，版本及传递依赖锁定。系统缓存可能使离线通知延后，需实测并明确限制；发现从不代表身份认证或连接存活。

## 未决问题

| ID | 需要作出的决定 | 收集什么证据 | 对应任务时点 |
| --- | --- | --- | --- |
| （无） | 当前没有待本阶段实现前决定的问题 | 新事实出现时再新增，不预先扩展范围 | 按单任务处理 |

阶段记录（Phase 0）：D-01 的最小真机链路已由 [P0-010](docs/audit/P0-010-VALIDATION.md)完成，范围为备用机无线到 PC 有线同 LAN；当时未验证 ROM 来源、Samsung 与两端同时无线。[阶段门槛](docs/audit/PHASE-0-ACCEPTANCE.md)允许开始 P1-001；后续 Samsung 产品链路证据见 P1-011 起的验收记录。

D-04 已由 ADR-014 确定传输身份机制，P1-008 将其接入正式配对、Windows控制请求和 rclone；P1-010/015/028 分别验证活动恢复、手动地址和真实地址变化仍执行身份检查。

D-03 已由 ADR-017 和 [配对契约](docs/PAIRING.md)确定，并由 P1-005/006 的两端存储、P1-007 Android授权服务及 P1-008 Windows产品配对/挂载完成限定实现与验收。该结论不等于独立密码学审计或公开发行安全认证。

未决项按当前任务需要逐项解决，不一次实现全部。若新事实推翻已确定决策，先在任务中说明证据、影响和替代方案，再修订相应 ADR。


ADR-015 实施修正：已用原生 API 与独立 mDNS 监听复现持续 watcher 不发 Removed；选择每 12 秒重新枚举、连续两轮缺失撤销候选。没有变更协议或库，重枚举复用同一校验路径；有效广播去重，旧 watcher 停止后才创建下一个。原始失败证据与修复后的真机结果分别记录。

## ADR-016 只读会话的进程所有权与停止

P1-002 实施前选择：固定 rclone 1.75.1，实际 ca-cert 校验；独立 CA 会话文件持有防修改句柄；凭据只留内存与子进程环境。固定只读挂载、VFS cache off；RC 仅 loopback、随机认证并核对 core/pid，正常 core/quit 后核对进程及盘符。命名租约防止本应用重复占用同一盘符，Windows Job kill-on-close 约束宿主异常退出后的子进程。只读正常停止超时才可终止所属进程，停止失败不释放所有权/伪报成功。读写缓存恢复不由本决定覆盖。

依据：[rclone Windows mount](https://rclone.org/commands/rclone_mount/)、[RC core/pid 和 core/quit](https://rclone.org/rc/#core-quit)、[Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)。官方 RC 说明它是完整控制接口，因此不使用 rc-no-auth、不对 LAN 暴露、不开放任意命令入口。

进程复核后采用创建时 Job 绑定，避免事后分配的崩溃窗口；[Microsoft STARTUPINFOEX 属性](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute) 明确 Windows 10+ 支持 JOB_LIST 与 HANDLE_LIST。最低 API TFM 不变；本轮实际兼容验证仍只有本机 Windows 11 x64。

## ADR-017 一次性 PAKE 身份引导与每电脑凭据

P1-003 确定 [PBNG Pairing v1](docs/PAIRING.md)：手机前台随机 8 位码、120 秒窗口、最多 5 次连接，Bouncy Castle J-PAKE NIST_3072/SHA-256 三轮确认。独立限时 TCP 通道只交换公有消息，完整 Hello 绑定 CA、端口、窗口及双方角色；确认后关闭监听，再通过严格 HTTPS、限时 grant 和手机明确批准保存每电脑独立的随机 256-bit token。文件传输仍是 HTTPS/WebDAV，不把发现或首次未认证 TLS 当信任，也不引入用户 ADB 流程。

Windows 选择当前用户 DPAPI，提交 token 前持久保存 Pending。Android Keystore AES-GCM 保护配对集，仅保存随机 token 的绑定验证值及元数据；不沿用实验 SharedPreferences 密码。批准/取消幂等、回执丢失以已保存 token 查询恢复，撤销成功必须持久提交；写失败停止共享但不能声称重启后已撤销。默认安全模式和写缓存仍分别由 D-05/D-06 实施。

阶段记录（P1-003）：官方两端库源码已核对，Java 1.86/C# 2.7.0 的 14 项离线互操作、11 项合成编码/HKDF 检查通过。RFC 8236 是 Informational；应用帧及授权事务是本项目契约，未做独立密码学安全审计。固定 32 字节有符号 MAC 编码与 C# 适配器前置恒定时间比较见契约。依赖来源、校验及当时未运行的 22 项产品验收见 [P1-003 记录](docs/audit/P1-003-VALIDATION.md)。该任务当时未升级 APK 或将探针加入正式 Windows solution；后续产品接入见 P1-007/008。

阶段记录（P1-004）：正式 C# 核心与独立 Kotlin/JVM 核心已实现完整 8 帧/库校验/HKDF；50 项 C#、15 项 Kotlin 和 59 项管道互操作通过。库选择与协议不变；该任务只产出待验证 CA 与短命 grant，当时尚未实现网络授权和安全存储，证据见 [P1-004](docs/audit/P1-004-VALIDATION.md)。

阶段记录（P1-005）：Windows 原生 CurrentUser DPAPI + UI_FORBIDDEN 保护整条二进制记录，CA 自签名与协议算法严格验证；当前用户/SYSTEM ACL、目录句柄固定和独占文件锁限制本地合作进程访问。密文临时文件 flush 后原子提交并回读；提交不确定则实例禁用。修订号和状态阻止迟到激活恢复挂载，Active 模式刷新须新的严格 session 验证。只对已确认文件占用错误有界重试，不重试不确定替换或整个授权事务，不提供同用户恶意回滚防护。原生 60 项存储测试与 184 项 Windows 回归通过，源码复核/占用失败及修正见 [P1-005](docs/audit/P1-005-VALIDATION.md)。Android 受保护记录与网络授权在该任务当时尚未实现，后续由 P1-006 至 P1-008 完成。

P1-006 实施补充：Android Keystore AES-256-GCM 保护整组 token 验证值/元数据，CA 绑定、容量/修订号/保留撤销历史；credential-protected noBackup 私有文件与原子提交。初始化只供宿主全新身份事务，损坏不重建；不确定提交禁用同进程全部实例，新进程依磁盘恢复。每请求重读、短提交与撤销共用锁；真正 HTTP 取消仍须宿主实现。API 26/36 各 26 原生与 3 进程恢复通过；两模拟器无硬件保护证明，未覆盖真实 OEM 备份/断电/有效密文回滚。具体构建组合、公开 API 兼容和 fd 资源修复见 [P1-006](docs/audit/P1-006-VALIDATION.md)。

P1-007/008 后续完成 Android真实 TCP/HTTPS授权、手机批准、正式 Windows v3 入口、DPAPI Pending/Active、严格 session、rclone/WinFsp挂载及撤销边界。P1-009/023 再完成安全写入和三种模式；ADR-017 的当前状态以 [配对契约](docs/PAIRING.md)为准。

## ADR-018 正式 Android 宿主与严格 HTTP 入口

P1-007 在独立 org.phonebridge.ng 应用中整合既有 PAKE/Keystore 源文件，AGP 内置 Kotlin 2.2.10 重编译，不读取 Kotlin 2.4 元数据，不改变独立模块构建。BC prov/pkix/util 统一 1.86。只导入 P0 已验证的 TLS/文件辅助代码，旧实验包和明文密码不迁移。P1-007 阶段默认 SAFE 且暂拒绝所有写方法；P1-009/023 后续实现服务端写入边界、删除确认和三种模式。

NanoHTTPD 2.3.1 原版请求头 Map 会吞掉重复认证头，故保留官方 BSD 源码并作限定入口修正，同时限制连接数/请求期限。状态机持久成功后才承认 Active/撤销；发生存储错误立即阻断活动流，网络可能关闭而没有可达 503，Windows 必须保留待确认/待撤销。Activity 本地按钮批准，测试控制只存在 androidTest APK；正式 Service 不导出。具体代码、已执行证据和未覆盖项以 P1-007 验收为准。

## ADR-019 Windows 首次连接的组合边界（P1-008 已验收）

保留已有发现、PAKE、DPAPI 和只读挂载模块，新增 Connection 组合层与 WPF 入口；不更换密码库/协议或增加 UI 框架。v3 广告的身份/窗口只是候选提示，不能建立信任；八帧确认与 EOF 后以同 IP、确认端口和 CA 建立严格 HTTPS。持久 Pending 先于 token POST，已验证 session 先于 Active 与挂载。超时保留可恢复状态，用户取消先禁用再清理；待撤销不能重新激活。

桌面实例以当前用户单实例约束串行操作，只拥有一个只读 MountManager。关闭时等待取消/卸载，清理失败保留窗口和进程归属。远端撤销未确认时保留禁用记录；“仅忘记本机”是独立且明确警告的用户选择，不能宣传为远端撤销。输入短码不经剪贴板、命令行或普通设置；rclone 沿用已固定的二进制与凭据子进程环境边界。

Redmi K40/API 36 已通过真机 WPF 配对、只读读取/卸载、已保存配对重连与关窗清理。该句只描述 P1-008 当时范围；默认安全模式、自动重连、托盘、自启和本地交付已分别由 P1-009 至 P1-024 后续任务完成限定验收。

## ADR-020 安全模式只创建写入与一次性删除确认

P1-009 解决 D-05：安全模式的 WebDAV 写入仅可创建新目标，PUT/COPY/MOVE 遇到已存在目标一律冲突，避免覆盖绕过删除保护；MOVE 到不存在目标视为重命名。普通 DELETE 始终拒绝，不能等待 Explorer 请求期间弹窗。

删除改用受认证控制 API 的两步事务。准备操作绑定客户端、规范路径和完整目标树快照，产生 30 秒、最多使用一次的随机确认 ID；桌面端显示具体目标并由用户确认后执行。执行前先消费 ID，再在服务端核对客户端、期限、目标未变化和 SAFE 权限，成功或失败均不能重放。确认仅存在内存，停止共享或重启后失效。完全读写模式可以直接删除和按 WebDAV 规则覆盖，但不在当前 UI 提供模式切换。

写请求在共享范围内完成暂存和同步，最终文件系统提交与客户端撤销共用短门闩。撤销可中止传输，并保证之后不能提交；网络传输和大文件复制不在门闩内。Windows 写挂载的缓存、恢复与安全卸载由 ADR-021 约束。

## ADR-021 每设备 VFS 写缓存与待上传保护

P1-009 的真实 Explorer 新建目录测试在服务端写接口已通过后仍使 Explorer 无响应，且请求没有到达手机；进程参数确认使用 `--vfs-cache-mode off`。rclone 官方文档明确该模式只支持有限的顺序写入，`writes` 模式才支持普通文件系统写操作并重试上传。因此 SAFE/READ_WRITE 挂载固定使用 `writes`，READ_ONLY 保持 `off`。

每个设备按已验证身份指纹使用独立且稳定的当前用户缓存目录，显式传入 `--cache-dir`；同一设备另持跨进程租约，禁止两个 rclone 进程使用重叠缓存。写回在文件关闭后立即开始，缓存上限 32 GiB、目标最小剩余空间 2 GiB、清理轮询 5 秒；开放文件可能按 rclone 明确限制临时超过配额。缓存目录只授予当前用户与 SYSTEM，不含凭据，不随正常卸载递归删除。

正常卸载前通过受认证 loopback RC 同时核对 `vfs/queue` 与 `vfs/stats`。队列、上传中、错误文件或空间不足任一未清零，就保留盘符、进程和缓存归属并报告 `pending-writes-not-confirmed`，不得进入超时强杀。只有写队列确认干净后，后续 `core/quit` 超时才可终止所属子进程。异常退出保留稳定缓存；同一设备下次以相同参数启动时由 rclone 恢复未上传内容。RC 响应只读取计数和布尔状态，不记录缓存内用户路径。

阶段记录（P1-009）：依据为 [rclone VFS 文件缓存](https://rclone.org/commands/rclone_mount/#vfs-file-caching)、[rclone vfs/queue 与 vfs/stats](https://rclone.org/rc/#vfs-queue)。该任务当时尚未验证断网、磁盘空间不足和进程异常后的真实恢复；后来 P1-016 至 P1-019 验证了断网和 Windows 重启后的恢复，磁盘满与强制终止仍未实机验证。

阶段记录（P1-009）：SAFE 真机挂载的实际 rclone 参数已确认使用稳定每设备 `writes` 缓存；Explorer 新建、重命名和小文件写回手机通过。客户端安全卸载后 `P:` 与 rclone 均消失，缓存目录保留。一次确认等待超过 30 秒后服务端拒绝并保留目标，新确认才允许删除，ADR-020 的失败关闭边界得到真机证据。该任务当时尚未运行断网、磁盘满、异常退出及重启恢复；后续证据按 P1-016 至 P1-019 更新。

阶段记录（P1-016）：1 GB Wi-Fi 中断证明“稳定每设备目录 + 临时 `:webdav:` 后端”仍会因 rclone 配置后缀变化产生不同缓存命名空间，新会话不能接管旧脏条目。写挂载因此固定使用受保护短期配置中的 `phonebridge:` 远端名；停止判定除 RC 队列/计数外，还必须检查该固定命名空间的磁盘元数据，存在 `Dirty: true` 或元数据异常时失败关闭并保留所有权。修复后原 rclone 在网络恢复后完成同一 1 GB 上传，手机端独立哈希一致，随后干净卸载。该任务关闭同进程 Wi-Fi 中断恢复；P1-018/019 后续关闭 Windows 重启后的 10 GB 脏缓存恢复状态，磁盘满和强制终止仍未实机验证。

## ADR-022 锁屏共享的电源所有权与有界自动恢复

PhoneBridge 的共享是用户明确启动、通知持续可见的外部设备网络交互，因此保留 `connectedDevice` 前台服务，并在共享生命周期持有 `PARTIAL_WAKE_LOCK`。前台服务只提高进程存活优先级，不自动防止熄屏后的 CPU suspend；旧实现的六小时 WakeLock 超时会在 UI 仍显示共享时静默改变能力，改为由服务所有并在全部退出路径释放。mDNS 继续持有 MulticastLock。Android 34+ 已将 `WIFI_MODE_FULL_HIGH_PERF` 替换为只在亮屏前台生效的低延迟锁，故不增加一个对 API 36 锁屏无效且增加耗电的 WifiLock。

Redmi K40/API 36 的首轮真机锁屏推翻了“不请求电池优化豁免”的假设：系统在锁屏后将应用 WakeLock 标为禁用，局域网地址和监听端口在手机内部仍存在，但电脑端 ping/TCP 均不可达，P: 在三次健康检查失败后被安全卸载。Android 官方说明 Doze 会暂停网络并忽略 WakeLock；部分豁免应用才可继续使用网络和部分 WakeLock。由于持续局域网文件服务是本应用的核心功能且 FCM 无法替代电脑主动访问，开始共享前改为检查 `PowerManager.isIgnoringBatteryOptimizations()`，未通过则以 `ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` 请求用户确认，返回后再次检查，拒绝即不启动共享。声明普通级 `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`，不使用 MIUI/三星私有白名单，也不静默变更系统设置。项目只作第三方 App 分发；用户接受共享期间的额外耗电，停止共享后必须释放全部运行资源。

用户移除 PhoneBridge 的 Android 最近任务视为明确退出共享。普通离开界面或锁屏继续前台共享；任务移除、应用/通知“停止共享”和系统强制停止均结束服务，不保留 PhoneBridge 后台进程或自动重启意图。

Windows 自动恢复以严格 session 健康检查为依据，不把 mDNS 缺失直接等同离线。连续三次暂时性失败才安全卸载；待上传状态禁止强制卸载。恢复仅跟随同一 `device_id` 候选并重新验证保存 CA、凭据、`share_ready` 和模式。身份/授权变化失败关闭；主动卸载清除恢复意图。依据：[Android 前台服务](https://developer.android.com/develop/background-work/services/fgs)、[保持设备唤醒](https://developer.android.com/develop/background-work/background-tasks/awake)、[Doze 限制](https://developer.android.com/training/monitoring-device-state/doze-standby)、[WifiLock API 34 行为](https://developer.android.com/reference/android/net/wifi/WifiManager#WIFI_MODE_FULL_HIGH_PERF)。

P1-010 实施补充：221 项 Windows 测试通过。Redmi K40/API 36 的未豁免失败、用户确认豁免后的锁屏读写、Wi-Fi 中断安全卸载和仍锁屏自动恢复均已实测；测试文件由手机端独立回读和散列。该证据只关闭本任务限定范围，不扩大到长期运行、PC 睡眠、重启或大文件缓存恢复。

P1-011 实施补充：Samsung SM-S9180/API 36 在公开权限和电池优化用户豁免下通过首次配对、锁屏双向读写、再次共享、最近任务移除和通知停止。停止后服务、通知、端口、WakeLock、盘符与 rclone 均消失；Android 系统保留的 `CACHED_EMPTY` 进程无活动组件和 CPU 增量，不改变“无后台共享”的判定，也不增加应用自行终止进程的实现。

## ADR-023 托盘使用系统 NotifyIcon，保持单一连接所有者

P1-012 采用 .NET 自带 Windows Forms `NotifyIcon` 承载 WPF 托盘图标和本地化菜单，不增加第三方运行时依赖。窗口右上角关闭在托盘可用时只隐藏；打开操作恢复同一个窗口；明确退出复用现有安全卸载，不另建后台服务或第二套连接状态机。托盘创建失败时继续显示窗口，关闭窗口执行安全退出，避免失去控制入口。

托盘状态由现有连接状态确定，优先级为错误、已挂载、连接中、已发现、离线。退出遇到待上传未确认或卸载失败时必须恢复窗口并保留进程、盘符和缓存归属。P1-012 当时的单实例桌面只持有一个 `ConnectionClient` 和一个 `MountManager`，同时活动挂载数为一；该历史限制后来由 ADR-039 的按设备会话替代，托盘改为聚合全部会话但仍保持同一安全退出规则。

依据：[Microsoft NotifyIcon](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0)、[ContextMenuStrip 属性](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.contextmenustrip?view=windowsdesktop-10.0)。P1-012 的 227 项测试与 Samsung 实际隐藏、恢复、退出验收通过；各状态图标视觉变体、英文系统布局和真实待上传退出失败界面仍未逐项实测。

## ADR-024 未打包客户端使用当前用户启动文件夹且不自动挂载

当前 WPF 客户端没有包身份。P1-013 最初使用 Microsoft 支持的 `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`；P1-030 的首次真实重启证明本机 Windows Shell 正常执行其他已批准 Run 项，却未枚举或执行新写入的 PhoneBridge 值，安装版没有启动。为使用同样受 Windows 启动应用管理、但在该用户环境可可靠执行的受支持入口，改为当前用户 Startup 文件夹中的 `PhoneBridge NG.lnk`，不引入 MSIX、管理员任务或系统服务。

设置仍默认关闭并由用户决定。快捷方式目标固定为当前 exe 绝对路径，参数只能是 `--startup`，工作目录为 exe 所在目录。快捷方式是状态源；创建后必须重新解析并核对目标与参数，同名异常文件视为外部冲突，不覆盖、不删除。安装升级只把精确匹配当前安装路径的旧 Run 值迁移为快捷方式，成功创建后才删除旧值；卸载也只删除精确匹配的旧值或快捷方式。

`--startup` 只创建唯一客户端并隐藏到托盘。它不读取配对记录以触发连接，不启动恢复进程，不启动 rclone，也不创建盘符；电脑重启后由用户打开客户端并手动选择设备连接。已经由用户手动建立的活动连接仍沿用原有健康检查和网络恢复策略。当前仍只有一个 `ConnectionClient`/`MountManager`。

依据：[Startup apps - Win32 apps](https://learn.microsoft.com/en-us/windows/win32/w8cookbook/startup-apps)、[Run and RunOnce Registry Keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)。P1-030 已确认 Startup 快捷方式由 Shell 执行；最终实现的 Release 构建为 0 warnings/0 errors、280/280 测试通过，手机在线时运行安装版 `--startup` 40.661 秒保持一个隐藏客户端、0 个 rclone、无 P:。

## ADR-025 日志使用固定字段 JSONL，诊断包仅显式白名单导出

P1-014 选择 .NET 自带 `System.Text.Json` 与 `System.IO.Compression.ZipArchive`，不引入日志、遥测或崩溃采集 SDK。日志 API 只接受固定事件、级别、结果码、状态、计数和耗时，禁止任意消息和原始异常文本，从入口上避免事后正则脱敏遗漏。日志目录限制为当前用户与 LocalSystem，单文件 1 MiB、最多 5 个；日志故障失败开放，不改变文件传输状态机。

诊断包由用户明确选择保存位置，只包含 `manifest.json` 和轮转日志快照。导出先写同目录随机临时文件，再通过同卷移动提交；失败保留原日志并清理自己的临时文件。manifest 不记录用户名、计算机名、设备身份、网络地址、文件路径或凭据。依据：[.NET ZIP API](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression?view=net-10.0)、[ZIP/TAR 最佳实践](https://learn.microsoft.com/en-us/dotnet/standard/io/zip-tar-best-practices)、[DirectorySecurity](https://learn.microsoft.com/en-us/dotnet/api/system.security.accesscontrol.directorysecurity?view=net-10.0)、[File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0)。实现与实际验证结果以 P1-014 验收记录为准。

P1-014 实施补充：日志入口、1 MiB × 5 轮转、受保护 ACL、并发写入、失败隔离和 ZIP 原子提交由 11 项新增 Desktop 测试覆盖，全量 254 项通过。实际 WPF 导出包白名单、JSONL 解析、禁止字段扫描、临时文件清理和导出后继续写入均通过；没有因本任务加入网络传输或第三方运行时依赖。

## ADR-026 手动地址是已保存身份的会话内路由提示

P1-015 不把手动 IP/端口加入发现注册表，因为手动输入本身没有可认证的 `device_id`，无法安全关联任意附近设备。桌面端改为按已有配对记录保存一个内存端点，自动发现端点与该端点只在界面选择和当前进程的恢复路径合并；应用退出或用户清除即丢弃，不持久化。

解析继续复用 `CandidateParser.TryManual` 的严格 IP 规则，端口文本另要求规范十进制 1–65535。实际连接完全复用 `ConnectionClient`，所以保存 CA、SAN、Token、设备/客户端身份、共享就绪和模式校验均保持不变；错误地址或错误证书不能被手动入口覆盖。固定枚举日志只记录添加、清除或拒绝结果，不接收地址或端口。

该入口允许已有 Pending/Active 记录连接，并允许 RevocationPending 记录重试远端撤销；不实现首次手动配对。当前 Android 文件共享端口固定为 8273，但配对端口动态分配，且手动表单不包含 PAKE 所需的可信身份/窗口信息；将 8273 猜作配对端口或允许跳过身份校验都会破坏 ADR-017/019。首次配对在 mDNS 完全不可用环境中的安全引导需独立协议和界面设计。

P1-015 实施补充：临时端点按保存的 `device_id` 隔离，自动端点优先，缺少匹配 PairedV3 候选时才用于恢复。Samsung 真机在防火墙阻断 UDP 5353、列表没有 mDNS 候选时完成手动连接，并在 Wi-Fi 中断后的相同条件下自动恢复；阻断期间 TCP 8273 保持可达。专用入站/出站规则已按名称删除并回读为不存在。实际 IPv6 手动连接、首次手动配对和地址变化后的手动更新不在本决定的已验证范围内。

## ADR-027 本地预览使用按用户安装的 Inno Setup 单入口

P1-024 解决 D-07 的本地交付边界。Windows 采用 Inno Setup 7.1.0 生成按当前用户安装的单一 EXE，不要求客户端本身提权；包内自包含 .NET 10.0.12 和固定 rclone 1.75.1。WinFsp 2.1.25156 使用未修改且 NAVIMATICS 签名有效的官方 MSI，安装器仅在注册项和 `winfsp-x64.dll` 均缺失时显示系统管理员确认并运行，不在线下载。卸载 PhoneBridge 不移除可能被其他软件共用的 WinFsp。

运行中的客户端以 `Local\PhoneBridge-NG-Installer-Guard` 互斥体阻止升级或卸载覆盖，不强杀 rclone 或盘符。升级和卸载保留 `%LOCALAPPDATA%\PhoneBridge-NG` 中的 DPAPI 配对、会话和可恢复缓存；卸载只在自启动值仍精确指向本安装路径时删除该值。Android 本地 APK使用既有测试证书以支持同签名覆盖安装，正式发行必须使用独立长期密钥，不能把测试证书当产品签名。

当前电脑的安装、运行中阻断、升级、卸载、最终重装和 Samsung 覆盖安装已通过；无 WinFsp 的干净 Windows 分支尚未实机验证，且 Windows 安装器未作 Authenticode 产品签名。因此该决定只关闭本地预览的依赖与安装方式，不代表公开发行已完成。证据见 [P1-024 验收](docs/audit/P1-024-VALIDATION.md)。

## ADR-028 大目录使用 Explorer 现有能力并固定性能通过线

P1-027 开始测试前确定：5,000/10,000 个有效 JPEG 时，共享到 P: 不超过 30 秒；Explorer 在 5 秒内打开且保持响应；首次完整枚举分别不超过 15/30 秒；同会话第二次完整枚举不超过 5 秒。不得有重复 P:/桌面客户端/rclone、崩溃或无法安全停止。网络读取量仅观察，不在测后追加阈值。

Samsung SM-S9180/API 36 与当前安装版在 10,000 项下取得 2.628 秒挂载、1.072 秒首枚举、0.047 秒复枚举和 2.155 秒 Explorer 完整计数，进程唯一且响应，因此 D-08 已解决。首版继续使用 Explorer 自带目录与缩略图能力，不增加专用缩略图服务、分页协议、缓存调参或新 UI。该证据只覆盖 10,000 个小型有效 JPEG 的目录/元数据路径，不外推真实大照片解码或更大规模。

## ADR-029 Android 测试签名与长期第三方发行签名严格分离

P1-035 保留默认 `LocalTest` 模式，用本机标准 Debug key 生成明确带 `local-test` 的 APK，只服务本地验收。长期第三方侧载必须显式选择 `ThirdPartyRelease`：keystore 必须位于项目目录外，别名不得为 `androiddebugkey`，密码值只从调用进程指定的环境变量读取，命令行只包含环境变量名称。

发行构建还必须预先给出证书 SHA-256。脚本在删除/重建输出前从 keystore 读取证书，拒绝 Android Debug 身份和摘要不匹配；签名后从 APK再次读取证书并核对同一摘要。manifest 只记录签名模式、证书摘要和产物散列，不记录 keystore 路径、别名、密码或环境变量名称。正式长期私钥由单独任务在确定受保护保存和备份边界后生成，构建脚本不得自行创建或复制私钥。

一次性项目外测试密钥已验证五种错误配置失败关闭及正确发行路径；测试私钥和测试发行产物随后删除。默认 LocalTest 交付也已重建，证明兼容现有本地验收流程。证据见 [P1-035 验收](docs/audit/P1-035-VALIDATION.md)。

P1-036 补充长期身份的一次性初始化边界：固定 PKCS12、RSA-4096、`phonebridge-release` 别名和产品证书主题；主副目录均必须位于项目外且路径互不包含。密码只从当前进程环境读取，两个 PKCS12 密码必须相同且至少 16 个非控制字符。生成后必须核对主副 keystore SHA-256、证书 SHA-256 和两份身份记录；主目录 ACL 只允许当前用户与 SYSTEM。脚本无法证明备份位于不同物理介质，操作者必须选择独立受保护介质。任何已有目标都失败关闭，提交后异常不自动删除可能唯一的密钥。验证证据见 [P1-036 验收](docs/audit/P1-036-VALIDATION.md)。

P1-038 将 `ThirdPartyRelease` 的输入收口为 P1-036 生成的身份 JSON，不再接受手工组合 keystore、别名和预期证书摘要。构建从身份记录同目录定位 `.p12`，并绑定 schema、用途、固定别名、keystore SHA-256、证书 SHA-256、主题和有效期；记录或密钥位于项目内、使用重解析点、散列/证书不一致、证书未生效或已过期均失败关闭。密码仍只来自当前进程环境，manifest 不记录身份路径、keystore 信息或环境变量名称。一次性项目外身份的正确发行路径和八项错误配置已验证，证据见 [P1-038 验收](docs/audit/P1-038-VALIDATION.md)。

P1-040 修正 P1-036 只验证目录分离的不足。主密钥必须位于本地固定磁盘；本地固定卷备份通过 `Get-Partition` 的 `DiskNumber` 与主卷比较，同一物理磁盘上的不同分区或盘符也拒绝。备份只接受不同物理磁盘的固定卷、可移动卷或网络卷。网络卷只证明访问路径独立，脚本无法核验远端实际磁盘、加密和备份管理，仍由操作者负责。当前 C:/D: 均为 Disk 0，真实拒绝用例通过；外置介质正向生成等待设备接入，证据见 [P1-040 验收](docs/audit/P1-040-VALIDATION.md)。

P1-041 在磁盘 0 建立正式长期身份，并在磁盘 1 的 USB 可移动介质建立逐字节一致备份，补齐 P1-040 正向证据。正式 APK的证书 SHA-256 固定为 `66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4`；后续所有可覆盖升级的第三方侧载 APK必须继续使用该身份。身份、备份和隔离正式构建的 17 项一致性检查通过，证据见 [P1-041 验收](docs/audit/P1-041-VALIDATION.md)。

P1-042 已将 Samsung 测试机从本地测试证书迁移到上述正式身份。首次跨证书迁移必须卸载并重新配对；此后同一正式 APK通过 `adb install -r` 覆盖时，应用数据、共享目录选择和电脑配对均保留，且无需重新输入配对码即可恢复 P:。这证明正式身份具备当前版本的升级连续性，证据见 [P1-042 验收](docs/audit/P1-042-VALIDATION.md)。

P1-043 不重新构建二进制，直接把 P1-042 按 APK SHA-256 验收过的正式五文件集合提升为默认交付。提升采用同卷暂存、完整校验和目录重命名切换，成功后保留隔离正式源作为回滚副本；默认目录不再包含本地测试签名 APK。证据见 [P1-043 验收](docs/audit/P1-043-VALIDATION.md)。

P1-044 将 GPL 对应源码作为正式交付的一部分，而不是依赖工作目录或口头承诺。源码包只包含 Android/Windows 源码、构建脚本、测试、产品文档和许可，明确排除 `.audit`、版本库元数据、构建输出、缓存、测试结果和私钥文件类型；ZIP 内的逐文件 SHA-256 清单必须全部回读通过。每次新的正式二进制或源文件变化后都必须重建该包，并由默认 manifest 和 SHA256SUMS 绑定。证据见 [P1-044 验收](docs/audit/P1-044-VALIDATION.md)。

P1-045 保持 Inno Setup 的按用户安装、固定 AppId 和依赖逻辑不变，只移除 `AppVerName` 和文件描述中的本地预览标签。新安装器复用相同已验证 Windows publish 内容和 WinFsp MSI；当前机同版本覆盖安装保留 DPAPI 配对且不启动后台进程。Windows Authenticode 仍为 `NotSigned`，在未购买受信任代码签名证书的第三方分发边界下如实写入 manifest。证据见 [P1-045 验收](docs/audit/P1-045-VALIDATION.md)。

## ADR-030 v0.2 使用统一浅色视觉系统与“桥接文件”图标

P2-002 的 r1 静态设计基线采用冷灰页面、白色单层卡片、科技蓝主色、青色连接强调、独立语义状态色和克制的圆角线性图形语言。Windows 固定“设备 / 设置 / 关于”分层及按状态变化的设备卡片主操作；Android 固定共享状态、共享目录、一个开始/停止主按钮、配对入口和已配对电脑的首页层级。简体中文主稿及 English 关键路径、按钮命名、控件状态和视觉变量以 [设计包](docs/design/v0.2/README.md)为准。

统一识别图标选择 A“桥接文件”：手机轮廓、文件页角和连接桥组成彩色主图；Windows 16 px 托盘使用独立单色减线变体，Android 使用保留中心安全区的 adaptive icon 变体。B“双端连接”和 C“文件入口”仅保留为比较记录，不作为 v0.2 正式资产方向。

用户已在 2026-09-24 明确确认 r1 视觉方向、Windows 分层/按钮命名、Android 页面层级和图标 A。该决定只冻结设计，不代表 WPF/Android 已实现，也不代表 DPI、字体放大、高对比度、辅助功能或平台图标资产已经验收。

## ADR-031 UI 验收候选先于多设备核心，并使用可区分的本地预览身份

用户在 P2-002 冻结后明确要求先生成各端应用，验收实际 UI 效果。因此 P2-003 先把 r1 落到原生 WPF 与 Android 页面，保持现有单会话业务核心，不在同一任务开始按 `device_id` 隔离的多会话重构。当时多设备核心顺延为 P2-004；用户复验通过后又插入本地移除/截屏任务，当前编号调整见 ADR-033。

Windows 验收入口使用显式 `--ui-preview` 参数和独立单实例互斥体，使候选窗口可以与当前安装版同时显示；这不隔离 `%LOCALAPPDATA%` 中的配对、设置或缓存，所以并行期间只允许浏览 UI，不允许两个客户端同时连接或挂载。Android 使用 `uiPreview` build type、独立包名 `org.phonebridge.ng.uipreview` 和明确的“UI 验收”名称，避免覆盖正式包及其数据；该 APK 使用本地调试签名，不属于正式升级或发布链路。

Android 正式 Debug/Release 当时保留 `FLAG_SECURE`。Windows 六页通过实际窗口回读，Android 首页和设置页通过 Samsung 实际屏幕及 UI hierarchy 回读；该截屏决定后来由 ADR-033 按用户明确要求覆盖。

## ADR-032 P2-003 首轮反馈使用验收身份定向修正，不改变正式安全边界

用户在首轮实际视觉检查后指出：两端应用图标未使用已确认的 A 方案、Android 安全策略阻止验收截图、设置和返回图标过小、设置项需要独立菜单以便后续扩展。P2-003 r2 因此把 A“桥接文件”接入 Windows EXE/窗口及 Android adaptive/monochrome 启动器资源，把 Android 设置与返回改为 48dp 图标按钮，并把设置改为菜单、语言改为独立子页。

截图策略当时只在独立包名和调试签名的 `uiPreview` build type 中放开；正式 Debug/Release 仍设置 `FLAG_SECURE`。该历史边界后来由 ADR-033 覆盖。安装器、完整托盘状态图标、正式签名与升级链路顺延为 P2-007。

## ADR-033 用户移除改为本端直接删除，所有构建允许截屏

P2-003 r2 由用户复验通过后，用户明确要求：Windows“移除此手机”和 Android“移除此电脑”都直接删除本端与该设备有关的配对信息，不等待或要求远端确认；再次连接按全新设备配对；Windows 和 Android 均允许截屏。因此插入 P2-004 实现此策略，原多设备核心顺延为 P2-005。

Windows 当前按钮停止该设备挂载并删除本机 DPAPI 配对记录，同时清除临时地址和进程内恢复状态；不再调用远端 self-revocation。Android 本地按钮在授权提交锁内从加密库精确删除 client 的名称、访问模式和凭据验证值，并关闭其活动连接。远端 `DELETE /phonebridge/v1/pairings/self` 及 Revoked 墓碑仍保留给旧客户端兼容和配对取消流程，但不再是当前本地移除按钮的依赖。

单端不联系远端就不可能保证远端记录也已删除，因此产品只承诺本端完整移除，不宣称双端同步清除。旧 token 在删除端立即失效；重新连接必须产生新的 client_id/token。Android 删除 `FLAG_SECURE` 及 build type 截图开关，所有构建允许系统截屏；应用仍禁止把配对码或凭据写入日志、遥测和剪贴板，用户负责截图的保存与分享。

## ADR-034 验收身份按修订隔离，首次流程改为先配对、后共享、手动连接

P2-004 实机准备确认两个缺陷具有同一验收状态根因：Windows `--ui-preview` 只隔离单实例互斥体，仍读取正式 `%LOCALAPPDATA%\PhoneBridge-NG\Pairings-v1`；Android `uiPreview` 固定包名并使用覆盖安装，保留上轮数据。同时 Android“配对新电脑”被错误依赖于共享引擎已启动。旧记录使 Windows 对已配对候选禁用“配对”，而手机开始共享后既有身份又触发 Windows 已保存路径，表现为未完成新配对却自动连接。该结论来自源码、正式目录中的既有记录和实际候选状态，不再把现象归因于按钮视觉问题。

首次流程固定为：Android 在未共享时打开“配对新电脑”并启动仅配对前台服务；Windows 完成身份/授权后只保存 Active 记录；手机开始共享；用户在 Windows 手动点击“连接”。仅配对状态的 session 返回 `share_ready=false`，文件路由返回 `share_not_ready`，不得启动 rclone、创建盘符或设置恢复意图。共享开始后 session 才报告就绪，但仍不自动连接。

每轮 UI 验收必须使用未复用的修订号：Windows 程序版本随修订变化，并只读写 `%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\<assembly-version>`；Android 包名为 `org.phonebridge.ng.uipreview<revision>`，使用不带 `-r` 的全新安装。构建脚本拒绝复用已存在的修订输出。该隔离仅用于验收候选，不改变正式升级语义：同一正式签名/包名的正式升级仍应保留用户配对和设置。本文覆盖 ADR-031 中“Windows 预览不隔离数据”和固定 Android 预览包名的历史描述。

## ADR-035 未配对候选只用于显式配对流程，连接状态绑定真实挂载

用户在 r12 实机验收中确认：Windows 不需要在“我的手机”中展示同网段未配对设备；必须由 Android 先打开“配对新电脑”，Windows 再进入“添加手机”、输入一次性配对码并由手机批准。mDNS 仍作为后台定位配对端点的传输机制，但普通未配对广播不进入 UI；只有带有效配对窗口属性的未配对候选可进入“添加手机”流程。主设备列表只显示本机已有配对记录的手机。

“已连接”严格表示当前存在真实挂载，并且非空配对设备 ID 与非空挂载设备 ID 精确相等。发现候选、打开配对窗口、输入配对码或仅保存配对都不得显示“已连接”、显示“打开文件/断开”，也不得启动 rclone 或创建盘符。首次流程继续遵循 ADR-034：配对成功后手机开始共享，用户再从 Windows 手动连接。

## ADR-036 配对记录独立于共享运行态，并明确两端本地体验

用户在 r13 验收后确认：Windows 配对完成应返回设备首页；Android 停止共享后仍要显示全部已配对电脑并允许本地管理；Windows 要为已连接手机保存本地名称和备注。配对身份属于持久安全状态，不能由共享引擎是否运行决定。因此 Android 在服务启动时独立读取加密 PairingStore，离线详情、访问模式修改和本地移除直接使用同一库；仅配对引擎在配对完成或关闭窗口后释放。Windows 配对记录 schema 2 增加本地名称和备注，继续读取 schema 1；这些字段只影响本地显示，不参与 device_id、client_id、token 或证书身份校验。

Android 共享目录保留原有七项及索引，在末尾增加“手机存储”，映射系统允许访问的内部共享存储根目录。该选项不绕过 Android 对应用私有目录的限制。配对完成后可重新选择目录，再开始共享。

Android 子页返回上级；首页二次返回在共享中只把界面置于后台并保持前台服务，未共享时退出。设置中的“退出应用”先停止共享再退出，避免把系统的界面后台化与用户明确退出混为一谈。Android APK 永久只通过 GitHub 分发，不规划 Google Play 或其他应用商店发布。

## ADR-037 手机批准后必须保留 Active 结果供 Windows 完成确认

r14 真实验收确认：手机批准已经把长期记录可靠写为 Active，但 Android UI 随即关闭配对窗口和配对专用引擎；Windows 尚未完成按 1.1 秒间隔的状态轮询，下一次 GET 得到 401，因此安全地保留 Pending，界面显示“待确认/继续连接”。之后用户点击“继续连接”时，严格 session 使用同一长期凭据验证成功并挂载，证明短码、发现和长期凭据本身正确。

现有 PAIRING 规范已经要求 Active 在原 120 秒窗口期限内可回读且轮询不能延长期限。r15 据此修复实现而不新增协议：手机批准后 UI 可以返回首页、进入系统权限页或暂时后台，但不能取消 Active 结果；尚未批准的窗口仍按原规则在离开前台时取消。Windows 收到 Active、验证严格 session 并更新本地记录后自动返回设备首页，不自动连接或挂载；“继续连接”只保留给真实中断或结果不确定状态。配对专用服务在窗口自然结束且未共享时自动停止，避免常驻。

2026-09-26，用户完成 r15 配对主流程真实验收并确认可正常使用；该证据关闭 r14 的批准结果收口竞态，但不代表 P2-004 其余记录管理、手机存储浏览、两端移除/重配或正式 v0.2.0 发布已经验收。

## ADR-038 两端设备备注只属于各自本地配对记录

用户随后确认 P2-004 剩余记录管理、手机存储跨端浏览、两端移除、旧凭据失效和全新配对均已验收通过，并要求两端统一为单一“设备备注”。Windows 不再同时显示“自定义名称”和独立“备注”：既有显示别名字段改名为“设备备注”，继续决定设备卡片标题；schema 2 中旧长备注字段只为已有记录兼容保留，不再进入界面或卡片。

Android `PairedClient` 增加本机 `deviceNote`，加密 `PBS1` 格式升为 version 2，并继续读取 version 1 为空备注。备注有界、可清空，只保存在该手机，卡片优先显示备注并同时保留原始电脑名称；它不通过协议发送，不改变 client_id、token、访问模式或授权状态，保存时也不关闭现有连接。两端备注互不自动同步，避免把本地显示信息误当成远端身份。

## ADR-039 Windows 多设备使用按 device_id 隔离的会话与轻量协调器

P2-005 实施前审计确认，现有单会话限制同时存在于 `ConnectionClient` 的唯一 `ReadOnlyMountManager`，以及 WPF 的唯一操作任务、取消令牌、重连策略、健康监督和挂载日志观察。只把一个挂载管理器改成字典仍会让取消、监督或 UI 忙状态串设备，因此所有这些运行态必须一起下沉到按 `device_id` 创建的设备会话。

每个设备会话继续复用现有严格 TLS、DPAPI 凭据、rclone 进程所有权、随机认证 RC、每设备缓存租约和安全停止规则。会话协调器只维护会话集合、盘符预留、退出停止和托盘汇总；不同设备的操作不经过全局长时锁。日志使用进程内单调会话序号区分上下文，不记录 `device_id`、设备名、地址、盘符、路径或凭据。一个会话失败或被取消不得取消、停止或清除另一个会话的恢复意图。

进程内盘符预留先于 rclone 启动，跨进程冲突继续由既有命名租约和实际盘符检查负责。短暂离线后的自动恢复保留该设备原盘符预留；用户主动断开或本地移除才释放。应用退出必须尝试停止所有会话并汇总失败，不能因第一个失败跳过其余会话；只要任一会话的待上传或卸载状态未确认，桌面进程就不得伪装退出。

实现保持既有 `VfsCache-v1/<证书 SHA-256>` 缓存根稳定，只把短期 CA/config 会话文件放入按 `device_id` 哈希的目录。2026-09-26 的 r20 验收确认 Redmi P: 与 Samsung E: 同时挂载、各自双向小文件散列一致；停止 Redmi 后 Samsung 会话继续浏览与传输。日志 schema 2 的会话序号 1、2 可区分上下文，设备身份模式扫描为 0 命中。证据见 [P2-005 验证](docs/audit/P2-005-VALIDATION.md)。

## ADR-040 应用语言按数据根持久化并由单一目录动态刷新

P2-006 保持 ADR-007 的简体中文默认和 zh-CN/en-US 资源目录，不引入第三方本地化框架。Windows 把非秘密语言选择保存到当前正式或 UI 候选数据根的 `Settings-v1/language.txt`；缺失、损坏或不支持值回落简体中文。WPF `TextExtension` 绑定单一可通知目录，语言变化同时刷新静态 XAML；设备卡片和底部状态保留资源键后重建，托盘订阅同一变更事件。对话框、错误与诊断入口在调用时读取当前目录。

Android 继续使用私有 SharedPreferences 保存 `zh-CN`/`en-US`。Activity、SharingService、通知和通知频道均通过 `AppLanguage.wrap` 读取同一选择；切换时重建当前 Activity 并刷新已有前台通知。协议错误码、诊断字段、设备身份和凭据不随显示语言变化。

Windows 卡片忙碌模型仍从对应 `DeviceSession` 的操作和监督状态生成；只有该卡主操作/设置被禁用，可安全取消时只显示本卡取消，其他设备卡不受影响。r21 已验证 Windows 与 Redmi 当前界面和冷启动 English 保持；Android 真实前台通知外观因未扩大到存储/通知权限与共享而未触发，不能从实现证据推断为真机外观已通过。证据见 [P2-006 验证](docs/audit/P2-006-VALIDATION.md)。
