# P2-004 本地移除、截屏与全新配对流程验证

日期：2026-09-24。状态：实现与隔离验证完成，等待用户完成 r10 真实端到端验收。

## 制品

| 平台 | 本地文件 | SHA-256 | 大小 |
| --- | --- | --- | ---: |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r10.zip` | `34125EBF9D36C4564DFF09DFE1E4C1EA4518724D182BD6BEEBBC1BC051F5CAE7` | 115,424,253 bytes |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r10.apk` | `97072BF816F1C7F841E7D5545AB8D3AA9E3589FDB21C9DA0D0AA175EB48686AF` | 4,130,899 bytes |

两者均为本地验收候选，不是正式发布包。Windows 程序版本为 `0.2.0.10`，Android 包名为 `org.phonebridge.ng.uipreviewr10`、版本为 `0.2.0-ui-preview-r10`。

## 根因

- Windows 旧 `--ui-preview` 只隔离单实例互斥体，仍读取正式配对目录；本机正式目录已有 3 条记录，导致已配对候选的“配对”按钮按既有逻辑禁用。
- Android 旧 `uiPreview` 固定包名并用 `adb install -r` 覆盖，保留上轮数据；同时“配对新电脑”被硬性依赖于共享引擎已启动。
- 因此开始共享后，Windows 会沿已保存身份进入连接路径，造成“无法配对新电脑但自动连接”的表象。以上均由源码、目录内容和实际候选状态确认，不是推测。

## 已验证事实

- Windows `scripts/Verify-Windows.ps1`：Release 构建 0 警告/0 错误，283/283 测试通过。新增测试覆盖未共享时完成配对但不挂载，以及不同预览修订使用不同的非正式数据根。
- Android `scripts/Verify-AndroidApp.ps1`：Debug/Release、测试 APK、单元测试和两套 lint 共 123 个任务成功；r10 `assembleUiPreview lintUiPreview` 39 个任务成功。
- Samsung 隔离 instrumentation：停止共享时可完成配对与授权，session 为 `share_ready=false`、文件请求为 409；共享门禁打开后 session 为 true 且文件可读，1/1 通过。此前本地删除/旧 token/新配对两条测试 2/2 通过。
- r10 在 Samsung 上首次安装后，首页同时启用“配对新电脑”和“开始共享”，显示“尚无已配对电脑”。启动仅配对服务时前台服务存在且 `sharing=false`，通知明确文件共享尚未开始；随后已清除 r10 数据并恢复到无 PhoneBridge 身份/配对记录的首页。
- Android r10 实际 `screencap` 得到 134,396-byte 正常 PNG；旧 r8/r9 已卸载，当前只保留 `org.phonebridge.ng.uipreviewr10`。
- Windows r10 实际窗口可响应，“添加手机”页显示“手机打开配对 → 选择手机 → 输入配对码 → 手机批准”，并明确配对后开始共享、再手动连接。隔离根只有日志、无 Pairings；正式 3 条记录未改变。

## 边界与未验证

- 尚未由用户用 r10 完成真实手机批准、Windows 手动连接、盘符出现，以及两端各一次本地移除和全新重配。
- 本地移除不联系远端，不能同时删除远端自己的旧记录；产品只承诺删除本端设备配对记录、凭据、访问模式、临时端点、活动连接和进程内恢复状态。
- 正式签名 APK、Windows 安装器和正式升级路径未刷新；正式升级仍应保留正式用户数据，与每轮验收候选全新身份是不同语义。

## 唯一下一任务

用户按 `docs/UI_ACCEPTANCE.md` 使用 r10 完成一次全新配对、后共享、手动连接，并分别验证两端本地移除与全新重配；通过前不开始 P2-005 多设备核心。
