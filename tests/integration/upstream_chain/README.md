# 原始链路实验脚本

仅供 Phase 0 隔离实验，不是 NG 客户端。P0-003 原版实际结果为[失败](../../../docs/audit/P0-003-VALIDATION.md)，当前累计修复见 [P0-007](../../../docs/audit/P0-007-VALIDATION.md)，禁止将目录存在视为测试通过。

## 前置环境

从项目根运行；固定 `.audit/upstream` 为 `a378fec40561a4d18be2334f4de92ee02a0e7d0c`；依赖版本与 SHA 见实测报告。需要 `.audit/venv/Scripts/python.exe`（含 zeroconf）、`.audit/tools/android-sdk`、`.audit/tools/rclone-v1.75.1-windows-amd64`、已安装 WinFsp，以及空闲 P:、18273/15579 回环端口。

专用 **PhoneBridgeP0** AVD 必须为空白 Android 14/API 34 x86_64，按 `-port 5556` 启动，ADB 目标固定 emulator-5556。不可把任何个人数据置入该 AVD。APK 必须自行从固定上游构建，路径为 `.audit/upstream/android/app/build/outputs/apk/debug/app-debug.apk`。构建组合检查会因已知 Lint 错误退出 1；不能隐藏错误或当正式 APK 分发。

## 脚本与执行顺序

1. `start_android.py`：首次安装 APK，设置仅用于实验的 Download 共享与权限，经 UI 启动，建立 ADB 回环，检查真实 HTTPS 和未认证 401。安装失败立即停止；不是任意设备安装工具。
2. `chain_test.py`：生成不同区段的 100,000,000 字节数据、挂载 P:、复制和独立 Android 哈希校验。原版预期在下载哈希处失败并退出。若未来修复通过，会打开 Explorer 待人工核对最多 90 秒，再正常卸载；打开窗口本身不计视觉验收。
3. `inspect_failure.py`：分析下载差异。仅适用于已经生成了下载文件的失败运行，不是通用测试。
4. `protocol_probe.py`：独立复现 Range 和持久连接问题，并观察 30 秒 mDNS。需要 Android 服务仍在运行。
5. `stop_android.py`：点击停止共享，保存限定 tag 的服务日志、移除回环转发并关闭专用模拟器，保留缓存和数据。

例如从项目根运行第二步：

```powershell
& '.\.audit\venv\Scripts\python.exe' '.\tests\integration\upstream_chain\chain_test.py'
```

每步检查退出码与 `.audit/runs/P0-003` 日志再决定下一步，不是无条件串行执行所有脚本。重跑链路前须确认 P: 已退出、上一进程状态已核对。首次启动脚本不会强行覆盖已有安装；新版本部署由后续任务单独处理。

P0-004 复验：设置当前进程环境变量 `$env:PHONEBRIDGE_AUDIT_TASK = 'P0-004'` 后，运行同一组脚本。此时 APK 来自 `.audit/p0-004-worktree`、证据写入 `.audit/runs/P0-004`，安装允许更新专用模拟器中的实验 APK。新增 `stream_regression.py` 在真实 Android HTTPS 服务上执行 34 个断言用例；`protocol_probe.py` 是原始行为观察工具，不是修复后的通过断言。

P0-005：设置 `PHONEBRIDGE_AUDIT_TASK=P0-005`，APK/输出切换至对应任务目录。启动后先确认安装 APK 散列与本任务产物相同，再执行存储相关脚本；每项失败即停止。

- `storage_baseline.py` 只在 P0-004 APK 上复现兄弟目录读取和专用旧文件截断，输出放 P0-005；绝不测试原版根 DELETE。
- `storage_regression.py` 在修复 APK 上执行 63 项 HTTPS 检查，包含根修改拒绝、专用临时目录删除/覆盖、上传超时、并发访问及暂存保留。
- `storage_process_death.py` 对专用样本启动短上传，然后强制停止该模拟器的测试应用，重启后核对旧文件和暂存均保留。
- `storage_scope.py` 暂时重命名专用模拟器中全为合成样本的 Download 目录，验证选择不可用时停止共享，再恢复原目录；另测未知选择。任何异常保留样本，不清空目录。
- `stream_regression.py` 复验 34 项数据流，再执行 `chain_test.py` 与 `stop_android.py`。重跑前移走旧 `finish-chain` 标志可恢复 Explorer 观察等待；该标志只缩短成功后的等待，不改变上传完成判断。

Android 单元/原生测试随累计补丁提供，原生测试只用 UUID 私有缓存目录，清理不跟随符号链接。硬链接样本被平台拒绝时标记 SKIP，不计通过。

## P0-006 兼容性复验

设置当前进程 `PHONEBRIDGE_AUDIT_TASK=P0-006` 和 `PHONEBRIDGE_AUDIT_API=26` 或 `34`。API 26 必须使用 PhoneBridgeP0Api26/端口 5558/回环 18275；API 34 沿用 PhoneBridgeP0/5556/18273。两个 AVD 均只允许合成样本，启动完成后才运行脚本。APK 来自 `.audit/p0-006-worktree`，输出为 `.audit/runs/P0-006/api26` 或 `api34`。

