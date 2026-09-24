# P0-003 原始链路实测

日期：2026-09-19。**执行完成，验收失败，Phase 0 未通过。** 本轮没有修改上游产品源码，也没有生成 PhoneBridge NG 正式客户端。

## 环境与身份

| 项目 | 实际值 |
| --- | --- |
| 上游 | `ysachin26/PhoneBridge`，`a378fec40561a4d18be2334f4de92ee02a0e7d0c`；开始/结束均无 tracked 修改 |
| Windows | Windows 11 Pro 25H2，10.0.26200.8875，x64，15.8 GB 内存 |
| JDK | 上游指定 JetBrains 21.0.10+1-b1163.110，官方 SHA-512 校验通过 |
| Gradle / AGP / Kotlin | 9.3.1 / 9.1.1 / 2.2.10 |
| SDK | Command-line Tools 22.0；Platform 34 revision 3；Build Tools 36.0.0；platform-tools 37.0.1 |
| Android | 专用空白 `PhoneBridgeP0` AVD，Android 14/API 34，AOSP x86_64 revision 2，无个人账号/资料 |
| Emulator | 37.1.11；`emulator-check.exe accel` 返回 0，WHPX 可用 |
| rclone | 1.75.1 Windows amd64，官方 SHA-256 校验通过 |
| WinFsp | 2.1.25156，用户明确授权并完成安装；MSI 与已安装 DLL 的 NAVIMATICS LLC 数字签名有效，安装退出码 0 |
| 测试根与网络 | Android `/storage/emulated/0/Download`；ADB 回环 `127.0.0.1:18273` → 模拟器 8273；不是 Wi-Fi 实机连接 |
| TLS/凭据 | 原版生成的临时实验身份；HTTPS 实际握手成功；本实验跳过证书信任校验，不能作为 pinning 验收。密码只在验证进程内读取，未进入命令行/本报告 |

