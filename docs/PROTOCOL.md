# 协议观察与 NG 要求

本文区分上游固定快照、早期阶段记录和当前 NG 产品协议。当前 `org.phonebridge.ng` 与 Windows 客户端已实现 `version=3`、`auth=paired-v1`、一次性 J-PAKE 配对、严格 HTTPS、每电脑凭据及三种访问模式；限定证据见 P1-007、P1-008、P1-009 和 P1-023。本文不保证兼容未来上游版本，也不代表完成独立密码学安全审计。上游快照固定为 [P0-001 审计](UPSTREAM_AUDIT.md) 的 `a378fec40561a4d18be2334f4de92ee02a0e7d0c`。

## 1. 上游协议快照

| 项目 | 当前源码行为 |
| --- | --- |
| 发现 | Android NsdManager `_phonebridge._tcp.`；Windows zeroconf `_phonebridge._tcp.local.` |
| 端口 | 默认 TCP 8273；连接应使用发现到的端口，不能以端口号确认身份 |
| TXT | version=`2`、deviceName、model、brand、sdk、auth_required、auth_user、protocol；可选 tailscale_ip |
| 身份 | 设备标识从服务名或手动连接 IP/端口派生；不是持久公钥身份 |
| 认证 | HTTP Basic，固定用户名 `phonebridge`，手机生成并保存 8 字符长期密码 |
| HTTPS | 自签证书，客户端存在跳过检查/失败放行；服务端初始化失败退回 HTTP |
| 状态 | 认证后的 GET `/phonebridge/status`；含设备名称、版本、存储/请求计数、指纹及可选 Tailscale 地址 |
| 文件路由 | 以共享根为 `/`；支持 OPTIONS、PROPFIND、GET、HEAD、PUT、DELETE、MKCOL、MOVE、COPY 的部分语义 |

`version=2` 是上游 ServerConfig 字段，不等同于 APK 版本 1.1.0，也不是本项目已冻结的协议版本。旧状态接口返回的指纹未经可信通道验证时不能作为信任来源。

## 2. NG 发现与身份要求

保留 mDNS 作为发现机制，使用手动 IP 排障时走同样的身份校验。解析端必须限制 TXT 长度/格式、验证地址/端口，处理多个网卡和 IPv4/IPv6；未知协议或不支持版本给出明确错误，不能回退到明文。

设备持久 ID 与可变名称/IP 分离。[PAIRING](PAIRING.md) 定义并由当前产品实现 `version=3`、`auth=paired-v1` 与限时配对字段；不兼容旧密码认证、不自动降级。Android 发布 v3；Windows 严格解析 v3，并只把上游 `version=2` 显示为不可连接的实验候选。TXT 不广播密码、Token、配对秘密或私钥。

## 3. 配对与认证顺序

当前产品顺序：发现候选设备 → 用户完成一次性配对身份确认 → 安全保存信任/凭据 → 后续 TLS 连接校验绑定身份 → 认证 → 文件操作。

Device ID、公有 CA、证书指纹与名称是绑定数据。ADR-017 与 [PAIRING](PAIRING.md) 定义的一次性 8 位码/J-PAKE、固定帧及期限、CA 绑定、严格 HTTPS 授权、每电脑随机 token、存储/恢复/撤销已接入 Android 与 Windows 产品。8 位码只用于 PAKE，不作为 Basic 密码；后续 Basic 承载的是每电脑随机 256-bit token，且只允许用于已认证 HTTPS。

所有 HTTP 请求与 rclone 数据传输使用同一身份绑定策略。对 TLS 的一次先行探测不保护后续独立连接，因此 Windows 为控制请求和 rclone 分别配置已确认设备 CA并校验当前 IP SAN。P1-008 真机已验证正式配对、DPAPI记录、严格 session 与 rclone/WinFsp 挂载；P1-010/015 验证活动恢复与手动地址仍走相同身份校验。

