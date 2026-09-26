# P2-006 UI、多会话接线与语言收口验证

日期：2026-09-26。候选：r21。结论：代码与本轮限定验收通过；真实 Android 前台通知外观未触发，明确保留为未验证。

## 实现

- Windows 语言选择写入当前候选或正式数据根的 `Settings-v1/language.txt`；缺失、损坏或非 `zh-CN`/`en-US` 值均回落简体中文。
- WPF 静态文本改为可通知绑定；切换后当前页面、设备卡片、底部状态和托盘菜单同时刷新。对话框、错误和诊断入口继续在调用时从同一资源目录取值。
- Windows 设备卡片按自身会话的操作/监督状态显示“正在处理”，只禁用本卡操作；仅可安全取消的本设备操作显示本卡“取消”。
- Android 页面继续使用 `AppLanguage` 的持久选择；后台服务和通知每次从同一应用语言包装后的 Context 取字符串，切换时刷新已有前台通知及通知频道名称。

## 自动验证

- `scripts/Verify-Windows.ps1`：locked restore、Release 构建 0 警告/0 错误；最终完整门禁 297/297 通过，其中桌面 57/57，包含语言存储/动态刷新和单卡忙碌/取消隔离断言。
- `scripts/Verify-AndroidApp.ps1`：Debug/Release/AndroidTest 组装、Debug 单元测试及 Debug/Release lint 共 123 个任务成功。
- r21 Android 针对性 `testDebugUnitTest + lintUiPreview + assembleUiPreview` 共 64 个任务成功。
- Windows、zh-CN、en-US 三份资源键一致；Android `values` 与 `values-en` 均为 65 个同名字符串资源。可见硬编码复核只剩产品名、箭头、端口和 URL。

## r21 制品

- Windows ZIP：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r21.zip`
  - SHA-256：`9BE40E1622C3769250B42A1FBFC7C2C10D2173E8B9763097B111819016DCE9A7`
  - 大小：115,438,166 bytes
  - EXE 文件版本：`0.2.0.21`
  - 隔离根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.21`
- Android APK：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r21.apk`
  - SHA-256：`D98D91CA20C23122F8B98AAB5795645241B72573204F7814B1F0E3699EB6C4BA`
  - 大小：4,139,311 bytes
  - 包名：`org.phonebridge.ng.uipreviewr21`

## 实际验收

- Windows 从打包后的 r21 EXE 加 `--ui-preview` 启动，用户确认设置页切到 English 后当前界面和托盘菜单立即更新；持久文件回读为 `en-US`。
- 用户从英文托盘 `Exit` 正常退出；日志记录 `AppStopping`，进程归零。随后再次从同一个 r21 路径启动，用户确认冷启动仍为 English。
- 候选路径、文件版本、日志 `appVersion=0.2.0-ui-preview-r21` 和 `0.2.0.21` 隔离根均已回读；正式数据根及 r20/更早隔离根没有本轮写入。
- Redmi 全新 r21 首次启动由 UI hierarchy 回读为简体中文；用户切到 English 后，应用偏好回读 `language=en-US`，当前页面与 `force-stop → start` 后页面均为英文。
- Samsung 未安装 r21，仍运行 r20；未把旧候选结果误记为 r21 证据。

## 未验证与边界

- Redmi 拒绝 ADB 直接授予通知权限，Samsung r21 安装未完成；为避免扩大到存储权限、电池豁免和真实共享，本轮未触发 r21 前台通知，故真实通知外观未验证。通知语言实现、资源和构建已通过。
- r21 使用全新隔离根，没有重复两台手机配对、双盘符传输或单设备断开实测；P2-005 r20 的真实多会话证据未重复。新增单卡忙碌/取消隔离由针对性模型测试覆盖。
- 未重复大文件、睡眠、重启、升级、正式签名或安装器测试；这些不属于 P2-006。
