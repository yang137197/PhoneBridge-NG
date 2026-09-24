# PhoneBridge NG v0.2.0 新对话交接

更新时间：2026-09-24。适用分支：`main`。

## 1. 仓库与版本状态

- GitHub：<https://github.com/yang137197/PhoneBridge-NG>
- 本地目录：`C:\Users\yang1\Documents\ChatGPT\samsung link windows\PhoneBridge-NG`
- 已发布版本：`v0.1.0`，标签提交 `e74e3a2403d3485883e24a723826c786973eb29e`。
- v0.2.0 需求计划基线：`18b5e0778d4fd03fad95a0a4602cea36dcc83f28`。
- P2-002 静态设计 r1 已由用户全部确认并冻结；视觉方向、Windows 分层/按钮命名、Android 页面层级和图标 A“桥接文件”均已确定。
- v0.2.0 仍未修改 Windows/Android 业务代码、资源或构建配置，也没有生成候选安装包或 APK。
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

视觉参考只用于风格：浅灰背景、白色圆角卡片、蓝色主色、简洁线性图标、清晰留白和左侧分区导航。不复制参考应用的品牌、图标或资产。参考截图没有提交到仓库；精确图像不可用时按上述描述继续，不因此扩大工作。

## 4. 固定边界

- 保持 Kotlin 原生 Android、C#/.NET/WPF、HTTPS/WebDAV、rclone、WinFsp、mDNS、现有配对协议和安全存储路线。
- 当前计划建议 v0.2.0 先交付一套现代浅色主题，不增加主题选择器；这是控制范围的建议，不是用户明确提出的功能要求。深色或多主题只有在用户明确确认扩展范围后才进入开发。
- 多设备指一台 Windows 同时连接多台 Android 手机；不把“一台手机同时连接多台 Windows”扩为新需求。
- Windows 登录或客户端启动后仍不得自动连接、启动 rclone 或创建盘符；用户手动点击连接。
- 不增加云账号、远程访问、同步、投屏、剪贴板、AI、Root 或自研文件系统驱动。
- 不因 UI 改动重复既有 5/10/20 GB、PC 睡眠、手机重启等无关测试。每个测试必须对应本次需求、已知风险或已复现缺陷。
- 达到每个任务的明确通过条件后停止；后续想法进入下一版本候选。

## 5. 已确认的代码事实

- `windows/src/PhoneBridge.Desktop/MainWindow.xaml` 把配对码、端点、盘符、删除路径、手动地址和九个操作按钮放在同一主界面。
- `windows/src/PhoneBridge.Desktop/MainWindow.xaml.cs` 当前创建一个 `ConnectionClient`。
- `windows/src/PhoneBridge.Connection/ConnectionClient.cs` 当前持有一个 `ReadOnlyMountManager` 和一个 `Connected` 设备状态；多设备必须重构为按 `device_id` 隔离的会话所有权。
- Android 主界面位于 `android/app/src/main/java/org/phonebridge/ng/MainActivity.kt`，当前使用程序化纵向布局。
- 两端已有 `zh-CN`/`en-US` 文本资源，但 Windows `App.xaml.cs` 和 Android 系统资源选择仍按系统语言工作，没有应用内持久语言设置。
- 设备发现、配对协议、凭据、严格 TLS、单个挂载会话、VFS 缓存、安全卸载、托盘、签名和发布链路可以复用。

## 6. 已确认的 Windows 操作收口

| 当前操作 | v0.2.0 位置与名称 |
| --- | --- |
| 配对并连接 | “添加手机”流程中的“配对”，成功后自动连接 |
| 连接 / 恢复确认 | 设备卡片按状态显示“连接”或“继续连接” |
| 取消 | 只在正在执行的操作旁出现 |
| 打开盘符 | 改名“打开文件”，连接后作为主操作 |
| 卸载 | 改名“断开”，作为设备次要操作 |
| 撤销配对 | 改名“移除此手机”，进入设备设置 |
| 仅忘记本机 | 进入设备设置的高级排障 |
| 删除路径 / 确认删除 | 移出首页，进入设备设置的安全操作并解释“授权一次删除” |
| 手动 IP / 端口、诊断包 | 进入设置的高级排障 |
| Windows 自启动 | 进入设置的常规页，行为仍是只启动客户端 |

