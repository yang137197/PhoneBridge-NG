# P0-004 WebDAV 数据流正确性验证

2026-09-19。**本次最小修复通过已执行的协议与隔离挂载验证；Phase 0 整体验收、正式 NG 产品和“可正常使用”目标尚未完成。**

## 改动与可追溯性

基于 `ysachin26/PhoneBridge@a378fec40561a4d18be2334f4de92ee02a0e7d0c`，在独立 `.audit/p0-004-worktree` 修复，原版基线保持干净。最终改动见[补丁说明](../../android/patches/README.md)，固定产物见 [manifest](p0-004/manifest.json)。

| 产物 | SHA-256 |
| --- | --- |
| P0-004 patch | `63ceba89b8637dbaf298563d9106b423f1d74a05aa4344ff2eac55d9a64c3ff8` |
| 本机实验 Debug APK | `f666935cc788ba347400ff61e3e0b6266408bdcb440fabc75aaf6058ead835ea` |

补丁共 5 个 Kotlin 文件，210 行新增、10 行删除；APK 9,635,123 字节。新增 Range 解析、精确正文消费及 12 个单元测试；WebDavServer 调用上述逻辑。未改构建依赖/UI/Android 版本策略；未将上游整个 UI 或远程访问功能移入正式产品。完整 GPL 文本和原许可证/来源声明已保存，见 [NOTICE](../../NOTICE.md)。

设计依据：[RFC 9110 Range](https://www.rfc-editor.org/rfc/rfc9110.html#section-14.2)、[RFC 9112 持久连接](https://www.rfc-editor.org/rfc/rfc9112.html#section-9.3)。单 Range、后缀/开放区间支持 206/416；不支持的多区间与未知单位忽略 Range 返回 200；缺少强验证器时 If-Range 返回完整内容。非 PUT 正文最多 1 MiB，读取预算 5 秒并受现有单次 socket 5 秒超时约束。未知传输编码/无效长度拒绝并关闭连接。

## 实际验证

沿用 P0-003 官方工具、Windows 11、WinFsp 2.1.25156、rclone 1.75.1、空白 Android 14 AVD。只通过 ADB 回环连接，不是同 Wi-Fi 真机；TLS 信任跳过仅用于隔离实验。

| 检查 | 实际结果 |
| --- | --- |
| `assembleDebug testDebugUnitTest` | 退出 0；12 tests、0 failures、0 errors；[单元结果](p0-004/unit-results.json) |
| 实际 APK 安装/HTTPS | 安装成功；实际握手，未认证 OPTIONS=401；独立实验证书身份沿用本机 AVD |
| Range/请求正文协议回归 | **34/34 通过**；[逐项结果](p0-004/stream-regression.json) |
| P: 挂载/文件读取 | 通过，实测盘符及目录；[链路结果](p0-004/chain-results.json) |
| 100,000,000 字节手机 → PC | 非重复区段数据；手机独立哈希与 PC 一致，见下表 |
| 100,000,000 字节 PC → 手机 | 经 P: 写入，等手机独立哈希稳定一致及上传状态完成后判定通过 |
| 文本/创建目录/重命名 | UTF-8 文本复制哈希一致；创建/重命名后 Android 独立确认目录存在 |
| 资源管理器 | 确认真实 `file:///P:/` 窗口及文件夹视图，系统接口读取到 7 项；人工打开文本未收到确认，复制/拖放视觉操作未验证 |
| 待上传与退出 | 待上传=0、上传中=0、错误缓存=0；`core/quit` 后 rclone 退出 0，P: 不存在；Android UI 停止共享后关闭模拟器、移除 ADB 转发；缓存保留 |
| `lintDebug` | 退出 1；仍为原版同样的 2 errors、128 warnings；逐条 ID/严重度/消息比较差异为 0，**没有声称 Lint 通过** |
| 补丁完整性 | 在未修改基线上 `git apply --check` 成功；修复副本 reverse-check 成功；`git diff --check` 成功 |

协议覆盖：首/末/中间/后缀/开放 Range，超出/反向/零后缀/无效/超长十进制范围，空文件，gzip 不改变偏移，If-Range 回退，HEAD；同一连接三次 PROPFIND → GET；真实 pipelined 请求不吞下一请求；超限/负值/溢出 Content-Length；未知 Transfer-Encoding；无长度 PUT；未认证正文关闭；短正文等待约 5.047 秒后返回 408；多缓冲区正文。20 GB 偏移仅有纯解析单元测试，**不是 20 GB 文件实测**。

| 文件方向 | 字节 | 两端相同 SHA-256 |
| --- | --- | --- |
| Android → PC | 100000000 | `9b3b9896c7296fba874dd1289d94a4689bf3845a5e4cfac63962655985477007` |
| PC → Android | 100000000 | `949427b22e9c22df6500cf65c951dd8ba9c89eb752a21c5bbb675955207ac58c` |
| Android → PC 文本 | 50 | `d3d60f0a294eeee652a96efdef1f8b5650077c18dc80730167ba401eb3d594cf` |

下载使用与 P0-003 失败时相同的非重复样本和 32 MiB rclone chunk，没有通过重复数据或降低 chunk 掩盖问题。本轮 rclone 日志未再出现原有 XML/HTTP 400；仍有一条既有 `symlinks not supported without the --links flag` 消息，测试不宣称链接功能已支持或日志无错误。

## 完成前代码审核

核对了最终补丁：整数溢出与空文件边界；同一打开文件句柄的长度/定位；成功/失败分支流关闭；固定响应长度及 gzip；正文消费不超出声明长度；拒绝路径真正关闭连接；认证失败路径不会把正文留给下一请求。各项分别有单元或真实 HTTPS 对应证据，未发现本补丁剩余的阻断问题。

实验脚本使用明确任务号选择 APK/输出目录，强制同名专用 AVD。密码只在内存、stdin 和受管子进程环境中使用；未写入命令行、报告或普通配置。链路失败时不清除缓存，只有写入状态明确才关闭；本轮成功走过此前未实测的上传等待和清理路径。

上述审核限于本补丁，不是全库安全验收。已有风险仍包括：路径越界/根删除、PUT 原文件直接截断及短正文成功、长期凭据存储、真正 TLS pinning、默认安全模式/删除确认、生命周期和发现；不得在个人数据上使用。

## 未完成与下一项

同 Wi-Fi mDNS、真机、Explorer 人工完整操作、1/5/10/20 GB、断网/重启/睡眠、正式配对和全量 MVP 均未通过验收。Phase 1 尚未开始。

下一任务 P0-005：最小修复共享路径边界和上传提交完整性，验证越界/根操作被拒绝、上传中断不破坏旧文件，再继续 Phase 0。

原始构建、Lint、Android、rclone、协议/复制日志保存在 `.audit/runs/P0-004`；工具回读的 Explorer 实际视图曾列出 7 项，随后[清理后窗口状态](p0-004/explorer-shell-view.json)记录 P: 已不存在，两者不混用。原版失败证据保留在 P0-003。