依次执行下列步骤，每步成功后再继续：

1. `start_android.py` 安装并核对 APK；API 26 授予旧版存储权限，API 34 使用所有文件访问和通知权限。经实际 UI 启动后检查 HTTPS 401。
2. `compatibility_instrumentation.py` 安装对应测试 APK，保留并检查原有崩溃日志，执行/解析 15 项原生测试；仅允许指定硬链接用例因平台限制跳过。
3. 再运行 `start_android.py`，恢复原生测试停止的共享；随后 `compatibility_runtime.py` 验证三轮启停/后台恢复，以及 1,000,000 字节 PUT/GET/手机端哈希。该样本不替代大文件验收。
4. `storage_scope.py` 验证不存在目录和未知选择拒绝启动，并恢复原选择。API 26 的 mv 无 -T；脚本停应用、检查目标不存在、禁止覆盖并核对目录 inode，恢复失败立即停下保留样本。
5. `stop_android.py` 验证 UI 停止、HTTPS 关闭和无崩溃，再移除对应转发及退出模拟器，输出 cleanup.json。仍需主机回读进程及 ADB 列表确认退出。

本任务不调用固定 API 34 的其他链路脚本来测试 API 26。最终原生测试 APK 已在两版本实测；日志保留入口的最后修订仅在 API 26 实跑，API 34 的原生断言结果不受该日志收集差异影响。详见 [P0-006](../../../docs/audit/P0-006-VALIDATION.md)。

## P0-007 支持策略验证

设置 `PHONEBRIDGE_AUDIT_TASK=P0-007`、`PHONEBRIDGE_AUDIT_API=26` 或 `36`、`PHONEBRIDGE_AUDIT_VARIANT=repair`。API 36 使用 PhoneBridgeP0Api36/端口 **5560**，ADB emulator-5560，回环 18277；API 26 沿用 5558/18275。输出增加 `api<版本>/repair` 子目录。修复 APK 来自 `.audit/p0-007-worktree`，不得把脚本指向真实手机。

按 P0-006 顺序运行启动、原生测试、重新启动共享和运行检查；本任务原生测试为 17 项（仅允许硬链接样本跳过）。API 36 另执行 `foreground_runtime.py`，验证实际服务类型、官方缩短时限、进程重建、手动停止及三种真实重启行为。脚本只改变专用 AVD 的 dataSync 测试时限并恢复，重启前核对停止状态和等待系统状态写盘。最后仍执行 storage_scope 和 stop_android。

`foreground_baseline.py` 仅限 API 36、`PHONEBRIDGE_AUDIT_VARIANT=baseline`；先由 start_android 安装 P0-006，再复现已知进程重建/时限崩溃，保存预期失败并恢复测试开关。不能在同一日志里将基线崩溃计入修复结果。

`PHONEBRIDGE_FOREGROUND_RESUME_BOOT=1` 仅用于本次已确认前四项通过、APK 一致且 finally 已停止服务的恢复入口；校验已有记录，不跳过未知失败。默认完整运行不设置该变量。最终脚本去掉了重启测试后重复的 UI 清理，完整序列未重新执行；本次七项行为断言及独立清理回读结果分别归档，不能把失败入口的退出 1 写成 0。详见 [P0-007](../../../docs/audit/P0-007-VALIDATION.md) 的工具限制。

## P0-008 授权备用机探针

`real_device_probe.py` 与模拟器脚本分开。仅用于本任务已授权的 Redmi K40/alioth、API 36 与散列固定的 P0-007 APK；不自动安装或替换设备，不可直接用于个人手机。先完成正常手机权限提示，选择 Music 并开始共享；脚本会检查实际前台服务、共享选择、自启关闭及目录只含现有缩略图元数据，不满足即停止。

当前进程环境必须提供 `PHONEBRIDGE_TEST_SERIAL`、`PHONEBRIDGE_TEST_LOCAL_IP`（测试 LAN 网卡）和 `PHONEBRIDGE_TEST_PHONE_IP`（手机 wlan0 地址）。从项目根用隔离 Python 执行。mDNS 只监听指定网卡，USB 转发 18279 只取得公共证书，不发送认证或文件；rclone 真正连接手机 LAN IP，并启用 ca-cert。结束后停止该实验应用并移除本次转发，保留 APK 和所有文件。

输出位于 `.audit/runs/P0-008`，含原始局域网地址，不直接对外分发。观察脚本退出 0 只代表记录完成；本次 rclone 退出 1、缺 IP SAN，链路判失败。[实际记录](../../../docs/audit/P0-008-VALIDATION.md)。最终两个输出字段语义已校正，未重复执行探针；运行逻辑与实际测试相同。

## 边界与审核