## 7. 多设备实现边界

可复用发现、配对、凭据、TLS 和单个挂载实现。必须把全局唯一连接所有权改成按 `device_id` 隔离的会话：每个会话拥有自己的盘符、rclone 进程、RC 端口、缓存目录、取消令牌、健康检查、重连状态和日志上下文。

只增加一个轻量会话协调器管理会话集合、盘符冲突、应用退出和托盘汇总。一个设备的取消、断开或失败不得停止另一设备。第一条真实通过线是 Samsung 与 Redmi 在同一 LAN 同时挂载两个盘符，各自可打开和双向复制；断开其中一个后另一个继续可用。

## 8. 唯一下一任务：P2-003

### 目标

建立按 `device_id` 隔离的 Windows 多会话核心，并在真实局域网完成 Samsung 与 Redmi 同时挂载两个独立盘符的最小链路。本任务不同时实现 P2-002 的新 UI。

### 必须交付

- 每台设备独立拥有盘符、rclone 进程、RC 端口、缓存目录、取消令牌、健康检查、重连状态和日志上下文。
- 一个轻量会话协调器管理会话集合、盘符冲突、应用退出和托盘汇总；不复制配对、TLS、挂载或安全卸载实现。
- 一个设备的连接、取消、断开或失败不得改变另一设备会话。
- 只增加完成真实双设备最小链路所需的入口和测试，不实现新视觉界面、语言设置或正式图标资产。

### 通过条件

Samsung 与 Redmi 在同一真实局域网同时连接为两个不同盘符；两个盘符都能打开、读取并各完成一次双向合成文件复制。断开或停止其中一台后，另一台仍可浏览和传输；进程、缓存、凭据和日志不能串设备。只运行与本次多会话改动直接相关的测试，不重复无关的大文件、睡眠或重启矩阵。

## 9. 后续顺序

1. P2-002：已完成并经用户确认。
2. P2-003：按设备隔离的多会话核心和真实双设备最小链路（唯一下一任务）。
3. P2-004：Windows 新 UI、操作精简和语言设置。
4. P2-005：Android 新 UI 和语言设置。
5. P2-006：正式图标、升级验证和 v0.2.0 交付刷新。

编号以后续实际任务文件为准，但顺序和每次一个根因的原则不变。

## 10. 新对话可直接使用的开场提示词

```text
继续开发 PhoneBridge NG v0.2.0。

仓库：https://github.com/yang137197/PhoneBridge-NG
本地目录：C:\Users\yang1\Documents\ChatGPT\samsung link windows\PhoneBridge-NG

先读取 AGENTS.md、DEVELOPMENT_RULES.md、README.md、docs/HANDOFF_V0.2.md、docs/V0.2_PLAN.md、docs/PRODUCT.md、ARCHITECTURE.md、DECISIONS.md，并核对当前 main、git status、最近提交和远端状态。以仓库当前事实为准，不沿用对话中的旧状态。

v0.1.0 已发布。v0.2.0 已完成需求计划；P2-002 静态设计 r1 已由用户全部确认并冻结，图标采用 A“桥接文件”，仍未修改业务代码。已确认范围：重构 Windows/Android UI、重做两端图标、两端首次默认简体中文并提供 English 设置、精简 Windows 操作、支持一台 Windows 同时连接至少两台 Android 手机。保持现有 Kotlin/WPF、HTTPS/WebDAV、rclone、WinFsp、mDNS、配对与安全存储路线。

本轮只执行 P2-003：把当前全局唯一的 Windows `ConnectionClient`/`MountManager` 所有权重构为按 `device_id` 隔离的多会话核心，并完成 Samsung 与 Redmi 同时挂载两个独立盘符的真实最小链路。不要同时实现 Windows/Android 新 UI、语言设置或正式图标，不重复无关的大文件、睡眠或重启测试。完成后把状态、未验证项和唯一下一任务写入仓库并提交推送。
```
