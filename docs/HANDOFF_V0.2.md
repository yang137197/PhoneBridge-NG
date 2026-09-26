# PhoneBridge NG v0.2.0 新对话交接

更新时间：2026-09-26。适用分支：`main`。

## 1. 仓库与版本状态

- GitHub：<https://github.com/yang137197/PhoneBridge-NG>
- 本地目录：`C:\Users\yang1\Documents\ChatGPT\samsung link windows\PhoneBridge-NG`
- 已发布版本：`v0.1.0`，标签提交 `e74e3a2403d3485883e24a723826c786973eb29e`。
- v0.2.0 需求计划基线：`18b5e0778d4fd03fad95a0a4602cea36dcc83f28`。
- P2-002 静态设计 r1 已由用户全部确认并冻结；视觉方向、Windows 分层/按钮命名、Android 页面层级和图标 A“桥接文件”均已确定。
- P2-003 r2 的实际 UI 已由用户复验通过并完成。
- P2-004 已完成全部既定体验；P2-005 也已完成：r20 在 Samsung 与 Redmi 上同时挂载两个盘符、各自双向小文件复制及单设备断开隔离均通过。
- r10 暴露并确认 Windows“添加手机”先打开、配对候选随后到达时仍无选中项的缺陷；r11 已实机验证候选自动选中、8 位码后按钮启用、真实配对成功且不自动挂载。
- r12 又确认未配对候选因两个空设备 ID 相等而被错误显示为“已连接”；运行证据证明当时没有真实配对、rclone 或盘符。r13 已实机确认主列表只显示已配对手机，未配对候选只进入“添加手机”，8 位码前后按钮状态正确。
- Windows r20 验收必须从打包目录运行 `PhoneBridge.Desktop.exe --ui-preview`，使用独立 `0.2.0.20` 数据根；Android 包名为 `org.phonebridge.ng.uipreviewr20`。最终验收已从托盘安全退出，桌面进程、全部 rclone 和 P:/E: 均为 0；隔离根保留两条配对。一次误用开发入口后的清理使正式 Windows 配对记录变为 0，未找到备份；如仍需要旧正式配对只能重新配对。制品、散列和完整证据见 `docs/UI_ACCEPTANCE.md` 与 `docs/audit/P2-005-VALIDATION.md`。
- 开始新任务前必须重新检查 `git status --short --branch`、`git log -5 --oneline --decorate` 和远端状态，不从本交接推断后来发生的变化。

## 2. 新对话必读顺序

1. `AGENTS.md`
2. `DEVELOPMENT_RULES.md`
3. `README.md`
4. `docs/HANDOFF_V0.2.md`
5. `docs/V0.2_PLAN.md`
6. `docs/PRODUCT.md` 中的“v0.2.0 已确认需求”
7. `ARCHITECTURE.md` 与 `DECISIONS.md`
8. `docs/tasks/completed/P2-001-v0.2-requirements-and-ui-plan.md`
9. `docs/design/v0.2/README.md` 与 `docs/tasks/completed/P2-002-ui-wireframes-and-icon-concepts.md`
10. `docs/UI_ACCEPTANCE.md`、`docs/audit/P2-005-VALIDATION.md` 与 `docs/tasks/completed/P2-005-multi-device-sessions.md`

涉及实现时再读对应 Windows/Android 源码和安全、配对、测试规范，不在任务开始时无差别展开所有历史验收文件。

## 3. 用户已确认的 v0.2.0 需求

| ID | 需求 | 预期效果 |
| --- | --- | --- |
| V2-UX-01 | 重构 Windows UI | 采用简约、现代、科技感的界面；设备、设置、关于分层 |
| V2-UX-02 | 重构 Android UI | 首页清楚显示共享状态、目录、主操作和已配对电脑 |
| V2-BRAND-01 | 重做两端图标 | Windows、托盘、安装器、Android 使用统一原创图标体系 |
| V2-I18N-01 | 两端默认简体中文，增加 English 设置 | 首次为简体中文，切换 English 后界面更新并持久保存 |
| V2-UX-03 | 精简 Windows 操作 | 日常只突出配对、连接、打开文件、断开；危险和排障操作进入对应设置 |
| V2-MULTI-01 | 下一版本支持多设备同时连接 | 一台 Windows 至少同时连接 Samsung 与 Redmi 两台手机，两个独立盘符互不影响 |
| V2-REMOVE-01 | 两端本地直接移除配对设备 | 本端删除记录并断开，不等待远端确认；重新连接按新设备配对 |
| V2-CAPTURE-01 | 两端允许系统截屏 | Windows/Android 不设置应用级截屏阻挡 |
| V2-STORAGE-01 | Android 增加“手机存储”共享范围 | 保留原目录选择，并可共享系统允许访问的内部共享存储根目录 |
| V2-LIFECYCLE-01 | Android 返回、后台和退出语义明确 | 子页返回上级；共享时退到后台继续共享；显式退出应用停止共享 |
| V2-METADATA-01 | 两端为已配对设备保存本机“设备备注” | 重启后保持；两端不互相同步，不改变设备身份、凭据、访问模式或远端名称 |
| V2-DIST-01 | Android 永久只从 GitHub 分发 | 不规划 Google Play 或其他应用商店发布 |

