# P1-005 Windows 受保护配对记录验收

结论：限定存储任务通过。已实现 Windows 当前用户 DPAPI 配对记录和状态/恢复约束，**还没有完整网络配对、Android 配对存储或可日常使用的 NG 客户端**。

## 实现与代码复核

[PhoneBridge.Credentials](../../windows/src/PhoneBridge.Credentials/README.md)加入正式 solution；复用锁定的 BouncyCastle.Cryptography 2.7.0 做 CA 验证，DPAPI 直接使用系统 crypt32，没有新增安全存储包。

- 完整二进制记录一起保护：schema、规范 CA DER/指纹/device_id、client_id、随机 32-byte token、名称、模式/状态和修订号。CurrentUser + UI_FORBIDDEN，不使用 LocalMachine；CA 验证包含真实自签名、算法、用途、critical extensions 和当前有效期。
- 当前用户/SYSTEM 私有 ACL 在创建时提供；固定祖先目录句柄、拒绝 reparse/硬链接/宽松 ACL/异常文件类型，以独占文件锁串行合作进程。密文临时文件 flush 后同目录提交，并重新解密校验；不会先删除旧记录再写入。
- Pending 持久成功前不导出 token；每次凭据读取重载记录。只有 Active 可取挂载凭据；RevocationPending 仅用于 self 撤销，NeedsRepair 不导出。状态和修订号防止迟到激活；新的已验证 session 可刷新 Active 模式。
- 写入结果不确定时实例禁用，需新实例回读磁盘。损坏读取返回 NeedsRepair 错误，不自动覆盖损坏证据；磁盘中显式 NeedsRepair 状态同样禁用。本地存储不执行 HTTP、不证明远端已撤销，也不能擦除先前发给调用方的 token 副本。
- 复核补强输入预检、原生固定缓冲释放/清零、调用方副本职责、测试子进程有界退出及文件占用处理。原生调用不输出日志；公开错误只为固定枚举，元数据/ToString 不包含 token。

这是实现者对 CA、记录解析、文件/原生句柄、状态竞争、异常和材料生命周期的源码复核，不是独立安全审计。最终源码、测试、锁文件和二进制绑定见 [manifest](p1-005/manifest.json)。

## 本机实际验证

项目根目录执行 `./scripts/Verify-Windows.ps1 -ResultsDirectory <本地证据目录>`，实际使用 .NET SDK 10.0.401，CurrentUser DPAPI 与 NTFS 均真实调用；合成测试材料位于忽略目录 .audit/p1-005/run-*。没有连接手机、重新安装驱动、改变账号/策略或发布产物。

| 验证 | 最终结果 | 限定范围 |
| --- | --- | --- |
| locked restore / Release build | 退出 0；0 警告、0 错误 | 正式 Verify-Windows 脚本 |
| Windows solution | 184 通过、0 失败、0 跳过 | 41 发现、33 挂载/进程、50 配对、60 存储 |
| DPAPI 与跨进程 | 通过 | 新进程回读相同合成凭据；匿名 impersonation 拒绝解密，之后恢复身份并退出 |
| 存储/输入拒绝 | 通过 | 非法 CA/指纹、记录结构/状态/名称、密文篡改/超长/错误 entropy、重复/缺失/错误 client |
| 文件边界 | 通过 | 目录/记录/锁的宽松 ACL、硬链接、目录替身、junction 根与祖先被拒绝；祖先不可改名 |
| 持久化与恢复 | 通过 | 5 个创建/替换失败边界、3 个真实进程终止点、100 次模式提交重开、跨进程占用 |
| 文件占用修复 | 通过 | 短暂 reader/锁解除后成功，持续占用失败并保留旧记录，本实例继续禁用 |

[测试明细](p1-005/unit-results.json)、[构建与失败历史](p1-005/build-results.json)、[最终结构/保全检查](p1-005/final-check.json)。测试仅输出合成凭据的散列或固定状态，原始 token 不进入命令行、普通配置、TRX 或可发布证据；密文与公开合成 CA 保留在本地忽略目录。

## 占用失败、定位与修正

首次测试入口因 MSTest 的 public 测试方法/专用断言规则退出 1，尚未执行测试；修正后首轮 54 项通过。复核新增模式刷新等用例后的第一次 solution 回归，原有 124 项通过，新模块 3 项失败，其中两个为 native Open 的 Busy。

诊断只记录异常类型、HResult 和方法名，不回显异常消息或凭据；随后完整并行回归再次复现两项失败，取得 `IOException|80070497|ReplaceFile`。单独 100 次提交测试曾通过，不能把这一次通过当作并发失败已消失。

[ReplaceFile 官方错误定义](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)说明 1175 无法移除被占用旧文件、两文件保留原名；[微软写入建议](https://learn.microsoft.com/en-us/windows/apps/develop/files/best-practices-writing-files)明确列出该占用错误并建议同步/重试。加入确定性持有旧文件 reader 的用例后，实际取得 `0x80070020`（32）。最初只覆盖 1175 的修复因此仍有 1 项失败，证据保留。

最终只对 CreateFile 32/33、ReplaceFile 32/33/1175 最多额外重试 10 次、间隔 25ms；每次替换重新校验两文件，期间一直持有事务锁。1176/1177、ACL/身份错误和其他 I/O 不重试，整个授权事务不重放。确定性占用 3 项以及正式脚本完整 184 项随后通过。没有删旧文件、放宽共享/ACL、延长为无限等待或关闭安全软件；具体占用进程尚未定位，不能归因杀毒软件。

## 未覆盖与使用边界

- 匿名解密拒绝不等同第二个正常 Windows 登录用户；未修改账号来补测。DPAPI 不能防同用户恶意代码/管理员或有效密文回滚，也不承诺绝对不可跨机。
- 真实进程中断与注入写失败不证明断电、磁盘满、介质故障或所有文件系统实现。遗留未引用密文临时文件不会当作记录，自动清理尚未实现。
- 同步存储调用需由宿主放在有界后台工作者；当前没有 UI、忘记/重新配对流程或完整挂载集成。持续文件占用会明确失败，调用方须回读恢复，不能假报授权/撤销成功。
- Android Keystore 配对记录、严格 HTTPS 授权、真实 token 的 rclone 接入、手机批准/撤销/重启、自动重连仍未实现。本轮没有重跑未改动 Kotlin/手机链路；历史源码/产物保全单独核对。22 项产品配对矩阵仍未完成。

下一任务：[P1-006 Android 受保护配对记录](../tasks/completed/P1-006-android-pairing-storage.md)。