## 4. 文件方法最低要求

下表是 NG 当前最低协议要求；上游快照未全部实现。当前 Android 服务实现 OPTIONS、PROPFIND、GET/HEAD、PUT、MKCOL、MOVE、COPY 和按模式受限的 DELETE，只声明 DAV Class 1，不提供锁服务。具体状态码和响应头按 [HTTP 语义](https://www.rfc-editor.org/rfc/rfc9110.html)及 [WebDAV](https://www.rfc-editor.org/rfc/rfc4918.html)核对。

| 方法 | NG 要求 | 审计关注 |
| --- | --- | --- |
| OPTIONS | 只声明实际支持的能力；按认证策略响应 | 上游声明 DAV 1,2 但没有锁实现 |
| PROPFIND | 正确的目录/文件元数据、UTF-8 路径和 Depth 语义；只读取元数据 | 深度/大目录行为、隐藏项及日期并发格式化 |
| GET / HEAD | HEAD 元数据与 GET 一致；文件流式读取；正确处理支持的 Range、部分响应及越界范围 | 上游忽略 Range，不能依赖 Accept-Ranges 宣称 |
| PUT | 严格检查请求完整性、空间与模式；安全提交；不支持的传输形式明确失败 | 不能直接截断旧文件、短写后报成功或把无长度当空上传 |
| MKCOL | 权限检查、路径边界与正确目录创建/冲突反馈 | 不扩大共享根 |
| MOVE | 同时检查源/目标、模式、覆盖条件；重命名不破坏内容 | 不能借覆盖绕过删除授权 |
| COPY | 若保留/声明则完整处理覆盖、深度、部分失败 | 非必要方法可明确不支持，不能无条件覆盖且报成功 |
| DELETE | 根据三模式与有效确认执行；永久根目录/越界操作拒绝 | 不能依赖 rclone/Explorer UI 自己保护 |

仅实现 MVP 需要的方法，不为追求“完整 WebDAV”加入远端在线编辑或无需求的锁服务。若真实 rclone 版本依赖额外方法，先记录调用证据并更新此表。

路径编码只在明确层次解码一次，区分加号、空格、百分号、分隔符；多次编码及规范化后越界必须拒绝。错误响应不回显秘密或用户文件内容，HTML/XML/JSON 根据格式正确转义。

## 5. 删除确认与重试边界

安全模式下普通 WebDAV DELETE 返回拒绝；PUT、COPY 和 MOVE 只允许目标不存在的创建或重命名，避免以覆盖绕过删除确认。完全读写模式仍执行路径和完整性检查，但可使用 WebDAV DELETE 与覆盖语义。

删除控制接口为 `POST /phonebridge/v1/deletions` 和 `DELETE /phonebridge/v1/deletions/{confirmation_id}`。POST 仅接收一个规范化共享内绝对路径，服务端返回目标类型、大小、修改时间、30 秒期限及 128-bit 随机 ID；它不修改文件。Windows 必须先显示目标再调用 DELETE。ID 绑定已认证 client_id、路径和完整目录树快照，内存中最多 16 个；执行尝试先消费，过期、重放、其他客户端、目标变化、模式变化或撤销均拒绝。服务停止会清空全部 ID。

控制接口的 DELETE 成功返回 204；未知或已消费 ID 返回 404，过期/目标变化返回 409，权限变化返回 403。超时仍不代表操作未执行，客户端必须重新查询目标结果，不能自动重试同一确认 ID。上传、重命名和删除重试均先验证当前目标，避免覆盖新的用户修改。

## 6. 协议证据

协议测试保存脱敏请求类型/响应状态、版本、精确测试文件信息与最终哈希；不保存 Authorization、密钥、码内容或真实文件数据。必须分别覆盖成功、拒绝、超时、中断以及请求重放；结果填写 [VALIDATION_TEMPLATE](tasks/VALIDATION_TEMPLATE.md)。

## 7. P0-009 实验身份语义（历史阶段）

认证后的 status 新增 `identity_certificate_sha256`（设备身份 CA 的 DER SHA-256，小写 64 位十六进制）和 `device_id`（`pbng-` 加该散列）。身份指纹与动态地址叶证书指纹不同；旧 `cert_fingerprint` 保留为 null，避免旧客户端误用新语义。`version=2` 仍是上游字段，本次不据此宣称配对协议兼容。

首次实验通过已授权 USB 通道取得**公有**身份 CA；正式版本必须由 D-03 配对确认并安全保存。mDNS、status 字段和 TLS 中送来的 CA 都不能自行建立信任。每次 rclone 连接仅载入该设备已确认的 CA PEM，通过证书链与 IP SAN 双重验证后才允许 HTTP 认证；不关闭证书验证。地址叶证书更新保持身份 CA、Device ID 不变；CA 改变必须阻断并重新配对。

旧 PKCS12、私钥或公有身份文件部分丢失/损坏均停止共享，不降级到 HTTP、不静默创建替代身份。首次全新安装才允许生成新身份。本节只记录 P0-009 实验实现；当前正式配对与受保护凭据状态见第 10 节。

## 8. P1-001 Windows 候选解析边界（历史阶段）

P1-001 当时兼容实验 APK 的 `_phonebridge._tcp.local` 与 TXT `version=2`、`protocol=https`，尚未定义产品配对。TXT 最多 32 条、每条 255 字节、总计 4096 字节，重复键、控制字符、格式字符和无等号条目拒绝。P1-008 在同一解析器上增加严格 v3 字段；v2 仍只作不可连接的实验候选。候选保留名称与规范化 IP/端口，未知字段不用于建立信任；手动输入仅接受 IP 与端口并使用同一校验器。

Windows 的候选 ID 由系统服务实例 ID 派生，仅用于列表更新/删除；与已配对设备 ID 不同。IPv6 link-local 必须有明确 scope。当前每 12 秒重新枚举，连续两轮无有效广告撤销候选；候选存在不代表共享仍可连接。产品身份、认证和挂载遵守第 3 节与 ADR-014。

## 9. P1-002 只读会话输入（历史阶段）

C# 挂载不接受 DeviceCandidate 直接建立信任。P1-002 当时使用授权 USB 提供公有 CA 与临时认证，证明固定 rclone 对每条 HTTPS 连接执行 ca-cert 链和地址校验；RC PID、真实远端列表和盘符存在后才报告 Mounted。P1-008 后续以 ADR-017 产品配对和 DPAPI Active 记录替代实验输入；挂载凭据只来自严格 session 已验证的受保护记录。

## 10. 当前产品协议状态

- Android `org.phonebridge.ng` 发布 v3 广告，限时配对窗口只交换固定 8 帧 J-PAKE 公有材料；手机明确批准后才持久保存授权。
- Windows WPF 通过 PAKE 确认设备 CA，先用 DPAPI保存 Pending，再经严格 HTTPS 提交和 session 核对变为 Active；已保存配对后不再需要短码。
- 配对 grant 使用 Bearer；长期文件与控制请求使用每电脑随机 token 的 Basic 承载。两者均只用于严格 HTTPS，不写入普通配置、URL、日志或命令行。
- Android 服务端按每电脑模式强制只读、安全或完全读写；Windows只显示并遵循手机确认的模式。安全模式删除使用第 5 节的一次性确认接口。
- Redmi K40 与 Samsung SM-S9180/API 36 已完成真实 LAN 配对、严格 session、rclone/WinFsp 挂载及限定文件操作。当前 Windows 回归为 280/280，Android最终构建与签名状态见本地交付清单。

边界：上游 v2 不迁移为产品凭据；二维码格式只在配对契约中保留，当前 UI仅支持手输 8 位码；没有进行独立密码学安全审计或公开发行安全认证。