视觉参考只用于风格：浅灰背景、白色圆角卡片、蓝色主色、简洁线性图标、清晰留白和左侧分区导航。不复制参考应用的品牌、图标或资产。参考截图没有提交到仓库；精确图像不可用时按上述描述继续，不因此扩大工作。

## 4. 固定边界

- 保持 Kotlin 原生 Android、C#/.NET/WPF、HTTPS/WebDAV、rclone、WinFsp、mDNS、现有配对协议和安全存储路线。
- 当前计划建议 v0.2.0 先交付一套现代浅色主题，不增加主题选择器；这是控制范围的建议，不是用户明确提出的功能要求。深色或多主题只有在用户明确确认扩展范围后才进入开发。
- 多设备指一台 Windows 同时连接多台 Android 手机；不把“一台手机同时连接多台 Windows”扩为新需求。
- Windows 登录或客户端启动后仍不得自动连接、启动 rclone 或创建盘符；用户手动点击连接。
- 不增加云账号、远程访问、同步、投屏、剪贴板、AI、Root 或自研文件系统驱动。
- Android APK 只通过 GitHub Release 分发，永久不增加 Google Play 或其他应用商店发布工作。
- 不因 UI 改动重复既有 5/10/20 GB、PC 睡眠、手机重启等无关测试。每个测试必须对应本次需求、已知风险或已复现缺陷。
- 达到每个任务的明确通过条件后停止；后续想法进入下一版本候选。

## 5. 已确认的代码事实

- `windows/src/PhoneBridge.Desktop/MainWindow.xaml` 已按“设备 / 设置 / 关于”及子页面分层；`MainWindow.xaml.cs` 通过 `DeviceSessionCoordinator` 把设备卡片操作接到各自会话。
- `DeviceSessionCoordinator` 按 `device_id` 创建会话；每个 `ConnectionClient` 绑定一个设备并独占 `ReadOnlyMountManager`。会话另行持有盘符预留、取消、健康监督、重连和进程内日志序号。
- 每设备短期会话文件位于 `Sessions/<device_id hash>/...`，既有 `VfsCache-v1/<certificate sha256>` 路径保持稳定。日志 schema 2 只记录非身份会话序号。
- Android 主界面仍位于 `android/app/src/main/java/org/phonebridge/ng/MainActivity.kt`，已用原生程序化布局实现首页、配对、电脑详情、设置菜单和语言子页；P2-004 已移除 `FLAG_SECURE`。
- Android 停止共享时也可启动仅配对前台服务；session 返回 `share_ready=false`，文件路由关闭。Windows 配对成功只激活记录，不挂载或设置恢复意图；共享开始后仍由用户手动连接。
- Android 共享引擎停止后仍独立读取加密配对库，所以已配对电脑可继续显示、修改访问模式、保存本机设备备注和本地删除；共享目录保留既有七项并追加内部共享存储根目录。`PBS1` version 2 保存设备备注并向后读取 version 1。
- Windows 配对记录 schema 2 的显示别名在 r18 界面统一命名为“设备备注”；旧长备注字段仅保留格式兼容，不再显示。设备备注不参与身份或凭据校验。
- Android 批准后的 Active 配对结果保留到原 120 秒窗口期限；UI 返回首页、进入权限页或暂时后台只取消尚未批准的窗口。Windows 得以完成 Active 轮询和严格 session 验证后返回首页；配对专用服务在窗口自然结束且未共享时自动停止。
- Windows 当前“移除此手机”停止活动挂载并删除本机配对记录、临时地址和自动恢复状态，不发远端请求。Android 当前“移除此电脑”从加密库删除完整 client 记录并关闭其 socket，不要求 Windows 确认。
- 两端候选均默认简体中文。Android 已实现并实测应用内中英文切换和冷启动保持；Windows 本轮只有语言控件，切换及托盘/通知/对话框的完整语言覆盖仍未实现。
- 设备发现、配对协议、凭据、严格 TLS、单个挂载会话、VFS 缓存、安全卸载、托盘、签名和发布链路可以复用。

## 6. 已确认的 Windows 操作收口

