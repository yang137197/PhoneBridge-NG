# P2-004 本地移除、截屏与全新配对流程验证

日期：2026-09-24。状态：配对按钮时序缺陷已复现并修复，等待用户完成 r12 真实端到端验收。

## 制品

| 平台 | 本地文件 | SHA-256 | 大小 |
| --- | --- | --- | ---: |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r12.zip` | `3E235833244C6E7BE3F3FC4E503947986474638C033F0FC99B6C220A091EBFC3` | 115,424,379 bytes |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r12.apk` | `561B9A6E661B26F668AEEE80E3D35F2B1D800CB75926AA34906D6B9584D68598` | 4,130,899 bytes |

两者均为本地验收候选，不是正式发布包。Windows 程序版本为 `0.2.0.12`，Android 包名为 `org.phonebridge.ng.uipreviewr12`、版本为 `0.2.0-ui-preview-r12`。

## 根因

- Windows 旧 `--ui-preview` 只隔离单实例互斥体，仍读取正式配对目录；本机正式目录已有 3 条记录，导致已配对候选的“配对”按钮按既有逻辑禁用。
- Android 旧 `uiPreview` 固定包名并用 `adb install -r` 覆盖，保留上轮数据；同时“配对新电脑”被硬性依赖于共享引擎已启动。
- 因此开始共享后，Windows 会沿已保存身份进入连接路径，造成“无法配对新电脑但自动连接”的表象。以上均由源码、目录内容和实际候选状态确认，不是推测。
- r10 复验又确认独立的 UI 时序缺陷：用户先打开“添加手机”时没有候选，Android 配对广播随后到达；Windows 日志记录候选已添加，但 `RebuildRows` 只尝试恢复原选中 ID，原值为空时仍保持无选中项。页面没有另一个候选选择控件，因此标题持续为“请选择附近手机”，即使 8 位码完整也无法启用按钮。

## 已验证事实

- Windows `scripts/Verify-Windows.ps1`：Release 构建 0 警告/0 错误，283/283 测试通过。新增测试覆盖未共享时完成配对但不挂载，以及不同预览修订使用不同的非正式数据根。
- Android `scripts/Verify-AndroidApp.ps1`：Debug/Release、测试 APK、单元测试和两套 lint 共 123 个任务成功；r12 `assembleUiPreview lintUiPreview` 39 个任务成功。
- Samsung 隔离 instrumentation：停止共享时可完成配对与授权，session 为 `share_ready=false`、文件请求为 409；共享门禁打开后 session 为 true 且文件可读，1/1 通过。此前本地删除/旧 token/新配对两条测试 2/2 通过。
- r11 定向实机复验从“添加手机”先打开且显示“请选择附近手机”开始；Android 后开启配对后，Windows 自动变为 `SM-S9180`，输入 8 位码后 UI Automation 回读“配对”按钮 `IsEnabled=true`。实际点击后配对完成，Windows 显示先开始共享再手动连接；rclone 进程为 0、P: 不存在，证明没有自动挂载。
- r12 作为最终全新候选部署后，Samsung 私有目录没有 PhoneBridge 身份/配对记录；Windows `0.2.0.12` 隔离配对数为 0。当前只保留 Android r12，Windows r12 窗口已打开；正式 3 条记录未改变。

## 边界与未验证

- 尚未由用户用 r12 完成 Windows 手动连接、盘符出现，以及两端各一次本地移除和全新重配。
- 本地移除不联系远端，不能同时删除远端自己的旧记录；产品只承诺删除本端设备配对记录、凭据、访问模式、临时端点、活动连接和进程内恢复状态。
- 正式签名 APK、Windows 安装器和正式升级路径未刷新；正式升级仍应保留正式用户数据，与每轮验收候选全新身份是不同语义。

## 唯一下一任务

用户按 `docs/UI_ACCEPTANCE.md` 使用 r12 完成一次全新配对、后共享、手动连接，并分别验证两端本地移除与全新重配；通过前不开始 P2-005 多设备核心。
