# Windows 受保护配对记录

`PairingStore.Open()` 使用当前用户 LocalAppData/PhoneBridge-NG/Pairings-v1；通过 Windows 原生 DPAPI 保护整条记录，固定 UI_FORBIDDEN，不使用 LocalMachine。BouncyCastle.Cryptography 2.7.0 仅用于 CA 严格验证，沿用配对模块的锁定版本与许可。

调用顺序：

1. 宿主完成 PAKE，再以已确认 CA 的散列调用 `ValidatedDeviceIdentity.Validate`。单一规范 DER、RSA 至少 2048 位、SHA256withRSA、自签名、CA/KeyCertSign、已知 critical extensions 和当前有效期都必须通过。
2. 新随机 client_id 使用 32 位小写 hex。`CreatePending` 内部生成 32 字节随机 token，将身份、名称、token、状态、模式和修订号一起保护、写入并回读；成功前不导出 token。同设备已有记录拒绝覆盖。
3. `OpenCredential` 每次重新读取受保护记录；Pending 仅可用于配对提交/状态验证。`CredentialLease.CopyToken` 返回调用方拥有的副本，使用后必须清零并 Dispose lease。普通元数据和 ToString 不含 token。
4. 严格 HTTPS/token session 验证完成后，调用 `ApplyVerifiedSession`，核对响应 device_id/client_id 和期望修订号。首次转换 Pending 为 Active；Active 可按新验证响应更新模式。只有 Active 能申请挂载凭据。
5. `BeginRevocation` 先持久禁用挂载，随后由宿主停止会话并请求 self 撤销。RevocationPending 只允许取撤销凭据，NeedsRepair 不导出；迟到的激活与旧修订号不能恢复挂载。

状态 API 不会发起网络连接，也不能证明调用方已经验证 HTTPS 或完成远端撤销。已取得的内存副本不能远程收回；服务端逐请求校验和宿主会话停止由 Android/Connection 宿主负责。

P1-008 增加 `List()` 的有界完整性校验枚举；任何损坏记录都会报错，不能从列表中悄悄隐藏。`RemoveAfterVerifiedRevocation` 仅在 RevocationPending/client/revision 匹配时删除，调用方必须先经保存 CA 的严格 HTTPS 验证远端撤销及 token 拒绝。`ForgetLocally` 只用于用户明确选择“仅忘记本机”的路径，必须说明远端可能仍有授权；它也要求本地先禁用并停止挂载。两者均通过验证后的独占文件句柄删除精确记录，不按目录批量清理。

## 文件与失败边界

当前用户/SYSTEM 是新建目录与文件的唯一 DACL 项。操作持有各级目录防删除句柄，拒绝 reparse、文件硬链接、目录冒充记录和宽松 ACL；不静默修复既有权限。锁文件串行同机合作进程的事务。缺失、破损、错误 DPAPI 绑定、异常 schema/字段、未知状态、无效 CA 均失败关闭，不回退普通设置或旧密码。

记录是有界二进制格式 PBC1/schema 1，整数大端；CA <=4096 bytes，名称 1..128 Unicode scalar 且 UTF-8 <=256 bytes，无控制/格式字符；单密文 <=65536 bytes。额外 DPAPI entropy 绑定固定用途与 device_id，不是秘密。随机临时文件只写密文，Flush(true) 后同目录 move/replace，提交后再解密校验。写入结果不确定时本实例进入 NeedsRepair；需新实例回读磁盘，再决定下一动作，不能原地盲重试授权。

打开文件只对 32/33，替换只对 32/33/1175 占用错误最多额外重试 10 次、间隔 25ms；始终持有事务锁，替换每次重新检查两个文件，不对 1176/1177 或整个授权事务重试。持续占用仍失败关闭；不修改系统安全策略。同步 I/O 应由宿主放在有界后台工作者，不能直接阻塞 WPF 线程。

异常只输出固定 StoreError，不回显原始系统错误/路径/凭据。崩溃可能留下未引用的密文临时文件，加载不会把它们当记录；本轮不做目录扫描清理。DPAPI/ACL 不能隔离同一用户恶意程序或管理员，也不防其回放旧密文；进程终止测试不证明断电耐久性。

## 复验

项目根目录执行 `./scripts/Verify-Windows.ps1`，锁定 SDK、还原、构建并运行 Windows solution。存储测试使用本机真实 DPAPI 和 NTFS，只有合成 CA/token；文件放在忽略目录 `.audit/p1-005/run-*`，保留用于核对。测试创建/移除自身目录联接与硬链接，启动/终止所属测试宿主，并在隔离子进程中短暂使用匿名 impersonation；不修改账号、系统策略、驱动或手机。

CredentialsFixture 仅为测试项目，通过 stdin/stdout 提供暂停点与固定状态，输出的 token 散列仅来自合成记录。它不进入产品依赖。当前第二个正常 Windows 用户和断电场景尚未实测。
