# PhoneBridge NG — Phase 0 上游审计

审计日期：2026-09-19。任务：P0-001。**结论：源码审计已完成；Phase 0 实机链路尚未验证，不能进入 Phase 1，也不能称 MVP 完成。**

上游具备 Android WebDAV/HTTPS/mDNS 与 Windows rclone 挂载的实现，但存在数据安全、身份验证及挂载生命周期缺陷。交接说明中的“已验证技术路线”不能替代本项目的端到端验收。保留用户指定的技术路线，本轮没有改写任何上游产品代码。

## 1. 审计基线与证据边界

| 项目 | 本轮确认结果 |
| --- | --- |
| 上游 | [ysachin26/PhoneBridge](https://github.com/ysachin26/PhoneBridge) |
| 固定提交 | [`a378fec40561a4d18be2334f4de92ee02a0e7d0c`](https://github.com/ysachin26/PhoneBridge/tree/a378fec40561a4d18be2334f4de92ee02a0e7d0c) |
| 分支、提交时间 | `main`；2026-04-25T16:36:45+05:30 |
| 跟踪文件 | 83 个；测试后上游已跟踪文件改动数为 0 |
| 发布记录 | GitHub Releases API 返回 0 条；Git 标签 0 个 |
| 问题记录 | GitHub Issues API（state=all，包含 PR）返回 0 条；没有可引用的上游成功修复案例 |
| 本地参考副本 | `.audit/upstream/`，由项目 `.gitignore` 排除 |
| 附件 | [基线与校验值](audit/baseline.json)、[验证记录](audit/VALIDATION.md)、[离线复现脚本](audit/reproduce_upstream.py) |

证据等级：

- **源码事实**：在上述提交的实际执行路径中找到；不等于已在手机上运行。
- **离线复现**：调用原始 Python 模块，以临时文件或 mock 网络/进程验证特定行为；不等于真实挂载。
- **源码推断／潜在问题**：有具体触发路径，尚未在 Android、rclone 或 Explorer 实机复现。
- **未验证**：没有运行证据，不给通过结论。

审查覆盖 Android Kotlin、Manifest、Gradle 配置、资源文本，Windows Python 主入口及发现/认证/挂载/托盘/设置/启动/日志路径，测试、构建、安装脚本及遗留 Web UI。没有执行仓库自带安装器、启动网络共享、连接真实手机或安装驱动。

## 2. 许可证与工具链

### 2.1 许可证核对

**源码事实**：[LICENSE 第 25–28 行][license]明确声明 GPL 第 3 版或任意后续版本，对应 `GPL-3.0-or-later`，不能只根据 README 写成 `GPL-3.0-only`。LICENSE 共 33 行，只有前言节选、全文链接和授权/免责声明，不是完整 GPLv3 正文。GitHub API 的自动识别结果为 `NOASSERTION`；这不能推翻文件中的明确授权声明。

基于该代码修改并分发时，应保留上游许可证、已有作者及版权/免责声明，标记修改及日期，并随分发提供完整许可证。分发 APK/EXE 时，应按 GPL 第 6 节向接收者提供对应源代码及构建/安装所需脚本；不能仅放上游仓库链接替代修改版对应源码。私人修改本身不要求公开发布。GPL 允许商业分发，不等于允许把受其约束的衍生代码直接闭源。[GPL 正文及标识说明](https://spdx.org/licenses/GPL-3.0-or-later.html)

后续正式导入代码时补齐完整 GPL 文本和来源说明，保留原文件以便追溯。rclone、WinFsp、运行库及其他第三方组件的再分发条件仍需按实际选定版本单独核查；本轮未做完整依赖许可证或已知漏洞清单扫描，也未决定打包方式。

### 2.2 当前版本事实

| 对象 | 确认结果与限制 |
| --- | --- |
| Windows 新版目标 | Microsoft 当前稳定 LTS 是 **.NET 10**，官方页面列出 10.0.12，支持至 2028-11-14；.NET 11 RC1 是预发布，不选作稳定基线。[官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) |
| 本机 .NET | `dotnet --list-sdks` 无输出；有 .NET/WindowsDesktop 9.0.19、10.0.11 运行时。运行时存在不代表可以编译 C# 项目。 |
| Android 上游 | `minSdk=26`（Android 8.0），`compileSdk=34`、`targetSdk=34`、versionName 1.1.0。[app/build.gradle.kts][android-build] |
| Android 构建 | AGP 9.1.1、Kotlin 插件 2.2.10、Gradle wrapper 9.3.1，daemon toolchain 指定 JetBrains JDK 21。`gradle.properties` 已明确关闭 built-in Kotlin/new DSL；不能据 AGP 9 与旧插件并存就断言构建冲突。[构建配置][build-root]、[属性][gradle-properties]、[官方迁移说明](https://developer.android.com/build/migrate-to-built-in-kotlin) |
| 本机 Android/rclone | PATH 未检出 `java`、`adb`、`rclone`；常见 Android SDK/Android Studio JBR、WinFsp 安装目录未检出。未全盘搜索，不能据此断言机器任何位置都没有这些软件。 |

NG 的 Android 最低版本、targetSdk 及前台服务方案留到 Phase 0 后续决策；本轮只确认上游下限为 26，不把配置数值当作兼容性测试结果。

## 3. 已经由源码确认实现

### 3.1 Android 模块

| 模块 | 实际实现 | 限制 |
| --- | --- | --- |
| 界面与权限 | Kotlin + XML/ViewBinding；开始/停止共享、密码复制/重生成、共享目录选项、开机启动开关；Android 11+ 检查全部文件访问权限，旧版检查读写权限，13+ 检查通知权限。[MainActivity][activity] | 大量硬编码英文；不是中文资源体系。 |
| 文件访问 | `Environment.getExternalStorageDirectory()` + `java.io.File`；可选整个外部共享存储、DCIM、Download、Music。[共享根目录][storage-root] | 未实现 MediaStore/SAF 文件访问层或媒体变更通知；不是 Root，也不能绕过系统私有目录限制。 |
| WebDAV | NanoHTTPD 2.3.1；分派 OPTIONS、PROPFIND、GET、HEAD、PUT、DELETE、MKCOL、MOVE、COPY。[请求路由][dav-routing] | 属于协议子集；方法存在不代表完整符合 WebDAV 或大文件正确。 |
| 认证 | 所有请求先校验 HTTP Basic Auth，固定用户名、8 字符 SecureRandom 密码；可轮换。[认证][dav-auth]、[密码生成][password] | 这是长期共享密码，不是一次性配对码；没有每台电脑独立授权/撤销。 |
| HTTPS | Bouncy Castle 生成 RSA-2048 自签证书，存为应用私有目录内 PKCS12，默认有效期 10 年，交给 NanoHTTPD `makeSecure`。[TlsHelper][tls] | 没用 Android Keystore；TLS 初始化失败会退回 HTTP。 |
| 发现广播 | `NsdManager` 发布 `_phonebridge._tcp.`，8273 端口；TXT 含 version、deviceName、model、brand、sdk、auth_required、auth_user、protocol，可能含 tailscale_ip。[NsdAdvertiser][nsd] | 无持久随机设备 ID、公钥或经配对认证的身份。 |
| 服务与开机恢复 | `dataSync` 前台服务、低重要性通知及停止按钮、Wi-Fi lock、最长 10 小时 wake lock；用户启用后 BootReceiver 请求启动。[服务启动][service-start]、[BootReceiver][boot] | 息屏、Doze、系统杀进程后恢复与三星限制没有测试证据。 |
| 状态接口 | 认证后 GET `/phonebridge/status` 返回存储量、请求/传输统计、证书指纹等。[状态接口][status-api] | 下载计数在创建响应时即计入整个文件大小，连接数在 `serve` 返回时减少，不能当作真实传输完成量/活跃流数量。 |

全部文件访问权限仍受 Android 系统边界限制，不能据此承诺其他 App 私有目录或受限 `Android/data` 可访问；`Android/media` 属于官方允许范围，但本轮未实测。[Android 官方权限说明](https://developer.android.com/training/data-storage/manage-all-files)

### 3.2 Windows 模块与调用链

实际入口是 `python -m phonebridge.main` → `customtkinter` GUI + `pystray` 托盘；不是 C#，也不是遗留 `desktop/phonebridge/ui/` 的 HTML/JS 页面。`main.py` 未接入后者，requirements 也没有 pywebview。[main.py][main]、[requirements][requirements]

| 模块 | 实际实现 | 证据边界 |
| --- | --- | --- |
| 自动发现 | Python zeroconf 监听 `_phonebridge._tcp.local.`；解析 TXT、地址、端口，支持发现/丢失/更新事件。[discovery.py][discovery] | Android 的尾部 `.` 与 Python 的 `.local.` 是两端 API 表示差异，不据此判不兼容；发现时延未实测。 |
| 手动连接 | 输入地址、端口、HTTP/HTTPS 和密码，查询状态接口，加入设备列表并挂载。[手动连接][manual] | 不是扫码或一次性配对。 |
| 凭据/配置 | `%APPDATA%/PhoneBridge/config.json` 保存密码、IP、盘符、auto_mount、子目录和证书指纹等。[config.py][config] | 明文 JSON；无 DPAPI/Credential Manager。 |
| 挂载 | 以参数数组 `subprocess.Popen` 启动 `rclone mount :webdav: <盘符>`，采用 network mode 和自定义卷名。[mounter.py][rclone-command] | Windows 文件系统映射由 rclone/WinFsp 承担；Python 未实现驱动。 |
| WinFsp 检查 | 仅检查 Program Files 中 WinFsp 目录是否存在。[utils.py][dependencies] | 未查驱动版本、服务状态、架构、损坏安装或真实挂载；没有安装代码。 |
| 托盘/自动挂载 | 发现已保存且启用自动挂载的设备后，后台调用托盘 `_mount_phone`；丢失事件触发卸载。[发现回调][tray-found] | 自动路径与 GUI 认证/指纹行为不一致。 |
| 健康检查 | 循环检查 rclone 是否退出，并用 OPTIONS 检查凭据；401 时卸载/提示重输密码。[健康循环][health] | 网络失败仅记 debug，不自动判离线；不是完整重连状态机。 |
| 开机启动 | HKCU Run 键启用/禁用；配置默认 false。[startup.py][startup] | 源码运行命令写成 `-m phonebridge`，但包无 `__main__.py`；打包 EXE 分支另行处理。 |
| 日志 | 标准 logging，记录发现、认证结果、挂载/退出、错误等。[utils.py][logging] | 实际为普通 FileHandler，无轮转、统一脱敏、诊断包导出。 |
| 打包 | PyInstaller build.py + Inno Setup installer.iss。[构建][desktop-build]、[安装脚本][installer] | 仅打包主 EXE；没有 rclone/WinFsp 依赖部署及校验。 |

### 3.3 WebDAV、缓存与重连细节

| 操作 | 代码行为 | 不能据此宣称的能力 |
| --- | --- | --- |
| PROPFIND | 一次 `listFiles()`、排序，逐项生成 XML 元数据；不打开所有子文件内容；隐藏文件跳过。[XmlResponseBuilder][xml] | 没有 Android 目录元数据缓存、分页或大目录压测；`Depth` 除 0 外均只列一层。 |
| GET | `FileInputStream` 流式返回完整文件，200；添加 `Accept-Ranges: bytes`。[GET/HEAD][dav-get] | 未读取 Range、无 206/416 分支，不能宣称随机 Seek/断点下载可用。 |
| PUT | 按 Content-Length，以 64 KiB 缓冲直接写目标文件。[PUT][dav-put] | 没有临时文件原子替换、短写校验、chunked 请求体处理或中断恢复保证。 |
| DELETE | 文件删除或目录递归删除。[DELETE][dav-delete] | 没有只读/安全/完全读写模式、二次确认或回收站。 |
| MOVE/COPY | `renameTo` 或 `copyTo/copyRecursively(overwrite=true)`。[MOVE/COPY][dav-move] | 未处理 Overwrite/If-Match 等条件、锁及事务性替换。 |
| VFS | 默认 full，max-age 1h，read-chunk-size 32M，dir-cache 5s，poll-interval 10s，write-back 0s。[配置][config]、[命令][rclone-command] | 未限制缓存总量、最小剩余空间或提供用户缓存管理；没有单独设置 read-ahead。 |
| 断连/恢复 | mDNS lost → 终止进程；found → 有已存密码时再挂载。进程退出从字典移除；认证失败单独处理。 | 未实现带退避的统一重试、Windows 睡眠/唤醒监听、挂载中取消、IP 更新后的 remount。 |

`poll-interval=10s` 大于 `dir-cache-time=5s` 不符合官方对支持轮询 backend 的参数关系；不能假设该选项给普通 WebDAV 带来实时通知。缓存中的待上传文件只有在相同条件重启后才有恢复可能，不能把 `write-back=0s` 解释为写入已安全到手机。以上是配置风险，未运行 rclone 验证其具体表现。[rclone mount 官方说明](https://rclone.org/commands/rclone_mount/)

## 4. README 声称但尚未验证，或与当前仓库不一致

README 仅作为宣称来源，参见[固定版本 README][readme]，不作运行证据。

| 宣称/展示 | 核对结果 |
| --- | --- |
| 手机成为真实盘符、Explorer 拖放和直接打开文件 | 有实现调用链，没有本轮 Android → TLS → rclone → WinFsp → Explorer 运行证据。 |
| 自动挂载、多手机、断开后使用 | 部分回调实现存在；死锁、并发和设备标识问题见下文，未完成可靠性验收。 |
| Release 下载 Windows 安装器、便携 EXE、APK | 当前 Releases API 为 0；仓库只有一个已跟踪安装器 EXE，没找到已跟踪 APK。安装器签名检查为 `NotSigned`，未运行，也未证明与源码一致。 |
| 安装流程“自动设置一切” | 安装脚本只部署主 EXE；README 后续又要求用户另外安装 rclone/WinFsp。 |
| HTTPS、多层安全与稳定证书 | 服务端证书持久化存在；客户端传输未强制 pinning，TLS 失败还有 HTTP 降级，不能归纳为安全链路完成。 |
| 实时下载/上传速度与活跃连接 | 计数实现不是实际流的完整生命周期，不作为吞吐量证据。 |
| VPN 远程访问 | 有 Tailscale 检测、引导和 peer 扫描代码，未验证；不纳入 NG 本阶段范围。 |
| 开机自动启动与后台使用 | 有开关及 BootReceiver，不等于现代 Android/三星上可持续自动恢复。 |

## 5. 相对 NG 要求明显缺失的能力

以下缺失以本次提交的入口、模块和全仓关键词/文件搜索为范围，不代表未来上游版本也缺失。

| NG 要求 | 当前差距 |
| --- | --- |
| C# + LTS .NET + WPF | 尚不存在。Python 仅可作为功能参考。 |
| 一次性码/二维码配对、持久 Device ID、公钥交换、授权撤销 | 只有共享 Basic 密码、服务名/IP 派生标识和不完整 TOFU。 |
| Windows DPAPI/Credential Manager、Android Keystore | 均未接入；普通 JSON/SharedPreferences/PKCS12 文件。 |
| 全传输链路证书绑定、身份改变必须重新配对 | 未实现；GUI 可接受替换，托盘直接绕过检查。 |
| 安全模式默认、受控删除、回收站扩展点 | 所有认证客户端直接进入同一 CRUD 路径。Explorer 自身弹窗不能替代服务器端授权。 |
| 中文优先、zh-CN/en-US 资源 | Android strings.xml 仅 app_name，其他 Android/Windows 文本大量硬编码；没有完整 i18n。 |
| 自动离线卸载/恢复、IP 变化、睡眠唤醒处理 | 只有部分事件路径；缺少统一连接/挂载状态管理和持久身份。 |
| 诊断包、日志轮转/脱敏、缓存管理 | 不存在完整实现。 |
| 安装依赖、版本校验、可追溯发布 | 未实现一体安装；requirements 未锁版本，构建测试使用 pytest 却未列为开发依赖；无仓库 CI 工作流。 |
| 大文件、视频 Seek、大目录、断网完整性 | 无 Android 测试源码或端到端测试；小文本模拟器不能证明这些指标。 |

没有发现要求外新增遥测/账号后端的主执行路径；Tailscale 集成确实存在，不应跟随上游一并移入 NG。遗留 Web UI 引用在线字体，但它没有接入当前 Python 主入口，不能据此声称运行中的 native GUI 一定请求该资源。

## 6. 主要安全与数据风险

这里的优先级表示对 NG 后续工作的影响，不是 CVSS 评分。Android 风险均未在真实手机进行破坏性复现。

### S1 — 路径边界与根目录删除（高；源码事实 + 影响推断）

`resolveFile()` 用字符串 `startsWith(rootDir.canonicalPath)` 判断边界，未检查路径分隔符。例如共享根为 `DCIM` 时，同前缀兄弟目录 `DCIM_backup` 可能通过；实际可访问范围仍受 Android 进程权限限制。更严重的是，越界时返回共享根目录而非拒绝请求；DELETE 随后对返回的目录 `deleteRecursively()`。已认证的异常路径请求存在删除整个共享根内容的危险，直接根目录请求也没有保护。[resolveFile][path-resolve]、[DELETE][dav-delete]

后续验收应在可丢弃目录/模拟器中验证：越界、共享根及不合法路径全部拒绝，目标外数据不变。不得在有真实照片的共享根上复现此问题。

### S2 — 上传可先破坏旧文件、短写可能报成功（高；源码事实 + 影响推断）

PUT 直接 `FileOutputStream(file)` 截断已有文件，再读取输入；遇到 EOF 直接跳出循环，没有检查 `remaining==0`，随后返回成功。缺少 Content-Length 时默认 0；没有 chunked 解码处理。异常时可能留下半文件，原内容已经丢失。断网具体表现取决于输入流是 EOF 还是抛异常，但两者都没有回滚旧文件。[PUT][dav-put]

后续验收须覆盖已有文件覆盖、传输中断、磁盘满、短请求体、无长度请求；不能只检查 UI 显示“复制完成”。

### S3 — 证书绑定未保护真实数据路径（高；源码确认；部分离线复现）

- GUI 先 `check_auth()` 发送认证，再到 `_do_mount()` 获取/比较指纹；认证及状态查询的 SSL context 都设置 `CERT_NONE`。[GUI 认证及 pinning][gui-pin]、[状态查询][gui-status]
- 托盘及自动挂载路径直接认证和挂载，没有调用指纹校验。[托盘挂载][tray-mount]
- 最终 rclone 参数仍带 `--no-check-certificate`；前置探针与实际文件传输是独立连接，检查一次无法保证后续连接身份。[rclone 命令][rclone-command]
- GUI 指纹获取为空仍继续；`verify_fingerprint()` 失败返回 `(True, None)`，该 helper 本身并未被挂载入口调用。离线复现确认 helper 的放行行为及实际命令参数。[certpin.py][certpin]
- GUI 挂载后重新获取的指纹不再与刚才的已接受指纹比较；手动连接、托盘保存配置会丢失原 cert_fingerprint，见 B5。

因此“已有 certpin.py”不等于“已经实现可靠 certificate pinning”。后续必须让携带凭据和文件内容的每条 TLS 连接验证同一配对身份，失败则拒绝；本轮不决定具体实现方案。

### S4 — 凭据和密钥保护不达标（高；源码确认 + JSON 离线复现）

Windows 密码直接写 JSON；Android 密码直接写私有 SharedPreferences，私钥放在固定内置口令保护的 PKCS12 文件中，不是 Android Keystore。应用私有目录提供访问隔离，但不等于符合用户指定的密钥保护方案。[config.py][config]、[密码][password]、[TlsHelper][tls]

`rclone obscure` 调用把原密码放在进程参数中，mount 参数携带可还原的 obscure 值；异常日志直接格式化异常，超时异常可能包含带密码的命令。混淆不是安全加密，不能作为凭据保险库。未声称本轮发现了真实凭据泄漏。[mounter.py 191–218][obscure]、[rclone 官方说明](https://rclone.org/commands/rclone_obscure/)

### S5 — TLS/共享范围失败时扩大访问（高；源码确认；HTTP 解析离线复现）

TLS 初始化失败会继续启动 HTTP；发现端接受 TXT 声明的 HTTP（缺省也是 HTTP）。共享指定子目录不存在时，服务改为共享整个外部存储。两种回退均扩大了用户原本预期的访问面，应改为明确失败。[服务启动][service-start]、[discovery.py 117–160][discovery-resolve]、[共享根][storage-root]

### S6 — 删除/覆盖无独立保护（高；源码确认）

DELETE 直接永久删除，COPY 固定允许覆盖，MOVE/COPY 未处理 Overwrite 或写入条件，没有只读/安全模式。知道一个共享密码即可执行全部文件操作。密码不是单次有效配对授权。[认证][dav-auth]、[DELETE][dav-delete]、[MOVE/COPY][dav-move]

### S7 — 目录 HTML 未转义文件名（中；潜在问题）

目录页面把文件名、相对路径直接拼入 HTML 和 href，具有脚本/标记注入风险；需要浏览器访问可控文件名目录等条件。NG 不以浏览器为产品入口，但现存 HTTP 路由确实可到达。本轮未做浏览器攻击复现。[目录 HTML][directory-html]

## 7. 明显 Bug 与其他潜在问题

| 编号 | 证据等级、触发条件 | 结果及源码依据 |
| --- | --- | --- |
| B1 | **离线复现**：同设备已有退出进程，再次调用 mount | `mount()` 持有普通 Lock 时进入 `_cleanup_mount()`，后者再次取同一锁；线程栈确认死锁。[mount 251–260][stale-mount]、[cleanup 436–441][cleanup] |
| B2 | **离线复现（进程为 mock）**：两个同设备/盘符挂载请求并发 | 去重检查、占用检查与启动/登记未原子化；启动 2 个 mock 进程，字典只保留 1 个。真实 rclone 可能拒绝其中一个，不能据 mock 宣称真实两个盘符均成功；但管理器存在重复启动及遗失进程引用风险。[mount][rclone-command] |
| B3 | **源码确认**：视频/大文件请求 Range | GET 忽略 Range，却声明支持；可能导致 rclone 分段读取或视频 seek 失败/回读全文件。具体客户端行为待测。[GET][dav-get] |
| B4 | **源码确认**：特殊文件名 | NanoHTTPD 2.3.1 已 URL decode，`resolveFile()` 再 decode。比如编码后的加号在第二次解码变空格，百分号也可能二次解释。未在 APK 复现。[resolveFile][path-resolve]、[依赖官方源码](https://github.com/NanoHttpd/nanohttpd/blob/nanohttpd-project-2.3.1/core/src/main/java/fi/iki/elonen/NanoHTTPD.java#L678-L686) |
| B5 | **源码确认**：挂载/重连后保存配置 | 托盘重建 PhoneConfig 时未复制 cert_fingerprint、mount_path、auto_mount、connection_type 等，默认值覆盖原设置；GUI 手动连接在 `_do_mount` 保存后再次重建配置，丢失指纹和盘符等。[托盘 475–484][tray-mount]、[GUI 手动连接][manual] |
| B6 | **部分离线复现**：mDNS update 改变 IP | scanner 只更新其字典并调用可选 on_updated；主入口/托盘未接入这个回调，所以托盘地址与现有挂载 URL 可能过期；不会仅因更新事件重新挂载。[发现更新][discovery-update]、[托盘回调接线][tray-wiring] |
| B7 | **源码确认/潜在卡死**：rclone 长时间运行 | stdout/stderr 都 PIPE，活进程期间没有持续消费；大量错误可能填满管道阻塞。挂载成功仅由睡眠 2 秒后进程存活判断，没验证盘符就绪；该成功判定已离线复现。[进程启动][rclone-command] |
| B8 | **源码确认/数据完整性待测**：丢失事件或退出时有待上传缓存 | 直接 terminate，5 秒后 kill，无待上传队列确认、恢复状态或稳定缓存归属管理；不能保证手机写完。`--vfs-write-back=0s` 也不是“无待写数据”。[unmount/kill][unmount] |
| B9 | **源码确认**：Android 系统以 null Intent 重建 START_STICKY 服务 | `onStartCommand` 只匹配 action，null 不会重新 startServer；没有自动恢复共享的分支。[服务 94–102][service-start] |
| B10 | **官方约束；未来迁移风险**：targetSdk 升至 35+ | Android 15+ 对 dataSync 后台前台服务有 24 小时内 6 小时限制，且禁止从 BOOT_COMPLETED 启动该类服务。上游 target=34，不能把新 target 的限制直接说成它已在三星必然失败。[超时文档](https://developer.android.com/develop/background-work/services/fgs/timeout)、[启动限制](https://developer.android.com/about/versions/15/changes/foreground-service-types) |
| B11 | **源码确认**：源码版 Windows 开机启动 | 命令 `python[w] -m phonebridge`，包没有 `__main__.py`；工作入口为 `phonebridge.main`。打包版使用 EXE 路径，不是同一问题。[startup][startup] |
| B12 | **源码确认**：配置/日志 | config.json 非原子覆盖且无并发锁，异常可能破坏配置；FileHandler 不轮转，长期日志增长；卸载清理 LocalAppData，但实际配置写 Roaming AppData。[config][config]、[logging][logging]、[installer][installer] |
| B13 | **源码确认**：WebDAV 语义 | 宣布 `DAV: 1, 2` 却无 LOCK/UNLOCK；无 ETag/条件覆盖支持；COPY 忽略递归返回值可能在部分失败时仍报成功；共享 SimpleDateFormat 并发使用需要验证。[OPTIONS][dav-options]、[MOVE/COPY][dav-move]、[XML][xml]。[WebDAV 标准](https://www.rfc-editor.org/rfc/rfc4918.html) |
| B14 | **源码确认/影响待测**：GUI 指纹变化弹窗 | 等待循环注释称最多 60 秒，实际没有超时条件；也没设置窗口关闭协议来标记拒绝，关闭弹窗可能留下等待线程。[GUI 992–1022][gui-pin]、[弹窗][cert-dialog] |

额外局限：设备 ID 来自服务名或 IP，不是稳定的密码学身份；同型号、重命名、双网卡、IPv6 地址格式及多个发现来源均需验证。`parsed_addresses()` 直接取第一项且 URL 未包 IPv6 方括号。发现暂时不可达时 auto-mount 直接返回，没有排队重试；手动配置启动时只加进 scanner，不触发 found/自动挂载。[discovery][discovery]、[found][tray-found]、[manual restore][manual-restore]

## 8. 可复用与建议重写

### 可直接作为参考保留

- 用户指定的 **HTTPS/WebDAV → rclone VFS → WinFsp → Explorer** 分工；没有证据要求换协议或自建驱动。
- mDNS service type、现有 TXT 字段及基本 NsdManager/zeroconf 接线，作为互通基线；不能把 TXT 当作身份认证。
- 小型无状态辅助逻辑，例如 XML 字符转义、逐路径段编码、MIME 类型映射、格式化函数；复制前保留来源并单测边界。
- 上游测试结构和 mock 样例、参数/回调模型，作为后续测试输入，不把现有断言当安全规范。

**没有任何网络、写入、凭据或挂载模块被判定为可原样用于正式版本。** XML builder 中共享日期格式器、隐藏文件策略等仍需检查；局部可复用不代表整个文件可直接照搬。

### 建议重写或重点修正（仅审计建议，本轮未实施）

| 范围 | 原因 |
| --- | --- |
| Windows 客户端 | 按用户指定 C#/WPF 开发；把设备身份、发现、认证、挂载状态分清；消除 GUI/托盘不同安全路径。 |
| 凭据与配对/TLS | 改为系统安全存储、持久设备身份、所有请求一致验证；现有 Basic 密码输入不是最终配对方案。 |
| Android WebDAV 文件操作层 | 路径拒绝规则、根保护、写入完整性、原子替换、Range 和删除策略是关键；保留技术路线不等于保留有缺陷的操作实现。 |
| 生命周期/重连 | 统一处理启动中、在线、离线、身份变化、待上传、停止；串行管理同一设备的挂载进程。 |
| Android 后台与权限 | 基于目标 Android 版本重新验证前台服务和恢复策略，不能用厂商 hack 或旧 target 规避长期约束。 |
| i18n/安装/日志/缓存 | 满足中文资源、用户可选自启、可追溯依赖、日志脱敏/轮转和数据恢复要求。 |

这不是已批准的详细架构方案；按交接要求，重大架构方案要在下一文档任务中记录，实机基线门槛通过后才进入 Phase 1。

## 9. 本轮真实验证结果

| 验证 | 结果 | 说明 |
| --- | --- | --- |
| 上游 Python tests | **107 passed, 1 skipped**，退出码 0 | Python 3.12.14、pytest 9.1.1、zeroconf 0.151.3；跳过的是 Non-Windows-only 测试。 |
| 七项离线审计复现 | **7/7 复现预期现象**，退出码 0 | 明文 JSON、指纹失败放行、TLS 绕过及虚假就绪判定、mDNS 接受 HTTP、IP update 回调缺口、并发重复进程、旧挂载死锁；详见 [JSON](audit/reproduction-results.json)。 |
| Python 静态语法解析 | **21 个 .py 文件通过** | 不导入 GUI，不代表依赖齐全或能打包。 |
| 原始源码完整性 | 已跟踪文件变更 **0** | 没有修改 Android/Windows 源码；测试和环境位于本地审计区。 |
| 许可证/发布/安装器 | 源文件、API、SHA-256、Authenticode 状态已回读 | 安装器未执行，签名状态 NotSigned；不能证明二进制与源码一致。 |
| Android 构建/APK | **未运行** | 本轮环境未发现可用 JDK/SDK；未为本审计安装 Android 工具链。 |
| Windows GUI/安装/WinFsp/rclone | **未运行** | 只测试 Python 逻辑，未启动 GUI 或挂载。 |
| Galaxy S23 Ultra/Explorer | **未运行** | 未连接真实手机，未读写用户文件。 |

测试限制：上游测试大量使用 mock；它甚至断言 `verify_fingerprint` 不可达时应放行、obscure 失败应返回原密码。已有测试全部通过不能消除 S1–S6 风险。上游模拟器只是 HTTP + 小文本测试目录，不是 Android HTTPS/权限/电源管理替代物。[证书测试][test-cert]、[挂载测试][test-mounter]、[模拟器][simulator]

## 10. 必须保留为未验证的实机验收

以下是后续验收清单，本轮所有项目均为 **未验证**，不是建议现在一次执行全部操作。

| 验收组 | 需要的可核对证据 |
| --- | --- |
| 原始链路 | Android 构建/签名、权限、TLS、发现、rclone/WinFsp 版本；Explorer 实际出现盘符并在隔离测试数据上读、写、复制。 |
| 身份 | 首次配对、重启后身份保持、伪造发现、HTTP 降级、证书替换/探针失败均拒绝；不能先发送长期凭据。 |
| 文件操作 | DCIM/Download/Pictures、中文/空格/加号/百分号/emoji、长文件名、建目录、重命名、按授权删除；前后文件哈希一致。 |
| 大文件 | 100 MB、1/5/10/20 GB 双向 Explorer 复制/拖放；检查可用空间、完整大小和 SHA-256；播放及随机 Seek。 |
| 故障完整性 | 上传覆盖中断、Wi-Fi 断开/切换、磁盘满、进程异常退出；原文件与待上传数据的实际状态及恢复结果。 |
| 自动恢复 | Android/Windows 重启、手机锁屏/息屏/系统杀进程、PC 睡眠唤醒、路由器重连/IP 变化；无重复盘符/僵尸进程/无限提示。 |
| 大目录 | 5,000/10,000+ 照片目录的发现、列目录、UI 响应和网络读取量；Explorer 缩略图不作为已解决项目。 |
| 安装与诊断 | 干净 Windows 11 环境、依赖检测/官方安装引导、自启用户选择、日志脱敏、诊断包和卸载行为。 |

原始版本具有删除和覆盖风险。Phase 0 第三个任务应先选可丢弃测试设备/模拟器数据，记录限制；在无法隔离风险时停止写入验收，另立最小修复任务，不能因赶进度把主手机存储直接作为试验目录。

## 11. 建议的唯一下一任务

**Phase 0 第二个任务：建立完整项目结构和开发文档。** 按用户交接补齐 README、ARCHITECTURE、DEVELOPMENT_RULES、DECISIONS、PRODUCT/SECURITY/PROTOCOL/TESTING，明确上述风险、验收证据格式和第三任务的隔离数据方案。该任务仍不修改核心功能、不进入 Phase 1。

本轮按“完成审计后停止”收口；没有自动展开该下一任务。

## 源码定位

以下链接均固定到审计提交，不跟随 main 漂移。行号范围用于复核，结论仅对该版本成立。

[license]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/LICENSE#L1-L33
[readme]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/README.md
[android-build]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/build.gradle.kts#L1-L67
[build-root]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/build.gradle.kts#L1-L5
[gradle-properties]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/gradle.properties#L14-L15
[activity]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/MainActivity.kt#L351-L395
[storage-root]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/service/PhoneBridgeService.kt#L390-L409
[dav-routing]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L87-L142
[dav-auth]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L147-L166
[password]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/service/PhoneBridgeService.kt#L355-L385
[tls]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/TlsHelper.kt#L28-L149
[nsd]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/discovery/NsdAdvertiser.kt#L33-L103
[service-start]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/service/PhoneBridgeService.kt#L94-L187
[boot]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/receiver/BootReceiver.kt#L25-L51
[status-api]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L175-L201
[main]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/main.py#L110-L214
[requirements]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/requirements.txt
[discovery]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/discovery.py#L18-L163
[discovery-resolve]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/discovery.py#L117-L160
[discovery-update]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/discovery.py#L259-L263
[manual]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/gui.py#L1305-L1367
[config]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/config.py#L19-L114
[rclone-command]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/mounter.py#L278-L362
[dependencies]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/utils.py#L63-L107
[tray-found]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/tray.py#L634-L675
[health]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/mounter.py#L443-L503
[startup]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/startup.py#L20-L125
[logging]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/utils.py#L13-L60
[desktop-build]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/build.py#L30-L105
[installer]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/installer.iss#L17-L73
[xml]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/XmlResponseBuilder.kt#L14-L117
[dav-get]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L269-L307
[dav-put]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L312-L351
[dav-delete]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L356-L378
[dav-move]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L408-L468
[path-resolve]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L476-L496
[gui-pin]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/gui.py#L937-L1048
[gui-status]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/gui.py#L1115-L1139
[tray-mount]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/tray.py#L388-L493
[certpin]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/certpin.py#L64-L120
[obscure]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/mounter.py#L191-L218
[directory-html]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L502-L530
[stale-mount]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/mounter.py#L251-L269
[cleanup]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/mounter.py#L436-L441
[tray-wiring]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/tray.py#L95-L119
[unmount]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/mounter.py#L371-L434
[dav-options]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/android/app/src/main/java/com/phonebridge/server/WebDavServer.kt#L235-L264
[cert-dialog]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/gui.py#L590-L648
[manual-restore]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/main.py#L251-L267
[test-cert]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/tests/test_certpin.py#L87-L92
[test-mounter]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/tests/test_mounter.py#L175-L181
[simulator]: https://github.com/ysachin26/PhoneBridge/blob/a378fec40561a4d18be2334f4de92ee02a0e7d0c/desktop/phonebridge/test_server.py#L354-L464