- 本实验跳过自签证书信任验证，只用于无个人数据的模拟器和回环端点，不能复用于真实手机或正式凭据。
- 临时密码从 app 私有配置进入进程内存，经 stdin 传给 rclone obscure，再经子进程环境传递。没有把密码或 RC 口令写入脚本、参数或日志。
- 失败时保留缓存；只有无待写入且状态明确时关闭挂载，未知写入状态保留供调查。
- P0-003/P0-004 不执行 DELETE 或破坏性覆盖；P0-005 仅在专用空白 AVD 的合成样本中执行明确的覆盖/删除回归，不用于用户目录或真实手机。不同区段样本和独立手机散列是必要条件；队列瞬间为零不证明上传完成。
- 本轮真实运行的是 `.audit/runs/P0-003` 的开发副本；归档版补充同名 AVD 防误操作检查、失败哈希输出及更严格失败清理条件，已做语法/静态审核，尚未再次端到端执行。修订后的成功上传路径也尚未通过。
- 上一条是 P0-003 收口时状态。P0-004 已实际执行当前归档版的启动、34 项协议回归、完整双向链路与停止脚本；同名 AVD 检查及修订后的上传等待路径均已走到，结果见 [P0-004](../../../docs/audit/P0-004-VALIDATION.md)。
# P0-009 TLS 验证入口

本段补充后文旧版文件链路入口；旧 `start_android.py` 等脚本仍只支持其声明的任务，不得将关闭证书校验的基线入口用于 P0-009 正式身份验证。

- `tls_instrumentation.py`：指定当前进程 `PHONEBRIDGE_TEST_SERIAL` 为已登记专用 API 26/36 AVD 或已明确授权的备用机；核对设备、安装当前 P0-009 应用/测试 APK 并核对哈希，仅运行 8 项隔离 Keystore/TLS 测试。临时别名和私有目录结束后清理，不碰默认身份或共享文件。
- `tls_runtime.py`：需相同最终 APK/测试 APK、通知/存储权限、可启动的前台界面。使用显式 Android 测试宿主启动**真实 PhoneBridgeService**，宿主最多 180 秒并在退出前停止服务；PC 每次最多等待 35 秒就绪。验证 root trust、地址、进程重启及故障拒绝，不进行共享文件写入。
- 旧实验 PKCS12 存在时，先验证拒绝共享，再要求当前进程设置 `PHONEBRIDGE_EXPERIMENTAL_TLS_MIGRATION=P0-009` 才删除该单一实验文件。此动作只适用于本项目此前创建、无正式配对的牺牲实验身份，不是产品自动迁移。备份/恢复的是公有 CA；不导出私钥。
- 真机还需 `PHONEBRIDGE_TEST_PHONE_IP`，只允许预检匹配的同 LAN 地址，核对 Music 只有已知元数据文件、没有链接、自启关闭。密码只在内存、stdin 和短命 rclone 子进程环境中使用，不输出或写入配置。正式凭据存储仍未实现。
- 输出 `.audit/runs/P0-009/api26`、`api36`、`phone36`；原生入口必须同时检查测试行和最终 `OK`，运行入口必须退出 0 且所需步骤齐全。中途 JSON 的单步 PASS、cleanup PASS 不能代表整个测试成功。

构建时必须保持原隔离 `JAVA_HOME`、`ANDROID_HOME`、`ANDROID_USER_HOME`、`GRADLE_USER_HOME`，尤其不能遗漏 ANDROID_USER_HOME 导致调试签名变化。最终结果、补丁 SHA 和未验证项见 [P0-009](../../../docs/audit/P0-009-VALIDATION.md)。

真机 ROM 曾拒绝 instrumentation 的后台 MainActivity 启动（结果 102）。`tls_runtime.py` 仅在当前 PID 的对应事件出现时，用标准 `am start` 打开公开主界面一次并保存证据；不修改权限、app-op 或锁屏。最终五次宿主均正常退出；这不代表后台自启已验证。


## P0-010 真机文件链路

`real_device_chain.py` 只针对已授权备用机，使用 P0-009 APK/公有 CA，核对型号/API、应用与测试包散列、身份连续性、Music 元数据白名单、空闲 P: 与 RC 15581。当前进程需 PHONEBRIDGE_TEST_SERIAL、PHONEBRIDGE_TEST_LOCAL_IP、PHONEBRIDGE_TEST_PHONE_IP。所有传输走 LAN，rclone 的实际连接启用 ca-cert。首次创建 UUID 样本目录后，脚本再次执行会因已有样本而停止，不得为重跑而删除既有数据。

`real_device_view.py` 通过 PHONEBRIDGE_CHAIN_RUN 指定已取得复制证据的 run ID，核对样本散列后只读重挂，最多等待 120 秒的实际界面观察。观察文件只能在代理已经看到界面或收到用户对四个项目的明确确认后写入；不得凭目录枚举自行声明视觉通过。

两入口共用 P0-010 的 phone_service_host，在 teardown 前通过标准 ADB 恢复公开测试界面到前台，避免本 ROM 拒绝 AndroidX EmptyActivity 的后台启动。不会修改权限或关闭系统保护。测试只证明前台启动下的文件链路，不证明后台自启。P0-009 的历史脚本与补丁保持不变。

本轮两次入口均曾退出 1；复制、人工可见性、独立清理和收尾修复回归分别提供证据。最终包装的后台退出回归为 0，未重复整轮复制；见 [P0-010](../../../docs/audit/P0-010-VALIDATION.md)。200,000,049 字节手机样本和 Windows 缓存保留。
