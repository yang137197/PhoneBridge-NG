# PBNG Pairing v1 与凭据生命周期契约

状态：当前产品已实现本契约的手输短码流程。P1-004 完成两端协议核心，P1-005/006 完成受保护记录，P1-007 完成 Android 窗口、手机批准和 HTTPS 授权，P1-008 将 v3 配对、DPAPI、严格 session 和 rclone 挂载接入 Windows WPF；P1-009/023 完成安全写入和三种模式。本文不宣称完成独立密码学安全审计。

## 1. 用户流程与选择依据

手机用户打开“配对新电脑”，显示一次性 8 位数字；Windows 从发现列表选择手机并输入该码。当前产品界面只实现手输 8 位码。契约预留的 QR 内容为 `PBNG1.<window_id 的 32 位小写 hex>.<8 位数字>`，不是 URL，但尚未提供扫码入口，不能列为现有功能。手机随后显示该次请求的电脑名称，让用户确认；名称只是标签，不能证明品牌、账号或硬件身份。

选择 Bouncy Castle J-PAKE（固定 NIST_3072、SHA-256、三轮显式确认），经短码确认公有设备 CA 后，才允许后续严格 HTTPS 授权。J-PAKE 的机制依据 [RFC 8236](https://www.rfc-editor.org/rfc/rfc8236.html)；它是 Informational RFC，不称为 IETF Standards Track。使用现有库算法，不自行实现群运算、证明或替换算法。应用消息与授权事务是本项目定义的协议，已完成实现测试和逐阶段复核，但没有独立密码学审计。

比较：只用 QR 携带完整 CA/高熵秘密可以安全引导，但手输很长且依赖 PC 摄像头；不作为本项目唯一入口。把 8 位码直接当 Basic 密码无法解决首次身份认证；TOFU 后先发凭据也不接受。SPAKE2/ADB 可作实际 PAKE 应用参考，但本项目不引入 ADB/NDK 到用户流程。选择 J-PAKE 是因为已核对 Java/C# 官方实现，并实际验证互操作，不是声称它普遍优于其他 PAKE。

前提：用户从自己手机的当前配对界面取得秘密短码。看到/控制该屏幕的攻击者、已控制 Windows 用户会话或 Android 应用进程的攻击者不在 PAKE/本地保险库可独立防御的范围。手机确认不能补救已泄露短码被用于冒充另一台服务端的情况；不得宣传为防已控制终端。P2-004 起产品不阻止系统截屏，因此用户截图可能包含短码；应用仍不得把短码写入日志、遥测或剪贴板，普通连接不再要求输入密码。

## 2. 版本与网络边界

- 文件协议仍为 HTTPS/WebDAV。NG 产品广告使用 `_phonebridge._tcp.local`、`version=3`、`protocol=https`、`auth=paired-v1`；原实验 `version=2` 不自动迁移或降级认证。P1-008 已在原解析器上增加严格 v3 支持；v2 只显示为不可连接的实验候选。
- `device_id=pbng-<CA DER SHA-256 小写 hex>` 仅为寻址提示，不能建立信任。所有地址仍过 P1-001 的 IP/端口验证。名称与 TXT 均不可信。
- 只有配对窗口开启时，广告增加 `pairing=jpake1`、`pair_port=<1..65535>`、`pair_window=<32 位小写 hex>`。窗口关闭立即撤销三个字段并关闭监听；不用固定全网通用配对端口。协议版本不兼容时拒绝，不协商弱套件。
- 配对端口是独立、限时的 TCP PAKE 公有消息通道：只允许下述 8 帧，不是 HTTP/WebDAV、文件路由或远程命令入口。它不承载短码、长期 Token、私钥、文件或明文授权请求。其身份真实性直到双方第三轮确认才成立。
- 后续控制和文件接口始终使用 HTTPS，证书链只信任本次确认/既有配对 CA，并校验当前 IP SAN。不启用任意证书接受回调、HTTP fallback、系统根信任导入或 `--no-check-certificate`。
- HTTPS 地址取配对 TCP 实际对端的已验证 IP + 服务端 Hello 中端口，不能接受任意重定向、另一个 URL 或系统代理；IPv6 scope 取本机接口。网络切换中断本次配对，不换 IP 后沿用半完成会话。

## 3. 窗口、资源与短码

手机使用 CSPRNG 均匀生成 `00000000`–`99999999` 的 8 位 ASCII 数字，保留前导零；不用时间戳、设备编号、取模偏差或人选 PIN。8 位是一次性 PAKE 密码，不用于派生长期文件密码。

每窗口由用户在前台明确开启：`window_id` 随机 16 字节、有效期 120 秒（Android elapsedRealtime），最多 5 次接受的连接尝试，同一时刻最多 1 个握手；每次连接即消耗一次预算，断开/错误/超时也计数。单次握手最多 30 秒，每帧读写最多 5 秒，总是受窗口剩余期限约束。达到限额、用户关闭、退出配对页、应用/服务停止或重启均取消窗口。失败不自动重开；新窗口最早间隔 10 秒并生成新码与 ID。

这限制每窗口在线猜测次数；不能阻止 LAN 拒绝服务，攻击者可以消耗预算。不得为可用性移除全局限制或仅按可变化的源 IP 计数。每帧 payload 上限 8192 字节，每会话累计上限 32768 字节，长度在分配前检查。密码学操作放有界单工作者，不在 UI 线程，异常终止即销毁 participant，不在失败对象上再次 validate。

双方确认第三轮后，该码不可再创建新握手；立即关闭监听/撤销配对广告。现有已配对文件会话不因此停止。授权 grant 只保留至原窗口期限，不能靠 HTTP 轮询延长；所有临时码、PAKE 私有值与 grant 都不落盘，退出及时释放可清零缓冲区，不承诺 GC 能立即擦除所有对象副本。

## 4. 固定帧与身份绑定

每帧：ASCII `PBP1`（4 字节）+ type（1 字节）+ payload 长度（4 字节 unsigned big endian）+ payload。顺序严格如下；重复、乱序、错误 magic/type/长度、剩余尾数据均终止，不猜测其他格式。

TCP 收尾：Windows 发完第 7 帧后关闭输出方向，继续读取；Android 必须在帧期限内读到 EOF，确认没有尾数据，随后发送第 8 帧、安装 grant 状态并关闭连接。Windows 完整验证第 8 帧后仍须读到 EOF，再发送 HTTPS 请求，避免状态安装与首次提交竞争。没有额外业务帧、超时后不恢复旧握手。ADB forward 无法代替保留半关闭语义的真实 TCP；它仅属于开发工具限制，不改变此协议。

| 顺序 / type | 方向 | payload（按列出的顺序） |
| --- | --- | --- |
| 1 / 0x01 | Windows → Android | suite=1（1 byte）、window_id（16）、新 client_id（16） |
| 2 / 0x02 | Android → Windows | suite=1（1）、相同 window_id（16）、相同 client_id（16）、新 attempt_id（16）、HTTPS port（u16 BE）、CA DER length（u16 BE，1..4096）、完整单 CA DER |
| 3 / 0x11 | Windows → Android | Round1：gx1（384）、gx2（384）、proof1 V（384）、proof1 r（32）、proof2 V（384）、proof2 r（32），共 1600 bytes |
| 4 / 0x12 | Android → Windows | 相同 Round1 编码 |
| 5 / 0x21 | Windows → Android | Round2：A（384）、proof V（384）、proof r（32），共 800 bytes |
| 6 / 0x22 | Android → Windows | 相同 Round2 编码 |
| 7 / 0x31 | Windows → Android | Round3 MAC（32 bytes signed two's-complement big endian，见下） |
| 8 / 0x32 | Android → Windows | 相同 Round3 编码 |

client_id 由 Windows 每次新配对随机生成 16 字节；不是硬件 ID，不从名称派生。attempt_id 由 Android 每次接受 Hello 随机生成。长度/字段必须匹配固定值，suite 仅 1。窗口 ID 不匹配立即结束，不把任意新 window_id 接纳为新窗口。

`context_hash = SHA256(frame1 || frame2)`，包括完整头与 payload 的实际字节。participant IDs 不另上网传输，而是在两端计算：Windows 为 ASCII `pbng-pair-v1:W:` + context_hash 的小写 64 位 hex；Android 为 `pbng-pair-v1:A:` + 相同 hex。预期对端 ID 必须与角色匹配。CA、端口、窗口、双方新 ID 因此进入库的证明和密钥确认；改变任一 Hello 字节不能悄悄保留旧信任。

群固定为库的 NIST_3072，不接收对端提供的 p/q/g。群元素和证明 V 使用固定 384 字节无符号 BE；r 固定 32 字节无符号 BE，要求 `0 <= r < q`。元素先检查 `0 < value < p`，gx2 与 Round2 A 不得为 1，再调用库的群/子群及零知识证明验证；不能以长度检查代替证明。Windows/Android 都严格执行 create R1 → validate R1 → create R2 → validate R2 → calculate key → create R3 → validate R3。Android 可以先生成 R3，但只在验证 Windows R3 成功后发送。

Bouncy Castle 将 HMAC 字节表示为**有符号** BigInteger：序列化补符号字节至 32，反序列化使用有符号构造；不能在 R3 复用无符号群编码。C# 2.7.0 的库验证使用 BigInteger.Equals；适配器须先用库 CalculateMacTag 得到预期值，以固定 32 字节和 `CryptographicOperations.FixedTimeEquals` 比较，再调用库 validate 推进状态，不把可变长度整数比较作为首个确认检查。Java 1.86 已采用固定宽度比较。此项是源码观察，不宣称全部模幂或 BigInteger 算法都为常数时间。

Windows 在 R3 成功后才解析并确认 CA：DER 大小受限、恰好一个证书、X.509 v3、CA=true、KeyCertSign、RSA >=2048、SHA256withRSA、自签名签名有效、当前有效期满足要求、critical extensions 仅接受 BasicConstraints/KeyUsage；沿用 ADR-014 的设备身份而非地址叶证书。出现既有已配对 Device ID/CA 冲突时停止并提示明确重新配对，不静默覆盖旧记录。所有 HTTPS 库连接仍需实际验证链、签名、有效期与 SAN，不只比较一个字段。

## 5. 从 PAKE 到严格 HTTPS 授权

共同 keying material K 以 384 字节无符号 BE 左补零。`transcript_hash = SHA256(frame1 || ... || frame8)`。使用 [RFC 5869 HKDF-SHA256](https://www.rfc-editor.org/rfc/rfc5869.html)：IKM=K，salt=transcript_hash，info=ASCII `PhoneBridge NG|pairing-grant|v1`，L=32，得到 grant。它与库内部 key-confirmation MAC key 分离，不直接使用 PAKE 大整数作为密码。K 与 grant 不打印；两端互操作探针只输出合成会话的 key hash。

grant 只用于 `Authorization: Bearer <32 bytes 的 canonical base64url 无 padding>`，仅授权当前 attempt 的下表接口，不授权文件、status、其他电脑或任意管理操作。服务端保留 grant 的 SHA-256 与 attempt 状态在内存，并恒定时间比较；明文 grant 只短暂用于客户端请求。过期/重启后失效；不会保存或自动刷新。认证头最大 512 字节，重复 Authorization/未知 scheme 拒绝，认证完成前不解析业务体。

Windows 先生成独立随机 32 字节长期 token，再将 CA、client_id、token 与 `Pending` 状态通过 DPAPI 持久保存成功，随后才提交。保存失败就取消本次授权，不发送 token。短码/grant 不作为长期 token，也不从 PAKE K 导出长期 token。

| HTTPS 操作 | 认证与语义 |
| --- | --- |
| `POST /phonebridge/v1/pairing/<attempt_id>` | 当前 grant；JSON 恰好 client_id、client_name、credential 三字段。前者为当前 32 位小写 hex，credential 为新 token 的 canonical base64url（43 chars），名称 1..128 Unicode scalar、UTF-8 <=256 bytes、无控制/格式字符；总 body <=2048 bytes。拒绝重复/未知键、无效 UTF-8、尾数据与大小溢出。 |
| `GET /phonebridge/v1/pairing/<attempt_id>` | 同一 grant；只返回自己的 PendingApproval/Active/Rejected/Cancelled，不列举其他电脑。每秒最多 1 次轮询，不能延长期限。 |
| `DELETE /phonebridge/v1/pairing/<attempt_id>` | 同一 grant；原子取消尚未激活的请求。与手机批准竞争时，若已 Active 则返回 409，客户端改用自身 token 撤销；不能把取消回执误写成已撤销已激活 token。 |
| `GET /phonebridge/v1/session` | 长期 Basic 凭据；返回 device_id、client_id、当前授权模式及共享就绪状态。Windows 验证字段与待保存记录相符后即可将 Pending 标记 Active；只有 `share_ready=true` 时才允许连接和挂载。 |
| `DELETE /phonebridge/v1/pairings/self` | 长期 Basic 凭据，只撤销自身 client_id；不能以 body/path 参数撤销其他电脑。手机本地 UI 另可撤销任意已配对电脑。 |

所有响应带 `Cache-Control: no-store`，无重定向、不返回 token/grant，正文最多 4096 bytes，只含固定代码和自己的非秘密状态。非法/过期认证统一 401；有效当前 grant 的拒绝/取消为 403/410，字段或顺序冲突为 409，容量限制为 429；未批准为 202，Active 为 200。权限检查不能将未识别 API 路径落入 WebDAV 文件路由。控制路径为保留空间，不能通过 PROPFIND/PUT 操作其下的“文件”。

JSON 使用 `application/json` 与严格 UTF-8；不启用 HTTP 内容压缩。202/200 配对状态响应恰好为 `attempt_id`、`client_id`、`state`，state 取上述四个大小写固定值；session 响应恰好为 `device_id`、`client_id`、`mode`（`readOnly`/`safe`/`readWrite`）、`share_ready`（boolean）。错误响应只有固定 `code`：`unauthorized`、`rejected`、`cancelled`、`conflict`、`capacity`、`invalid_request`、`not_found`、`method_not_allowed`、`share_not_ready` 或 `storage_failure`。格式错误为 400，保留路径不存在为 404，不支持的方法为 405，配对完成但共享未开始时文件路由为 409 `share_not_ready`，持久化失败为 503；仍先完成该路由适用的认证，不以错误正文泄露其他尝试是否存在。self 撤销持久成功返回 204，无正文；以后相同 token 的 session 必须为 401。Windows 校验 JSON 字段/类型/绑定身份，不仅检查 HTTP 200。

POST 提交前 GET 状态返回 409；错误响应仅返回上述 code，不返回配对状态对象。POST 的单次授权绑定当前 attempt_id、client_id、名称原始 UTF-8 字节和 token 验证值。完全相同的重试只返回现有状态；任一字段改变返回 409 并不覆盖。手机 UI 只能批准该确定请求；批准时先成功保存受保护 Active 记录，再承认激活。grant 的单次性指只能产生一个确定的配对记录；同一操作的幂等回读不产生第二个 token/记录。成功后不再接受新 credential，直到窗口期限可回读既定状态；过期后依靠已保存新 token 调用 session 恢复确认。

## 6. 长期认证与两个本地存储

长期认证继续兼容 rclone 的 HTTPS Basic 承载，但使用**每电脑独立的随机 token**：username=`pbng-` + 32 位小写 client_id；password=32 随机 bytes 的 43 字符无 padding base64url。它不是用户账号或手输密码。[RFC 7617](https://www.rfc-editor.org/rfc/rfc7617.html) 的 Basic 不是加密；本项目只允许在已认证 HTTPS 上使用，不在 URL、命令行、配置和诊断记录中出现。

Android 不保存可还原的长期 token，保存验证值：`SHA256(ASCII("PhoneBridge NG|credential|v1") || 00 || ca_sha256_raw32 || client_id_raw16 || token_raw32)`。这依赖 token 的 CSPRNG 256-bit 熵，不能把同一散列规则用于低熵人工密码。查对应 Active 记录后用固定 32 字节恒定时间比较；未知/撤销/错误 token 同样 401。每个请求重新过授权，不能仅在 TCP 建连时判断一次。

| 平台 | 固定存储边界 |
| --- | --- |
| Windows | 当前用户 DPAPI，UI_FORBIDDEN；禁止 LocalMachine。将 schema、CA DER、CA SHA、device_id、client_id、token、授权/待撤销状态放入同一个保护 blob，文件名/明文索引不作为信任来源。LocalAppData 下当前用户/SYSTEM 私有 ACL；单记录 <=64 KiB，严格解析并核对全部绑定字段。写临时密文、flush、原子替换；破损/解密失败进入 NeedsRepair，不继续挂载，不回退明文。只在内存和已限制的 rclone 子进程环境短暂使用凭据。 |
| Android | 保留 ADR-014 的 Keystore 身份/服务器私钥；新增独立 AES-256-GCM Keystore key 保护整个配对记录集（验证值与授权元数据）。每次写使用新随机 12-byte nonce、128-bit tag，AAD 绑定 schema 与本机 CA SHA。文件放 credential-protected noBackupFilesDir；最多 16 个 Active 电脑、整体 <=64 KiB。写临时密文、flush、原子替换，异常文件不自动重建为宽松授权。 |

P1-005 的 Windows 实现入口见 [存储模块](../windows/src/PhoneBridge.Credentials/README.md)。记录还包含名称、授权模式与单调修订号；后者用于合作进程间的比较交换，不是防恶意回滚计数器。Active 的模式刷新也必须来自重新验证的 session；这不放宽服务端权限。存储 API 不执行 HTTP，也不直接停止已取得凭据的会话。

依据：[DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)、[Android Keystore](https://developer.android.com/privacy-and-security/keystore)、[Android backup](https://developer.android.com/identity/data/autobackup)。DPAPI 不隔离同一 Windows 用户中的恶意程序，也不是硬件设备证明；漫游配置等例外不能宣传为绝对不可跨机。Keystore 保护密钥，不是把明文 SharedPreferences 改名为保险库。硬件保护是否可用由实际 KeyInfo 记录，不能凭 API 名称声称 StrongBox。

手机正常后台共享不要求每次使用 key 再弹生物识别；配对数据库只在系统用户解锁后可读，重启首次解锁前不能绕过 credential-protected 存储启动授权共享。禁用配对材料云备份/D2D 迁移，并验证 Android 12+ 厂商行为；仅设置 allowBackup=false 不当作已经验证。Android 重装/清数据、Keystore 丢失、密文损坏和 Windows 换用户/DPAPI 无法解密均停止自动连接，要求明确重新配对。

已存在身份/存储 key 但配对文件丢失，不按“首次安装”自动初始化；双文件/alias 状态不一致必须失败关闭。P0 实验的 SharedPreferences 密码不得导入为正式 token。正式 Android 工程迁移时需独立声明清理/保留旧身份策略，不在本任务改动已验证 APK。

## 7. 恢复、撤销与权限

状态分开：Windows `Pending → Active → RevocationPending/NeedsRepair`；Android 远端撤销路径为 `PendingApproval → Active → Revoked`，手机本地“移除此电脑”则直接删除该 Active/Revoked 记录。Rejected/Cancelled/Expired 不授权文件。短码正确或 CA 正确不等于用户已批准，配对 Active 也不等于存储权限/共享服务就绪。

P2-004 起 Android 可在停止共享时启动只承载配对的前台服务：配对与批准路由可用，session 返回 `share_ready=false`，所有文件/目录/删除路由返回 409 `share_not_ready`。Windows 可据严格 session 保存 Active 记录，但不得自动启动 rclone、创建盘符或设置恢复意图。用户在手机点击“开始共享”后，session 才返回 `share_ready=true`；Windows 用户仍需手动点击“连接”。

- POST 前/后崩溃：Windows 已有受 DPAPI 保护的 Pending。恢复只对该已确认 CA 的严格 HTTPS 查询 session；200 且身份/client_id 相符才激活。401 不自动重新提交、生成第二个 token 或尝试旧上游密码；保留可解释的待处理状态，用户可重试/取消/重新配对。
- 手机批准后回执丢失：同样通过已保存 token 的 session 回读，不能将网络超时判成“未授权”。未批准请求只在内存，期限/重启后清除；已经持久激活的记录不因临时配对窗口关闭而消失。
- 取消发生在批准之前：原子转 Cancelled；之后同一 grant 不可再批准。批准已经提交则以长期 token 撤销；手机不可达时标 RevocationPending 并禁用挂载/重连，不能声称远端已撤销。
- 手机撤销：先在内存阻止该 client 的新请求/写提交，再可靠保存撤销状态；若保存失败，停止共享、禁止本进程自动重启共享，尽可能返回 storage_failure；若为立即停止活动流而关闭请求 socket，则可能没有 HTTP 正文，客户端同样必须保留未确认状态，并提示撤销未完成。不能把内存阻断当成持久撤销：此时旧授权可能仍在磁盘，进程/设备重启后必须以持久记录为准，用户需重新撤销；Windows 保留 RevocationPending，不能显示成功或恢复挂载。成功回执只在持久提交、阻断新请求/写提交并取消该 client 的活动请求/流后返回。已经成功撤销的记录重启后仍须拒绝。已提交操作不回滚，PC 已下载/缓存的数据不能远程抹除。
- P2-004 当前用户移除语义：Windows“移除此手机”先停止所属挂载，再删除本机 DPAPI 配对记录、临时地址和恢复状态，不请求 self 撤销或等待手机；Android“移除此电脑”在授权提交锁内删除该电脑的加密记录并关闭其活动连接，不请求 Windows 确认。单端移除不能保证另一端也删除旧记录，界面不得宣称远端已清除；再次连接必须创建新的 client_id/token 并按新设备配对。`DELETE /phonebridge/v1/pairings/self` 仍用于兼容和配对取消后的保守撤销，不作为当前用户移除按钮的依赖。
- 单电脑撤销不轮换设备 CA/其他电脑 token。新配对总是新 client_id 与独立 token；不支持隐式轮换，需撤销/重新配对。重复 client_id 不覆盖其他 Active 记录。
- 凭据身份不能授权共享根之外的操作。默认仍为 AGENTS 的安全模式；授权记录和服务端模式共同限制请求，不允许 rclone flag 决定服务端权限。P1-009 已实现删除确认与覆盖屏障，P1-023 已提供手机端每电脑三种模式入口。
- 写提交与撤销共用服务端授权检查/并发屏障；上传中撤销不得提交半文件或毁掉旧文件。P1-009 已在新认证路径回归原子上传和撤销屏障；P1-016 至 P1-020 验证限定中断与大文件缓存恢复。缓存仍按设备身份隔离，不能因换身份自动上传给新设备。

## 8. 验收门槛

合成编码/KDF向量及实施清单在 [pairing contract](../tests/contracts/pairing/contract.json)，探针在 [interop](../tests/contracts/pairing/interop/README.md)。源码/证据见 [P1-003 验收](audit/P1-003-VALIDATION.md)。

必须覆盖：两端正确/错误/前导零短码；篡改任一身份 Hello、群参数/元素/证明、确认消息、反射和跨窗口重放；逐字节分片、超长/重复/乱序/截断；5 次全局预算、120 秒过期和重启；无确认时零长期凭据发送；严格真实 TLS/地址/CA 拒绝；Pending 持久化失败、丢回执与批准/取消竞争；多个电脑隔离、撤销中的流和原子写；DPAPI/Keystore 回读、损坏/备份恢复、日志/异常/命令行秘密扫描；实际 rclone 使用新凭据挂载。

阶段记录（P1-004）：已验证完整 8 帧纯核心、拒绝路径与两端 grant 一致，见 [验收](audit/P1-004-VALIDATION.md)；该阶段尚未运行手机配对、真实 wire 网络协议、DPAPI/Keystore、撤销写屏障或新认证的 rclone。

阶段记录（P1-005/006）：Windows DPAPI 与 Android Keystore/本地撤销提交屏障完成限定验证，API 26/36 各 26 原生、3 进程恢复通过，见 [Android 存储验收](audit/P1-006-VALIDATION.md)。Android 不持久保存 PendingApproval；未批准状态由限时服务仅在内存管理。

阶段记录（P1-007）：Android NG 的真实 TCP/HTTPS/手机按钮授权集成在 API 26/36 各 42 项原生与 C# 联动中通过，含三次独立应用进程和存储故障停服务；详见 [授权验收](audit/P1-007-VALIDATION.md)。当时 Windows 正式界面、rclone新 token 挂载和安全写入尚未完成。

当前产品证据：P1-008 在 Redmi K40/API 36 完成 v3 mDNS、8 位码、手机批准、DPAPI、严格 session、新 token rclone/WinFsp 挂载、已保存配对重连与清理；P1-009 完成安全模式写入/删除确认；P1-010/011 完成 Redmi与 Samsung 的锁屏和同身份恢复；P1-023 完成 Samsung 三种模式。最终 Windows 回归为 280/280。证据分别见 [P1-008](audit/P1-008-VALIDATION.md)、[P1-009](audit/P1-009-VALIDATION.md)、[P1-010](audit/P1-010-VALIDATION.md)、[P1-011](audit/P1-011-VALIDATION.md)和 [P1-023](audit/P1-023-VALIDATION.md)。

仍未完成独立密码学安全审计；API 27/28、更多 OEM 的配对存储/备份迁移以及真实断电下的原子持久性没有产品证据。当前本地预览的限定验收不能扩大为公开发行安全认证。