下载来源：[Android 官方工具](https://developer.android.com/studio)、[上游指定 JDK 元数据](https://api.foojay.io/disco/v3.0/ids/dbd05c4936d573642f94cd149e1356c8)、[Gradle 校验列表](https://gradle.org/release-checksums/)、[rclone 1.75.1](https://downloads.rclone.org/v1.75.1/)、[WinFsp 2.1 官方 Release](https://github.com/winfsp/winfsp/releases/tag/v2.1)。工具位于被忽略的 `.audit/tools`，未修改系统 PATH。

下载文件精确字节数和散列保存在 `docs/audit/p0-003/tool-download-checksums.json`；Android SDK 后续组件由官方 sdkmanager 下载，版本列于上表。

上游 Wrapper JAR 的 `76805e32…` 对应 Gradle 官方 9.0/9.1 Wrapper，而不是 9.3.1 的 Wrapper；已核对其来源。本轮使用另行下载并校验的 Gradle 9.3.1 执行同一上游工程，没有替换 Wrapper 或工程文件。

## 构建与审核结果

执行 `assembleDebug lintDebug testDebugUnitTest`：`assembleDebug` 成功产出 APK；`testDebugUnitTest NO-SOURCE`；`lintDebug` 失败。因此组合命令退出码为 1，不能记为全部构建检查通过。

- APK：`.audit/upstream/android/app/build/outputs/apk/debug/app-debug.apk`，9,630,739 字节。
- SHA-256：`fb25f508b0b05a46c250f77724f261fcf5e33b99bc261cab5fc775a2479da4a2`。
- `apksigner verify` 成功；Android Debug 签名，只用于本机实验，不是发布签名。
- Android 单元测试没有源码，不计为通过的测试用例。
- Lint：2 errors、128 warnings。错误为 `themes.xml:12` 在 minSdk 26 资源中使用 API 27 属性；`MainActivity.kt:91` 的广播接收器注册被标记缺少 exported/not-exported flag。此处记录静态检查结果，不推断 API 34 已出现对应崩溃。

审核结合源码与实际协议复现，确认本轮重点问题为 GET Range 处理和 PROPFIND 请求正文处理；没有用忽略 Lint、缩小文件或改换协议来记通过。

## 原始链路结果

| 检查 | 结果 | 证据与边界 |
| --- | --- | --- |
| CHAIN-01 构建/启动 | 部分通过 | APK 安装成功，Android 前台服务启动，HTTPS 握手成功，未认证 OPTIONS 返回 401；Lint 门禁失败 |
| CHAIN-02 mDNS/HTTPS | 未通过完整验收 | HTTPS 成立；Windows 观察模拟器 NAT 30 秒未发现 PhoneBridge。不能据此判定同 Wi-Fi 真机 mDNS 缺陷，也不能算发现通过 |
| CHAIN-03 盘符与目录 | 部分通过 | 实际 P: 挂载及目录读取成功；捕获间歇 400；尚未执行 Explorer 人工视觉验收 |
| CHAIN-04 手机 → PC | **失败** | 非重复区段 100,000,000 字节文件复制后大小相同、哈希不同，从 33,554,432 字节开始不一致 |
| CHAIN-05 PC → 手机 | 有限证据 | 首轮重复内容 100 MB 上传最终两端哈希一致；非重复内容双向完整矩阵因 CHAIN-04 失败而停止 |
| CHAIN-06 退出 | 通过本轮清理 | 第二轮在确认无待上传/错误文件后 `core/quit` 返回，rclone 退出码 0，P: 消失；Android 点击停止，日志确认 Server stopped，mDNS 注销；回环转发移除，模拟器退出 |

Explorer 复制/拖放、真机、1/5/10/20 GB、断网恢复、重启、睡眠、删除、配对与 pinning 均未验证。未连接用户备用手机。

### 确认问题一：Range 与实际数据错误

合成数据以 SHAKE-256 按区块计数器生成，使不同位置内容不同。手机端独立 SHA-256 与原始合成文件一致，而通过 P: 下载的文件不同：

| 对象 | 字节 | SHA-256 |
| --- | --- | --- |
| 原始合成文件及 Android 文件 | 100000000 | `9b3b9896c7296fba874dd1289d94a4689bf3845a5e4cfac63962655985477007` |
| P: 复制到 PC | 100000000 | `cbf4208593974665ed2000bf3ec7c20e8da08107c8f2dd3319ee83d02325bc72` |

首个不同字节恰为 32 MiB；下载文件该位置的前 1,024 字节等于源文件开头。独立 HTTPS 请求 `Range: bytes=67108864-67109887` 得到 **200、Content-Length: 100000000、无 Content-Range**，没有返回所需片段。源码 `WebDavServer.handleGet` 忽略 Range，却声明 `Accept-Ranges: bytes`。这些证据支持该实现与 rclone 分段读取不兼容、导致本次错误数据的判断。[原始差异证据](p0-003/download-failure.json)

HTTP 本身允许服务器忽略 Range；不能把所有 200 响应统称协议违规。本项目必须支持 rclone 分段读取与 Seek，修复应正确处理单区间、后缀/开放区间、206/416 及流关闭，依据 [RFC 9110 Range 语义](https://www.rfc-editor.org/rfc/rfc9110.html#section-14.2)。

### 确认问题二：PROPFIND 后连接失步

同一 HTTPS 连接发送带 XML 正文的 PROPFIND，得到 207；随后发送 OPTIONS，得到 400：`HTTP verb <?xml unhandled`。源码 `handlePropfind` 没有消费正文，残余 XML 被解析成下一请求。真实 rclone 日志也出现相同 400。[协议证据](p0-003/protocol-results.json)

服务器需消费完整请求正文，或明确关闭连接后再处理下一请求；不得无限读取、吞掉后续请求或信任无界长度。依据 [RFC 9112 持久连接要求](https://www.rfc-editor.org/rfc/rfc9112.html#section-9.3)。

### 首轮验证脚本的纠正

首轮使用重复区段内容，掩盖了错误偏移的影响；其哈希一致只能说明该样本，不能证明一般文件正确。首轮还在上传入队前观察到短暂的零队列，从而过早读取手机文件、误报上传哈希不一致。最终手机哈希与 PC 均为 `82b36ce70406166182bf586db89c972859d3eae3ff3ccbc3ba111683efd91f0d`，无 Dirty 缓存；这次上传没有确认的数据损坏。

已将合成数据改为不同区段内容，上传等待改为“手机独立哈希连续一致且上传队列/活动上传归零”。第二轮在下载阶段真实失败，未执行修订后的上传等待完整路径，不能将该路径记为已通过。首轮进程是在独立哈希/缓存核对后终止，第二轮使用 RC 正常退出。[首轮回读](p0-003/first-run-reconciliation.json)、[第二轮结果](p0-003/chain-results.json)

## 复现与留存

可审阅的实验脚本位于 [tests/integration/upstream_chain](../../tests/integration/upstream_chain/README.md)。运行前必须按其中说明准备同名专用空白 AVD 和固定依赖；不能指向真实手机。脚本不构成 NG 产品客户端。

原始日志、依赖元数据、Gradle/Lint 报告、APK、模拟器及合成数据保存在 `.audit/runs/P0-003`、`.audit/upstream/android/app/build`、`.audit/avd`。缓存未删除。可提交文件只包含合成数据摘要和非秘密环境信息；密码、认证 Header、私钥及 keystore 不进入版本控制。

结束回读：P: 不存在，无本任务 rclone/emulator/qemu 进程，无 ADB 设备；上游 tracked 文件无变化。WinFsp 作为已授权开发依赖保留安装。

提交前文档/脚本审核：76 个本地链接无断链，6 个 Python 文件 AST 解析成功。此检查不等于归档脚本再次端到端通过；所有实际行为结论以上述实测记录为准。

## 下一任务

P0-004：保留原始失败基线，建立带来源/许可证的独立修复版，最小修复 WebDAV 分段读取和请求正文处理，补充协议回归与非重复 100 MB 双向挂载实测。Phase 1 仍未获准开始；原版其他安全问题和 Lint 错误仍须后续分别解决。
