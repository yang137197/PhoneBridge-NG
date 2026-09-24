# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-003 候选已生成，等待用户确认。以下文件都在 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r5.zip`
- SHA-256：`1E3A3A130EE2EE1844314BF191EBDFBB58B5445ADC4579D111EF289CC0DC6955`
- 大小：39,475,746 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。

当前本机已打开验收窗口。依次检查“设备”“添加手机”“设备设置”“设置 → 常规”“设置 → 高级排障”“关于”。`--ui-preview` 只隔离单实例互斥体，不隔离配对和设置数据；安装版仍运行时只做页面浏览，不要在两边同时连接或挂载。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview.apk`
- SHA-256：`21D96725C0C228F09CC7A9778B7F296DF032036B6684D2D95162359DC7CABC06`
- 大小：4,187,753 bytes
- 包名：`org.phonebridge.ng.uipreview`
- 显示名：`PhoneBridge NG · UI 验收`
- 版本：`0.2.0-ui-preview`，versionCode 2

验收包已在 Samsung 上与正式 `org.phonebridge.ng` v0.1.0 并行安装，当前停留在简体中文首页。检查首页和设置页，可切换 English 后再切回简体中文。该包使用本地调试签名且有独立数据，不是升级包；只做 UI 验收时不要与正式应用同时开始共享。

## 反馈范围

请只确认或指出以下实际视觉问题：信息层级、留白与密度、按钮命名与主次、字号、颜色、卡片、导航和中英文页面观感。功能链路、多设备、正式图标和交付签名不在本次视觉确认范围内。
