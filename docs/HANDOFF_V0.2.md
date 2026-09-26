# PhoneBridge NG v0.2.0 新对话交接

更新时间：2026-09-26。适用分支：`main`。

## 1. 仓库与版本状态

- GitHub：<https://github.com/yang137197/PhoneBridge-NG>
- 本地目录：`C:\Users\yang1\Documents\ChatGPT\samsung link windows\PhoneBridge-NG`
- 当前正式发布版本：[`v0.2.0`](https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.0)，标签提交 `60c52b729f2052fa933870bfc175df01d78f17be`。`0.1.0` 不属于 `0.2.0` 的升级来源。
- v0.2.0 需求计划基线：`18b5e0778d4fd03fad95a0a4602cea36dcc83f28`。
- P2-002 静态设计 r1 已由用户全部确认并冻结；视觉方向、Windows 分层/按钮命名、Android 页面层级和图标 A“桥接文件”均已确定。
- P2-003 r2 的实际 UI 已由用户复验通过并完成。
- P2-004 至 P2-008 均已完成：r20 的双设备双盘符与单设备断开隔离通过；r21 的 Windows/Redmi 当前界面和冷启动语言保持通过；正式 `0.2.0` 已在 Samsung 与当前 Windows 完成全新安装、真实配对、Music 共享、`P:` 根目录读取和安全断开，并发布 GitHub Release。
- r10 暴露并确认 Windows“添加手机”先打开、配对候选随后到达时仍无选中项的缺陷；r11 已实机验证候选自动选中、8 位码后按钮启用、真实配对成功且不自动挂载。
- r12 又确认未配对候选因两个空设备 ID 相等而被错误显示为“已连接”；运行证据证明当时没有真实配对、rclone 或盘符。r13 已实机确认主列表只显示已配对手机，未配对候选只进入“添加手机”，8 位码前后按钮状态正确。
- r21 是已完成的历史隔离 UI 候选，r20 是继续有效的双设备真实链路证据；它们都不是当前安装入口。当前正式 `0.2.0` 已重新建立一条 Samsung/Windows 配对记录，制品与完整证据见 `docs/audit/P2-007-VALIDATION.md`。
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
- 两端候选均默认简体中文。P2-006 已实现 Windows 动态资源、托盘同步和按数据根持久化，并让 Android 后台服务/通知读取同一应用语言；r21 Windows 与 Redmi 的当前界面和冷启动 English 保持已实测，Android 真实通知外观未触发。
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

## 8. P2-007 完成状态

### 目标

已把 v0.2 UI、语言和多会话实现转入正式交付，完成正式图标、安装器、签名 APK、全新安装及源码/许可/散列/manifest 一致性验证，并把 `0.2.0` 建立为后续正式升级的首个基线。

### 已交付

- 把已确认的 A“桥接文件”应用图标收口到 Windows EXE、安装器、任务栏/窗口和 Android adaptive/monochrome 正式资源；托盘使用已确认的单色减线方向。
- 生成正式 Windows 安装器与长期签名 Android APK，验证 `0.2.0` 全新安装和基础运行；`0.1.0` 不属于正式升级来源，隔离候选数据也不作为正式安装证据。
- 刷新交付 README、源码 ZIP、许可、SHA256SUMS 和 manifest，确保版本、源码和二进制一一对应。
- 只补 P2-007 的图标、全新安装和制品一致性风险，不重复大文件、睡眠或 P2-005 双设备传输矩阵。

### 验收结果

通过。正式安装器与 APK 使用最终图标和正式身份；`0.2.0` 全新安装、配对、挂载读取和安全断开通过，并形成未来 `0.2.x` 到 `0.3+` 的升级基线；交付目录中的二进制、源码、许可、散列和 manifest 全部一致且可独立回读。

## 9. 后续顺序

1. P2-002：已完成并经用户确认。
2. P2-003：两端原生 UI 验收（已完成并由用户复验通过）。
3. P2-004：已完成；原定跨端流程及 r18 两端设备备注均已由用户验收通过。
4. P2-005：已完成；按设备隔离的多会话核心和真实双设备最小链路通过。
5. P2-006：已完成；UI、多会话卡片状态和完整语言覆盖已收口。
6. P2-007：已完成；正式图标、全新安装、真实配对/挂载和 v0.2.0 交付刷新通过。

编号以后续实际任务文件为准，但顺序和每次一个根因的原则不变。

## 10. 新对话可直接使用的开场提示词

```text
继续维护 PhoneBridge NG；`0.2.0` 正式本地侧载验收已完成。

仓库：https://github.com/yang137197/PhoneBridge-NG
本地目录：C:\Users\yang1\Documents\ChatGPT\samsung link windows\PhoneBridge-NG

先读取 AGENTS.md、DEVELOPMENT_RULES.md、README.md、docs/HANDOFF_V0.2.md、docs/V0.2_PLAN.md、docs/PRODUCT.md、ARCHITECTURE.md、DECISIONS.md，并核对当前 main、git status、最近提交和远端状态。以仓库当前事实为准，不沿用对话中的旧状态。

P2-002 至 P2-008 均已完成。`v0.2.0` GitHub Release 已发布并设为 Latest，标签精确指向 `60c52b729f2052fa933870bfc175df01d78f17be`，六项 Release 资产与本地正式交付摘要一致。`0.2.0` 已通过 Windows/Samsung 全新安装、真实配对、Music 共享、`P:` 根目录读取和安全断开，并成为未来正式升级的首个基线；不定义 `0.1.0 → 0.2.0` 升级路径。P2-005 r20 双设备证据与 P2-006 r21 UI/语言证据继续有效。Android 永久只通过 GitHub 分发。保持现有 Kotlin/WPF、HTTPS/WebDAV、rclone、WinFsp、mDNS、配对与安全存储路线。

当前没有已授权的后续开发任务。开始新需求前先核对当前 Git/交付状态并建立单一任务；若进入 `0.3+`，必须以当前 `0.2.0` 正式数据为升级保持基线。秘密不进聊天或仓库，不重复已经通过的双设备、大文件或睡眠测试。
```
