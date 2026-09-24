# P0-005 共享路径与上传提交验证

2026-09-19。**本任务的路径与上传修复通过限定验证；Phase 0、正式 NG 产品及“正常使用”目标尚未完成。** 只运行于 PhoneBridgeP0 空白 Android 14 模拟器，所有文件均为合成样本。

## 原问题与修复

P0-004 APK 实际复现了三项问题：[原状态证据](p0-005/unsafe-baseline.json)。`../Download-sibling` 和双重编码路径返回范围外哨兵；非法范围外路径返回根列表；声明 65,536 字节、只发送 100 字节的覆盖上传返回 500，原来 32,768 字节文件已被截成 100 字节。没有对未修复版执行根 DELETE。

本次在独立 `.audit/p0-005-worktree` 修复，原始 `.audit/upstream` 保持干净：

- 请求路径不二次解码；拒绝点段、反斜杠、控制字符、空段和保留暂存名称。Destination 独立解码一次并检查 authority/语法；解析失败不回退到根。
- 共享根及每层目录由打开的描述符约束，使用 Android 公开 Os/ParcelFileDescriptor API、NOFOLLOW 和句柄类型检查；通过 `/proc/self/fd` 定位持有的父目录。读、列表、写、复制、移动、删除统一走该边界，拒绝链接/特殊文件；不修改共享根。
- PUT 排他创建同目录随机暂存文件，精确读取声明长度、同步数据、检查目标及暂存 inode 后原子重命名。短正文/断开/失败不截断旧目标；只清理确认属于本次操作的暂存 inode。历史/状态不明暂存数据隐藏并保留，阻止递归删除其父目录。
- 文件 COPY 同样暂存提交；目录 COPY 完成后整体重命名。已有目录的替换明确拒绝，失败的目录暂存保留。MOVE/COPY 支持 Overwrite F，拒绝源/目标树重叠。
- 缺失或未知共享选择停止共享；不会扩成整个存储。实测失败路径曾触发前台服务启动超时，按[官方说明](https://developer.android.com/develop/background-work/services/fgs/troubleshooting#internal-exception)提前进入前台，再执行存储/TLS 工作并在失败时停止。
- PROPFIND/HTML 使用受约束的元数据，转义名称；日期格式器不跨线程共享。普通隐藏文件可列出，暂存文件不可通过协议访问。

本机操作在服务内串行化。公开接口依据：[Os](https://developer.android.com/reference/android/system/Os)、[ParcelFileDescriptor](https://developer.android.com/reference/android/os/ParcelFileDescriptor)；实际构建还核对了 Android 34 SDK 的签名和 API 版本表。O_CLOEXEC 仅在 API 27+ 访问，**API 26 实际运行未验证**。

## 固定产物

补丁是针对原始提交 `a378fec40561a4d18be2334f4de92ee02a0e7d0c` 的**累计补丁，已包含 P0-004，不能叠加应用**。共 12 个 Kotlin 文件，包含前一任务测试；构建依赖/minSdk/targetSdk 未改。见[补丁说明](../../android/patches/README.md)、[manifest](p0-005/manifest.json)及[逐文件散列](p0-005/source-hashes.json)。

| 产物 | SHA-256 |
| --- | --- |
| P0-005 patch | `fb5944b53f5ac95a2078560e31dc44c413b2560c2d283061f4f9bf3a3d81195f` |
| 本机实验 Debug APK | `ca72222d3e4011085526b4ff33055e5291ac8236f91e2afa1cd2838a8487f80e` |

未发布安装包、未连接用户备用手机。来源/许可见 [NOTICE](../../NOTICE.md)。

## 实际验证

| 检查 | 结果与证据 |
| --- | --- |
| assembleDebug、assembleDebugAndroidTest、testDebugUnitTest | 构建步骤完成；[20 个单元测试通过](p0-005/unit-results.json) |
| Android 原生文件操作 | [11 通过、1 跳过](p0-005/instrumentation-results.json)；包括祖先/目标/暂存符号链接替换、短正文、根保护、嵌套复制/移动/删除、流关闭 |
| 真实 HTTPS 路径/上传 | [63/63 通过](p0-005/storage-regression.json)；8 种方法越界、根写操作、Destination、中文/百分号/加号、断开/超时、并发读取、保留暂存、覆盖及正常文件操作 |
| P0-004 数据流回归 | [34/34 通过](p0-005/stream-regression.json)，Range、持久连接和精确正文边界仍正确 |
| 上传中强制终止 Android 应用 | [通过](p0-005/process-death.json)；原文件 32,768 字节、重启后哈希不变；100 字节暂存保留且不发布 |
| 缺失/未知共享选择 | [2/2 通过](p0-005/storage-scope.json)；超过前台启动超时后仍显示停止状态、服务不可访问，无全盘回退 |
| Windows P: 实际挂载 | [双向 100 MB 哈希一致](p0-005/chain-results.json)，UTF-8 文本一致，创建/重命名成功 |
| Explorer | [实际窗口及文件夹视图](p0-005/explorer-shell-view.json)读取到 15 项；人工打开文本/拖放没有确认，仍未验证 |
| 完成与清理 | 上传中/队列/错误缓存均为 0；rclone 正常退出 0，P: 已卸载；UI 停止共享、ADB 转发移除、模拟器关闭，数据与缓存保留 |
| Lint | **仍失败**，2 errors、128 warnings；[与原版逐条比较](p0-005/lint-comparison.json)新增/移除均为 0。组合构建命令最终退出 1 来自 Lint，不能当作全部构建检查通过 |
| 最终审核 | [apply 正反检查、源码/补丁匹配、文档链接与脚本语法检查](p0-005/final-check.json) |

复用同一 Windows 11、WinFsp 2.1.25156、rclone 1.75.1，ADB 回环 HTTPS。证书信任跳过只用于本隔离实验，不是正式 TLS 验收。

| 方向 | 字节 | 两端一致 SHA-256 |
| --- | --- | --- |
| Android → PC | 100000000 | `9b3b9896c7296fba874dd1289d94a4689bf3845a5e4cfac63962655985477007` |
| PC → Android | 100000000 | `949427b22e9c22df6500cf65c951dd8ba9c89eb752a21c5bbb675955207ac58c` |
| Android → PC 文本 | 50 | `d3d60f0a294eeee652a96efdef1f8b5650077c18dc80730167ba401eb3d594cf` |

没有新增最终 Android 崩溃；rclone 仍有既有根路径 `symlinks not supported without the --links flag` 日志，不宣称日志全无错误。

## 审核结论与限制

逐项核对了：解码次数、根和重叠树拒绝、打开后检查类型、描述符和响应流关闭、所有变更入口使用同一存储边界、精确正文、错误关闭连接、暂存所有权、提交点、异常保留数据，以及共享目录不可用时的服务退出。未发现本任务范围内仍阻断已验证链路的问题。

硬链接测试在创建样本时被 Android SELinux 拒绝（EACCES，日志有 `denied { link }`），因此明确跳过，**运行时硬链接检查未验证**。外部存储对含 `< > "` 的文件名返回 EPERM；该命名样本未算协议通过，转义另有纯单元测试，真实外部存储验证使用其支持的名称。首次编译/API、上述样本和失败启动记录保留于 `.audit/runs/P0-005`，最终结果与早期记录分开。

持有目录句柄解决远端路径/符号链接逃逸，不提供对其他本地应用的完整事务隔离：外部应用并发改写、最后检查与重命名之间的竞争、设备断电/介质满未完成验收。提交前进程终止已测，不等于断电持久性已测。串行存储操作、大目录及更大文件的性能尚未验证。目录递归操作异常可能部分完成，不宣称目录删除具有事务性。

安全模式删除确认、长期凭据保护、真正 TLS 身份绑定、真实同 Wi-Fi/mDNS、1/5/10/20 GB 和完整 MVP 仍待实现/验证。本任务收口时的下一任务是 [P0-006 Android 兼容性 Lint 阻断](../tasks/completed/P0-006-android-compatibility.md)，不启动 Phase 1。
