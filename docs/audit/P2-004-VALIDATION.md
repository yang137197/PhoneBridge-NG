# P2-004 本地移除、截屏与全新配对流程验证

日期：2026-09-24。状态：未配对候选误显示“已连接”的缺陷已确认并修复，等待用户完成 r13 真实端到端验收。

## 制品

| 平台 | 本地文件 | SHA-256 | 大小 |
| --- | --- | --- | ---: |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r13.zip` | `7F342AA2B3B0499055ED2E42BA46F251A5B7B322FD8641F5AE20979111BFF3E6` | 115,424,736 bytes |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r13.apk` | `B0841DB2B31B8EEB56C6B49A301AC9D4BD8C6DE0392470F01DC844BCD18C37FF` | 4,130,899 bytes |

两者均为本地验收候选，不是正式发布包。Windows 程序版本为 `0.2.0.13`，Android 包名为 `org.phonebridge.ng.uipreviewr13`、版本为 `0.2.0-ui-preview-r13`。

## 根因

- Windows 旧 `--ui-preview` 只隔离单实例互斥体，仍读取正式配对目录；本机正式目录已有 3 条记录，导致已配对候选的“配对”按钮按既有逻辑禁用。
- Android 旧 `uiPreview` 固定包名并用 `adb install -r` 覆盖，保留上轮数据；同时“配对新电脑”被硬性依赖于共享引擎已启动。
- 因此开始共享后，Windows 会沿已保存身份进入连接路径，造成“无法配对新电脑但自动连接”的表象。以上均由源码、目录内容和实际候选状态确认，不是推测。
- r10 复验又确认独立的 UI 时序缺陷：用户先打开“添加手机”时没有候选，Android 配对广播随后到达；Windows 日志记录候选已添加，但 `RebuildRows` 只尝试恢复原选中 ID，原值为空时仍保持无选中项。页面没有另一个候选选择控件，因此标题持续为“请选择附近手机”，即使 8 位码完整也无法启用按钮。
- r12 截图中的“已连接”与“未配对”来自同一个空值比较错误：未挂载时 `connectedDevice` 为 `null`，未配对候选的 `record?.DeviceId` 也为 `null`，原逻辑把两者相等误判为已连接。r12 运行证据同时确认配对记录为 0、rclone 为 0、P: 不存在，因此没有真实连接或绕过配对。

## 已验证事实

- Windows `scripts/Verify-Windows.ps1`：Release 构建 0 警告/0 错误，最终 286/286 测试通过。新增测试确认空配对/空挂载不能显示为已连接，并确认未配对候选只有手机配对窗口打开时才可进入配对流程。首次全量运行中一个既有挂载时序用例因并行调度先报告绝对超时；该用例定向复验 1/1、随后最终全量 286/286 通过，未修改挂载代码。
- Android `scripts/Verify-AndroidApp.ps1`：Debug/Release、测试 APK、单元测试和两套 lint 共 123 个任务成功；r13 `assembleUiPreview lintUiPreview` 39 个任务成功。
- Samsung 隔离 instrumentation：停止共享时可完成配对与授权，session 为 `share_ready=false`、文件请求为 409；共享门禁打开后 session 为 true 且文件可读，1/1 通过。此前本地删除/旧 token/新配对两条测试 2/2 通过。
- r11 定向实机复验从“添加手机”先打开且显示“请选择附近手机”开始；Android 后开启配对后，Windows 自动变为 `SM-S9180`，输入 8 位码后 UI Automation 回读“配对”按钮 `IsEnabled=true`。实际点击后配对完成，Windows 显示先开始共享再手动连接；rclone 进程为 0、P: 不存在，证明没有自动挂载。
- r13 实机从全新状态验证：Android 未开启配对时，Windows 主列表没有 `SM-S9180` 卡片和“已连接”；Android 开启配对后，主列表仍不显示未配对手机，Windows“添加手机”页自动显示 `SM-S9180`；未输入码时“配对”禁用，输入完整 8 位码后启用。未点击“配对”，全程 Windows 配对数为 0、rclone 为 0、P: 不存在。
- 实机验证后已重新卸载并全新安装 Android r13；当前只保留 r13，私有目录没有 PhoneBridge 身份/配对记录。Windows r13 已重新打开，隔离配对数为 0；正式 3 条记录未改变。

## 边界与未验证

- 尚未由用户用 r13 完成手机批准配对、Windows 手动连接、盘符出现，以及两端各一次本地移除和全新重配。
- 本地移除不联系远端，不能同时删除远端自己的旧记录；产品只承诺删除本端设备配对记录、凭据、访问模式、临时端点、活动连接和进程内恢复状态。
- 正式签名 APK、Windows 安装器和正式升级路径未刷新；正式升级仍应保留正式用户数据，与每轮验收候选全新身份是不同语义。

## 唯一下一任务

用户按 `docs/UI_ACCEPTANCE.md` 使用 r13 完成一次全新配对、后共享、手动连接，并分别验证两端本地移除与全新重配；通过前不开始 P2-005 多设备核心。
