# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-003 r2 候选已按首轮反馈更新，等待用户复验。以下文件都在 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r6.zip`
- SHA-256：`299BAB6469021FDB1E1364F756917DEFBE8E39718D066ACB2178C186A7C7EAA9`
- 大小：39,561,337 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。

当前本机已打开 r2 验收窗口。A“桥接文件”已用于 EXE、窗口和页内品牌图标。依次检查“设备”“添加手机”“设备设置”“设置 → 常规”“设置 → 高级排障”“关于”。`--ui-preview` 只隔离单实例互斥体，不隔离配对和设置数据；安装版仍运行时只做页面浏览，不要在两边同时连接或挂载。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview.apk`
- SHA-256：`126C27759F345C27A94B9D4D0026B46CA7B2334AF2731A8E81C2BEB0F081DBA7`
- 大小：4,190,058 bytes
- 包名：`org.phonebridge.ng.uipreview`
- 显示名：`PhoneBridge NG · UI 验收`
- 版本：`0.2.0-ui-preview`，versionCode 2

验收包已在 Samsung 上与正式 `org.phonebridge.ng` v0.1.0 并行安装。A“桥接文件”已用于启动器图标；设置与返回采用 48dp 图标按钮；“设置”现在是独立菜单，语言位于其子页。`uiPreview` 专用于视觉验收并允许截图，正式 Debug/Release 仍保留 `FLAG_SECURE`。该包使用本地调试签名且有独立数据，不是升级包；只做 UI 验收时不要与正式应用同时开始共享。

## 反馈范围

请复验 A 图标、Android 设置/返回图标尺寸、设置菜单与语言子页，并继续指出信息层级、留白与密度、按钮命名与主次、字号、颜色、卡片、导航和中英文页面观感。功能链路、多设备、安装器/完整托盘状态图标和交付签名不在本次视觉确认范围内。
