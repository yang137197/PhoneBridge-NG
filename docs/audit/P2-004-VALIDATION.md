# P2-004 本地移除、截屏与配对体验验证

日期：2026-09-26。状态：已完成。r15 的配对、停止共享后的记录、手机存储跨端浏览、两端移除、旧凭据失效和全新配对，以及 r18 两端单一“设备备注”均已由用户验收通过。

## 制品

| 平台 | 本地文件 | SHA-256 | 大小 |
| --- | --- | --- | ---: |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r18.zip` | `A5FDF38872919EABB5080329F44863678CEEDD706CC564D6BB1319C2093A0BC2` | 111,983,778 bytes |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r18.apk` | `AF6E2827997AFF0167C9EC1A85EDEB11C7E8A2241634E5B31A3E381F1068E14E` | 4,139,107 bytes |

两者均为本地验收候选，不是正式发布包。Windows 程序版本为 `0.2.0.18`，Android 包名为 `org.phonebridge.ng.uipreviewr18`、版本为 `0.2.0-ui-preview-r18`。

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
- r18 取消 Windows 独立长备注栏，把既有显示别名改名为单一“设备备注”；保存仍只更新本地显示字段，旧长备注数据保留格式兼容但不再显示。
- r18 为 Android 每个 `PairedClient` 增加本机 `deviceNote`；加密 `PBS1` 升为 version 2，继续读取 version 1 为空备注并在首次受控写入时升级。备注不进入协议、身份、凭据或访问模式，更新时不关闭连接；电脑详情只在记录或共享状态变化时重建，输入过程不被 500 毫秒刷新打断。

## 已验证事实

- Windows `scripts/Verify-Windows.ps1`：Release 构建 0 警告/0 错误，288/288 测试通过。新增测试覆盖本地名称/备注持久化、长度和字符边界、清空、身份不变、schema 1 向后读取及未知 schema 拒绝。
- Android `scripts/Verify-AndroidApp.ps1`：Debug/Release、测试 APK、单元测试和两套 lint 共 123 个任务成功；r15 `assembleUiPreview lintUiPreview` 39 个任务成功。
- Samsung `R5CW429DKDN` 定向 instrumentation：既有共享目录索引不变，“手机存储”指向内部共享存储根；停止共享后服务加载加密配对记录、本地移除成功且旧 token 失效；两项分别 1/1 通过。临时 Debug 与测试包随后卸载。
- Samsung `R5CW429DKDN` 新增定向 instrumentation：批准后模拟 UI 退到后台，配对 GET 继续返回 200/Active，长期凭据 session 返回 200 且 `share_ready=false`；1/1 通过。测试使用本地 Debug/测试 APK，随后卸载。
- Samsung r14 实际 UI：设置中存在语言和“退出应用”；语言子页返回设置；共享目录弹出项包含并可选择“手机存储”；共享运行时二次返回只把界面置于后台且 `SharingService` 继续；显式确认退出后服务停止。
- r15 已用新包名全新安装并打开，私有目录没有 PhoneBridge 身份或配对文件；旧 r14 候选已卸载。Windows r15 已打开并使用新的隔离 `0.2.0.15` 数据根，配对数为 0、没有 rclone 或候选盘符。正式数据未改动。
- r13 以前已实机确认：未配对手机不进入 Windows 主列表，只有手机打开配对窗口后才进入“添加手机”；8 位码完整后按钮启用，配对本身不自动挂载。
- 2026-09-26，用户确认 r15 配对主流程验收完毕、可正常使用；本次真实验收关闭了手机批准后 Windows 不能自动收口、需要“继续连接”的已知竞态。
- 2026-09-26，用户进一步确认设备记录、手机存储跨端浏览、停止共享后的配对记录、两端移除、旧凭据失效和全新配对均已验收通过。
- Windows `scripts/Verify-Windows.ps1`：r18 相关 Release 构建 0 警告/0 错误，289/289 测试通过；新增测试确认单一设备备注更新显示别名，并保留旧长备注数据兼容。
- Android `scripts/Verify-AndroidApp.ps1`：Debug/Release、测试 APK、单元测试和两套 lint 共 123 个任务成功；最终 r18 `assembleUiPreview lintUiPreview` 39 个任务成功。
- Samsung `R5CW429DKDN` 三条定向 instrumentation 分别验证：设备备注持久化且不改变授权/访问模式；version 1 记录以空备注读取并在写入后升级 version 2；停止共享时服务仍可保存备注并继续本地移除。三项均为 1/1 通过，临时 Debug/测试包随后卸载。
- r18 Android 已以全新包名安装并打开，无旧身份或配对；Windows r18 已使用独立 `0.2.0.18` 数据根打开，配对数、rclone 和候选盘符均为 0。旧 r15-r17 候选已停止和卸载，正式数据未改动。
- 2026-09-26，用户确认 r18 两端设备备注的字段、保存、卡片显示、停止共享/重启保持和清空行为通过真实界面验收；P2-004 完成。

## 边界与未验证

- 正式签名 APK、Windows 安装器和正式升级路径未刷新；正式升级仍应保留正式用户数据，与每轮候选全新身份是不同语义。

## 唯一下一任务

开始 P2-005：将 Windows 单一连接所有权改为按 `device_id` 隔离的多会话核心，并以 Samsung 与 Redmi 同时挂载两个盘符、断开一台不影响另一台作为最小真实通过线。
