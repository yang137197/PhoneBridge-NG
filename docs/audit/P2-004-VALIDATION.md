# P2-004 本地移除、截屏与配对体验验证

日期：2026-09-26。状态：r14 真实流程发现手机批准后过早关闭配对结果的竞态；r15 已完成修复、自动验证和 Samsung 定向测试，且配对主流程已由用户真实验收通过，可正常使用。P2-004 其余记录管理、手机存储浏览及两端移除/重配仍待验收。

## 制品

| 平台 | 本地文件 | SHA-256 | 大小 |
| --- | --- | --- | ---: |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r15.zip` | `217163C87D0A8B82A9F43616E1BC0B87AAD40A4618F09571BB6FA120AFCBA3A1` | 115,427,248 bytes |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r15.apk` | `20989A33F0149E88342AB97F7AABC7B79A8C44283E9501A49149CDACD254E1E0` | 4,135,151 bytes |

两者均为本地验收候选，不是正式发布包。Windows 程序版本为 `0.2.0.15`，Android 包名为 `org.phonebridge.ng.uipreviewr15`、版本为 `0.2.0-ui-preview-r15`。

## r14 新增根因证据

- 用户真实操作中，手机已批准且开始共享，但 Windows 仍停留在添加页；返回首页后记录为 Pending，显示“待确认/继续连接”。
- Windows 诊断日志记录同一次流程先后为 `PairingStarted`、`AuthenticationCompleted=Unauthorized/Failed`；用户点击“继续连接”后出现 `ConnectionCompleted=Success` 与 `MountStateChanged=Mounted`。
- Android 批准先可靠写入 Active；但 `MainActivity.renderPairing` 一观察到 Active 就调用 `closePairing`，清除 grant/attempt 并停止配对专用引擎。Windows 按协议每 1.1 秒轮询 Active，并在收到 Active 后用长期凭据验证 session；Android 的提前关闭使下一次 GET 得到 401，Windows只能保留已经安全落盘的 Pending。
- 因此“继续连接”可以恢复，是因为手机端长期 Active 记录和 Windows Pending 凭据本身有效；这排除了短码错误、发现失败或长期 token 不一致。

## 本轮实现

- Windows 配对成功并重载记录后返回设备首页，不自动连接或挂载。
- Windows 配对记录新增仅存于本机的名称和备注；schema 2 写入新字段并继续读取 schema 1，设备身份、客户端身份和凭据不因改名变化。
- Android 在共享引擎停止时仍从加密配对库展示、修改和本地删除已配对电脑。
- Android 保留七个既有共享目录索引，并在末尾增加“手机存储”，映射 `Environment.getExternalStorageDirectory()` 所代表的内部共享存储根目录。
- Android 配对完成后关闭仅配对引擎；开始共享时按用户当前目录选择重新创建引擎，避免先配对锁死目录选择。
- Android 返回手势/虚拟返回键按页面层级返回；首页二次返回在共享中进入后台、未共享时退出；设置新增显式“退出应用”，确认后停止共享并退出。
- 所有 Android 构建继续允许系统截屏；Android 正式分发边界固定为 GitHub Release，不计划 Google Play 或其他应用商店发布。
- r15 保留现有协议规定的生命周期：批准后的 Active 结果在原 120 秒窗口期限内可回读，不因 UI 返回首页、进入权限页或暂时后台而取消；尚未批准的窗口仍在离开前台时取消。配对专用服务在窗口自然结束且未共享时自动停止。

## 已验证事实

- Windows `scripts/Verify-Windows.ps1`：Release 构建 0 警告/0 错误，288/288 测试通过。新增测试覆盖本地名称/备注持久化、长度和字符边界、清空、身份不变、schema 1 向后读取及未知 schema 拒绝。
- Android `scripts/Verify-AndroidApp.ps1`：Debug/Release、测试 APK、单元测试和两套 lint 共 123 个任务成功；r15 `assembleUiPreview lintUiPreview` 39 个任务成功。
- Samsung `R5CW429DKDN` 定向 instrumentation：既有共享目录索引不变，“手机存储”指向内部共享存储根；停止共享后服务加载加密配对记录、本地移除成功且旧 token 失效；两项分别 1/1 通过。临时 Debug 与测试包随后卸载。
- Samsung `R5CW429DKDN` 新增定向 instrumentation：批准后模拟 UI 退到后台，配对 GET 继续返回 200/Active，长期凭据 session 返回 200 且 `share_ready=false`；1/1 通过。测试使用本地 Debug/测试 APK，随后卸载。
- Samsung r14 实际 UI：设置中存在语言和“退出应用”；语言子页返回设置；共享目录弹出项包含并可选择“手机存储”；共享运行时二次返回只把界面置于后台且 `SharingService` 继续；显式确认退出后服务停止。
- r15 已用新包名全新安装并打开，私有目录没有 PhoneBridge 身份或配对文件；旧 r14 候选已卸载。Windows r15 已打开并使用新的隔离 `0.2.0.15` 数据根，配对数为 0、没有 rclone 或候选盘符。正式数据未改动。
- r13 以前已实机确认：未配对手机不进入 Windows 主列表，只有手机打开配对窗口后才进入“添加手机”；8 位码完整后按钮启用，配对本身不自动挂载。
- 2026-09-26，用户确认 r15 配对主流程验收完毕、可正常使用；本次真实验收关闭了手机批准后 Windows 不能自动收口、需要“继续连接”的已知竞态。

## 边界与未验证

- Windows 本地名称/备注尚未在真实配对记录上验证保存、重启保持和界面显示。
- “手机存储”根目录本机映射已验证，但 Windows 挂载后实际浏览系统允许的顶层目录尚未跨端验证；Android 系统仍可能限制特定私有目录。
- 尚未验证停止共享后的真实电脑卡片、访问模式修改，以及两端各一次本地移除、旧凭据失效和全新重配。
- 正式签名 APK、Windows 安装器和正式升级路径未刷新；正式升级仍应保留正式用户数据，与每轮候选全新身份是不同语义。

## 唯一下一任务

使用现有 r15 配对记录完成停止共享后的电脑卡片与访问模式、手机存储跨端浏览、Windows 本地名称/备注，以及两端各一次本地移除、旧凭据失效和全新重配验收；通过前不开始 P2-005 多设备核心。
