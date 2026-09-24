# P2-003 — Windows / Android UI 验收应用

## 目标

把 P2-002 已冻结的 R1 视觉和页面层级落到原生 WPF 与 Android 控件中，生成隔离的 Windows / Android 本地验收应用，供用户检查实际渲染效果。

## 范围

- Windows：设备主页、添加手机、设备设置、常规设置、高级排障、关于页的实际 WPF 布局。
- Android：首页、配对页、电脑详情、设置页的实际原生布局。
- 保留现有单设备业务链路；仅为现有操作重新分层和命名。
- 生成不替换正式交付目录的本地 UI 验收制品。

## 不做什么

- 不开始按 `device_id` 隔离的多会话重构。
- 不宣称双设备、完整语言切换、无障碍、DPI 或真机 UI 已通过。
- 不覆盖或卸载当前正式 Android 应用，不发布 Release。
- 不把本地验收图标误称为已完成的安装器、完整托盘状态或正式签名交付资产。

## 涉及文件

- `windows/src/PhoneBridge.Desktop/`：WPF 视觉资源、主窗口布局及页面导航接线。
- `android/app/src/main/`：Android 原生页面布局与资源。
- `docs/`：任务状态、构建证据、当前未验证项和下一任务。
- `.audit/ui-acceptance/`：被 Git 忽略的本地验收制品。

## 实现

- Windows 已按 r1 实现设备主页、添加手机、设备设置、常规设置、高级排障和关于页；既有单会话连接、挂载和安全操作入口保持不变。
- Windows 默认显示简体中文；`--ui-preview` 只使用独立单实例互斥体，允许预览窗口与安装版同时显示，但两者仍共用当前用户数据，不能同时执行连接或挂载。
- Android 已按 r1 实现首页、配对页、电脑详情和设置页；验收 build type 使用 `org.phonebridge.ng.uipreview`，不覆盖正式 `org.phonebridge.ng`。
- Android 默认简体中文，设置页可切换 English 并持久保存；正式分享、配对、撤销和访问模式逻辑继续复用。
- 按用户首轮反馈，A“桥接文件”已接入 Windows EXE/窗口和 Android adaptive/monochrome 启动器资源；Android 设置与返回改为 48dp 图标按钮，设置改为独立菜单并由语言项进入子页。
- Android 仅 `uiPreview` 允许截图；正式 Debug/Release 继续设置 `FLAG_SECURE`。
- 本地候选位于 `.audit/ui-acceptance/`，不属于正式 v0.1.0 交付目录。

## 测试

- Windows 完整 `scripts/Verify-Windows.ps1`：Release 构建 0 警告/0 错误，280/280 通过；最终样式修正后 Desktop 51/51 通过。
- Windows 六个既定页面均由实际 WPF 窗口打开并以 `PrintWindow` 回读，未触发连接、挂载或危险操作。
- Android `scripts/Verify-AndroidApp.ps1`：123 个任务执行成功，Debug/Release、测试 APK、单元测试和 Debug/Release lint 通过。
- Android `assembleUiPreview lintUiPreview` 通过；Samsung 上以独立包名安装后，中文首页、中文/英文设置页、切回中文及冷启动保持均由 UI hierarchy 回读确认；最终包列表未检测到正式 `org.phonebridge.ng`，本轮不宣称当前并存。
- r2 在 Samsung 上实际截图首页、设置菜单和语言子页；启动器 A 图标已回读，设置/返回按钮的真机边界均为 135×135 px（48dp）。

## 验收结果

r2 候选与验收入口已准备完成，任务继续保持 active，等待用户复验实际视觉。

当前未验证：

- 用户尚未复验 r2 的 A 图标、Android 图标尺寸和设置菜单层级。
- Android 真机尚未进入配对会话和已有电脑详情两种状态；没有为截图启动共享或改动正式包数据。
- Windows 语言切换尚未接线；托盘、通知、对话框和全部错误文案尚未验证随语言更新。
- 正式 Android build 的截图阻止仍保留；无障碍、字体放大、横屏和不同 DPI 未验证。
- Samsung 启动器/应用信息当前未显示验收名称后缀；包内标题正确，未通过卸载验收包或清空桌面数据验证是否为系统缓存。
- 当前仍是单会话业务核心；多设备同时挂载未实现。Windows 首页出现多个发现/保存记录时可能显示同名卡片，本任务不把它作为多设备结果。

## 唯一下一任务

由用户复验已打开的 Windows r2 验收窗口和 Samsung 上的“PhoneBridge NG · UI 验收”，确认 A 图标、Android 图标尺寸和设置菜单层级，或给出限定调整项；未确认前不开始 P2-004 多设备核心。