| 当前操作 | v0.2.0 位置与名称 |
| --- | --- |
| 配对并连接 | 拆为“添加手机”中的“配对”和设备卡片的“连接”；配对成功不自动挂载 |
| 连接 / 恢复确认 | 设备卡片按状态显示“连接”或“继续连接” |
| 取消 | 只在正在执行的操作旁出现 |
| 打开盘符 | 改名“打开文件”，连接后作为主操作 |
| 卸载 | 改名“断开”，作为设备次要操作 |
| 撤销配对 / 仅忘记本机 | 合并为“移除此手机”，只清理 Windows 本端，不请求手机确认 |
| 删除路径 / 确认删除 | 移出首页，进入设备设置的安全操作并解释“授权一次删除” |
| 手动 IP / 端口、诊断包 | 进入设置的高级排障 |
| Windows 自启动 | 进入设置的常规页，行为仍是只启动客户端 |

## 7. 多设备实现边界

P2-005 已复用发现、配对、凭据、TLS 和单个挂载实现，并把全局唯一连接所有权改成按 `device_id` 隔离的会话：每个会话拥有自己的盘符、rclone 进程、RC 端口、会话目录、取消令牌、健康检查、重连状态和日志上下文。

轻量会话协调器只管理会话集合、盘符冲突、应用退出和托盘汇总。r20 已确认 Samsung 与 Redmi 在同一 LAN 同时挂载两个盘符、各自打开和双向复制；停止 Redmi 后 Samsung 继续可用。

## 8. 唯一下一任务：P2-006 UI、多会话接线与完整语言收口

### 目标

按已确认的 r1/r2 设计收口 Windows 与 Android UI，完成多会话状态在最终界面的必要接线，并补齐两端简体中文/English 可见文本及持久化；不重复 P2-004/P2-005 已通过流程。

### 必须交付

- Windows 设备、设置、关于及子页面使用已确认的层级与控件状态；多设备状态不回退为全局忙状态。
- 两端默认简体中文、English 切换后当前界面更新并持久化；Windows 托盘、对话框、错误与诊断入口纳入同一语言选择。
- 只验证本轮 UI/语言和多会话接线风险，不重复大文件、睡眠、重启或 P2-004/P2-005 真机矩阵。
- 不开始 P2-007 正式图标、安装器、升级和交付刷新。

### 通过条件

两端既定流程均使用确认后的 UI；默认中文和 English 可切换并在冷启动后保持；Windows 多设备卡片在连接、处理、断开和错误状态下只影响对应设备，既有安全与挂载行为不回归。

## 9. 后续顺序

1. P2-002：已完成并经用户确认。
2. P2-003：两端原生 UI 验收（已完成并由用户复验通过）。
3. P2-004：已完成；原定跨端流程及 r18 两端设备备注均已由用户验收通过。
4. P2-005：已完成；按设备隔离的多会话核心和真实双设备最小链路通过。
5. P2-006：唯一下一任务；按验收结果收口 UI、多会话接线和完整语言覆盖。
6. P2-007：正式图标收口、升级验证和 v0.2.0 交付刷新。

编号以后续实际任务文件为准，但顺序和每次一个根因的原则不变。

## 10. 新对话可直接使用的开场提示词

```text
继续开发 PhoneBridge NG v0.2.0。

仓库：https://github.com/yang137197/PhoneBridge-NG
本地目录：C:\Users\yang1\Documents\ChatGPT\samsung link windows\PhoneBridge-NG

先读取 AGENTS.md、DEVELOPMENT_RULES.md、README.md、docs/HANDOFF_V0.2.md、docs/V0.2_PLAN.md、docs/PRODUCT.md、ARCHITECTURE.md、DECISIONS.md，并核对当前 main、git status、最近提交和远端状态。以仓库当前事实为准，不沿用对话中的旧状态。

v0.1.0 已发布。P2-002 静态设计、P2-003 实际 UI、P2-004 配对与记录体验均已完成。P2-005 已把 Windows 改为按 device_id 隔离的多会话：Windows 294/294 测试通过，r20 在 Samsung 与 Redmi 上同时挂载两个盘符、各自双向小文件散列一致，停止 Redmi 后 Samsung 继续浏览和传输。正式 Windows 配对记录当前为 0；如需旧正式配对必须重新配对。UI 验收只能运行打包候选并传入 --ui-preview，不能用 scripts/Start-WindowsPreview.ps1 代替隔离验收。Android 永久只通过 GitHub 分发。保持现有 Kotlin/WPF、HTTPS/WebDAV、rclone、WinFsp、mDNS、配对与安全存储路线。

本轮只执行 P2-006：按已确认设计收口 Windows/Android UI、多会话状态接线和完整简体中文/English 覆盖。不要重复 P2-004 的配对/移除/设备备注或 P2-005 的双盘符传输，不开始 P2-007 正式图标、安装器和交付刷新，不扩大测试范围。完成后把状态、未验证项和唯一下一任务写入仓库并提交推送。
```
