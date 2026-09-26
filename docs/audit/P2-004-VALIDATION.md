# P2-004 本地移除、截屏与配对体验验证

日期：2026-09-26。状态：本轮新增行为已完成自动验证和 Samsung 定向实测，等待用户使用 r14 完成真实跨端验收。

## 制品

| 平台 | 本地文件 | SHA-256 | 大小 |
| --- | --- | --- | ---: |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r14.zip` | `49071B242838B773B52E11408B3239553D4E1610BC9F21F5D9A7E3066A98F846` | 115,427,254 bytes |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r14.apk` | `BA98F337E772AD6BAB3956018921822E3695B0B75EE561FF899A315A0E1A59BD` | 4,134,327 bytes |

两者均为本地验收候选，不是正式发布包。Windows 程序版本为 `0.2.0.14`，Android 包名为 `org.phonebridge.ng.uipreviewr14`、版本为 `0.2.0-ui-preview-r14`。

## 本轮实现

- Windows 配对成功并重载记录后返回设备首页，不自动连接或挂载。
- Windows 配对记录新增仅存于本机的名称和备注；schema 2 写入新字段并继续读取 schema 1，设备身份、客户端身份和凭据不因改名变化。
- Android 在共享引擎停止时仍从加密配对库展示、修改和本地删除已配对电脑。
- Android 保留七个既有共享目录索引，并在末尾增加“手机存储”，映射 `Environment.getExternalStorageDirectory()` 所代表的内部共享存储根目录。
- Android 配对完成后关闭仅配对引擎；开始共享时按用户当前目录选择重新创建引擎，避免先配对锁死目录选择。
- Android 返回手势/虚拟返回键按页面层级返回；首页二次返回在共享中进入后台、未共享时退出；设置新增显式“退出应用”，确认后停止共享并退出。
- 所有 Android 构建继续允许系统截屏；Android 正式分发边界固定为 GitHub Release，不计划 Google Play 或其他应用商店发布。

## 已验证事实

- Windows `scripts/Verify-Windows.ps1`：Release 构建 0 警告/0 错误，288/288 测试通过。新增测试覆盖本地名称/备注持久化、长度和字符边界、清空、身份不变、schema 1 向后读取及未知 schema 拒绝。
- Android `scripts/Verify-AndroidApp.ps1`：Debug/Release、测试 APK、单元测试和两套 lint 共 123 个任务成功；r14 `assembleUiPreview lintUiPreview` 39 个任务成功。
- Samsung `R5CW429DKDN` 定向 instrumentation：既有共享目录索引不变，“手机存储”指向内部共享存储根；停止共享后服务加载加密配对记录、本地移除成功且旧 token 失效；两项分别 1/1 通过。临时 Debug 与测试包随后卸载。
- Samsung r14 实际 UI：设置中存在语言和“退出应用”；语言子页返回设置；共享目录弹出项包含并可选择“手机存储”；共享运行时二次返回只把界面置于后台且 `SharingService` 继续；显式确认退出后服务停止。
- r14 验证结束后已执行应用数据清除并重新授权必要系统权限；当前首页为共享停止、无已配对电脑。Windows r14 使用隔离 `0.2.0.14` 数据根，配对数为 0；正式数据未改动。
- r13 以前已实机确认：未配对手机不进入 Windows 主列表，只有手机打开配对窗口后才进入“添加手机”；8 位码完整后按钮启用，配对本身不自动挂载。

## 边界与未验证

- 尚未由用户用 r14 完成真实手机批准、Windows 配对成功后返回首页、开始共享、手动连接和盘符浏览。
- Windows 本地名称/备注尚未在真实配对记录上验证保存、重启保持和界面显示。
- “手机存储”根目录本机映射已验证，但 Windows 挂载后实际浏览系统允许的顶层目录尚未跨端验证；Android 系统仍可能限制特定私有目录。
- 尚未验证停止共享后的真实电脑卡片、访问模式修改，以及两端各一次本地移除、旧凭据失效和全新重配。
- 正式签名 APK、Windows 安装器和正式升级路径未刷新；正式升级仍应保留正式用户数据，与每轮候选全新身份是不同语义。

## 唯一下一任务

用户按 `docs/UI_ACCEPTANCE.md` 使用 r14 完成真实跨端配对、手动连接、停止共享后的配对记录、手机存储浏览、Windows 本地名称/备注，以及两端本地移除和全新重配验收；通过前不开始 P2-005 多设备核心。
