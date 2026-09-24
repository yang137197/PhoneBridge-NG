# PhoneBridge NG v0.2.0 新对话交接

更新时间：2026-09-24。适用分支：`main`。

## 1. 仓库与版本状态

- GitHub：<https://github.com/yang137197/PhoneBridge-NG>
- 本地目录：`C:\Users\yang1\Documents\ChatGPT\samsung link windows\PhoneBridge-NG`
- 已发布版本：`v0.1.0`，标签提交 `e74e3a2403d3485883e24a723826c786973eb29e`。
- v0.2.0 需求计划基线：`18b5e0778d4fd03fad95a0a4602cea36dcc83f28`。
- P2-002 静态设计 r1 已由用户全部确认并冻结；视觉方向、Windows 分层/按钮命名、Android 页面层级和图标 A“桥接文件”均已确定。
- P2-003 r2 的实际 UI 已由用户复验通过并完成。
- P2-004 已实现两端本地直接移除、全构建允许截屏，并修正为“先配对、后共享、Windows 手动连接”。根因确认是旧候选复用正式/上轮数据，加上 Android 配对入口错误依赖共享引擎；隔离回归通过，等待用户真实端到端验收。多设备核心尚未开始。
- r10 暴露并确认 Windows“添加手机”先打开、配对候选随后到达时仍无选中项的缺陷；r11 已实机验证候选自动选中、8 位码后按钮启用、真实配对成功且不自动挂载。
- r12 又确认未配对候选因两个空设备 ID 相等而被错误显示为“已连接”；运行证据证明当时没有真实配对、rclone 或盘符。r13 已实机确认主列表只显示已配对手机，未配对候选只进入“添加手机”，8 位码前后按钮状态正确。
- Windows r13 验收窗口已打开，使用独立 `0.2.0.13` 数据根且未读取正式 3 条配对记录；Android `org.phonebridge.ng.uipreviewr13` 已重新全新安装并停在无身份/配对的首页，旧候选已卸载。制品和散列见 `docs/UI_ACCEPTANCE.md`。
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
10. `docs/UI_ACCEPTANCE.md`、`docs/audit/P2-004-VALIDATION.md` 与 `docs/tasks/active/P2-004-local-removal-and-screenshot-policy.md`

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

- P2-003 候选的 `windows/src/PhoneBridge.Desktop/MainWindow.xaml` 已按“设备 / 设置 / 关于”及子页面分层；原有连接、挂载和安全操作仍由同一个窗口代码接入。
- `windows/src/PhoneBridge.Desktop/MainWindow.xaml.cs` 当前创建一个 `ConnectionClient`。
- `windows/src/PhoneBridge.Connection/ConnectionClient.cs` 当前持有一个 `ReadOnlyMountManager` 和一个 `Connected` 设备状态；多设备必须重构为按 `device_id` 隔离的会话所有权。
- Android 主界面仍位于 `android/app/src/main/java/org/phonebridge/ng/MainActivity.kt`，已用原生程序化布局实现首页、配对、电脑详情、设置菜单和语言子页；P2-004 已移除 `FLAG_SECURE`。
- Android 停止共享时也可启动仅配对前台服务；session 返回 `share_ready=false`，文件路由关闭。Windows 配对成功只激活记录，不挂载或设置恢复意图；共享开始后仍由用户手动连接。
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

可复用发现、配对、凭据、TLS 和单个挂载实现。必须把全局唯一连接所有权改成按 `device_id` 隔离的会话：每个会话拥有自己的盘符、rclone 进程、RC 端口、缓存目录、取消令牌、健康检查、重连状态和日志上下文。

只增加一个轻量会话协调器管理会话集合、盘符冲突、应用退出和托盘汇总。一个设备的取消、断开或失败不得停止另一设备。第一条真实通过线是 Samsung 与 Redmi 在同一 LAN 同时挂载两个盘符，各自可打开和双向复制；断开其中一个后另一个继续可用。

## 8. 唯一下一任务：使用 r13 完成 P2-004 真实端到端验收

### 目标

从 r13 的全新状态确认未配对手机不出现在主列表，再完成“Android 配对新电脑 → Windows 添加手机并输入配对码 → Android 批准 → Android 开始共享 → Windows 手动连接”，最后分别从 Windows 与 Android 各执行一次本地移除并全新重配。通过前不开始 P2-005 多设备核心。

### 必须交付

- 配对完成后先确认 Windows 没有自动挂载；手机开始共享后必须由用户在 Windows 手动点击“连接”。
- Windows 点击“移除此手机”，确认不出现手机确认依赖，本机记录、临时地址和活动挂载消失。
- Android 使用可重建配对，点击“移除此电脑”，确认电脑无需确认、手机列表记录消失、旧 token 被拒绝。
- 两条路径各完成一次全新重新配对；不使用不可恢复的正式配对做试验。
- Android 再完成一次系统截屏；不扩展多设备、安装器或正式签名验收。

### 通过条件

先配对、后共享、手动连接通过；两端真实移除均不依赖远端确认，旧凭据失效，全新重配成功。P2-004 完成前不启动多设备重构。

## 9. 后续顺序

1. P2-002：已完成并经用户确认。
2. P2-003：两端原生 UI 验收（已完成并由用户复验通过）。
3. P2-004：两端本地直接移除、允许截屏和全新配对流程（已实现，等待 r13 真实端到端验收）。
4. P2-005：按设备隔离的多会话核心和真实双设备最小链路。
5. P2-006：按验收结果收口 UI、多会话接线和完整语言覆盖。
6. P2-007：正式图标收口、升级验证和 v0.2.0 交付刷新。

编号以后续实际任务文件为准，但顺序和每次一个根因的原则不变。

## 10. 新对话可直接使用的开场提示词

```text
继续开发 PhoneBridge NG v0.2.0。

仓库：https://github.com/yang137197/PhoneBridge-NG
本地目录：C:\Users\yang1\Documents\ChatGPT\samsung link windows\PhoneBridge-NG

先读取 AGENTS.md、DEVELOPMENT_RULES.md、README.md、docs/HANDOFF_V0.2.md、docs/V0.2_PLAN.md、docs/PRODUCT.md、ARCHITECTURE.md、DECISIONS.md，并核对当前 main、git status、最近提交和远端状态。以仓库当前事实为准，不沿用对话中的旧状态。

v0.1.0 已发布。P2-002 静态设计和 P2-003 实际 UI 已由用户确认。P2-004 已实现两端本地直接移除、全构建允许截屏和“先配对、后共享、Windows 手动连接”，并修复 Windows 配对候选迟到时未自动选中、未配对候选误显示“已连接”的问题；Windows 主列表只显示已配对手机，未配对候选只进入“添加手机”。每轮验收候选使用新的 Windows 数据根与 Android 应用 ID。当前 r13 已全新部署，等待真实端到端验收。多设备核心顺延为 P2-005。保持现有 Kotlin/WPF、HTTPS/WebDAV、rclone、WinFsp、mDNS、配对与安全存储路线。

本轮只完成 P2-004 r13 真实端到端验收：按 `docs/UI_ACCEPTANCE.md` 从全新状态确认未配对手机不出现在主列表，再验证手机开启配对、Windows 添加手机并输入 8 位码、手机批准、后共享及 Windows 手动连接，最后分别验证 Windows 和 Android 本地移除、旧凭据失效和全新重配。不要开始 P2-005 多设备核心，不把候选当正式发布包，不扩展功能或测试。完成后把状态、未验证项和唯一下一任务写入仓库并提交推送。
```
