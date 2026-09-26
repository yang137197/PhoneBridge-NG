# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-003 UI 复验已通过；以下 r15 是 P2-004 的全新隔离候选。r15 修复手机批准后过早关闭配对结果、导致 Windows 留在添加页并显示“待确认/继续连接”的竞态；其余 r14 功能保持不变。文件位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r15.zip`
- SHA-256：`217163C87D0A8B82A9F43616E1BC0B87AAD40A4618F09571BB6FA120AFCBA3A1`
- 大小：115,427,248 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。
- 隔离数据根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.15`

当前本机已打开 r15 验收窗口。该窗口不读取正式 `%LOCALAPPDATA%\PhoneBridge-NG` 或旧候选数据；隔离配对数为 0、没有 rclone 或候选盘符，正式配对目录不受影响。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r15.apk`
- SHA-256：`20989A33F0149E88342AB97F7AABC7B79A8C44283E9501A49149CDACD254E1E0`
- 大小：4,135,151 bytes
- 包名：`org.phonebridge.ng.uipreviewr15`
- 版本：`0.2.0-ui-preview-r15`，versionCode 2

r15 已用新包名全新安装并打开到 Samsung，并授予验收所需系统权限；私有目录没有 PhoneBridge 身份或配对文件，不继承 r14 的身份、设置或配对记录。旧 r14 候选已卸载。

## 验收顺序

1. Android 保持“共享已停止”。Windows“我的手机”不得出现未配对手机或“已连接”。
2. Android 点击“配对新电脑”，Windows 点击“添加手机”。发现配对广播后应显示手机；完整输入 8 位码后点击“配对”，再在手机批准。
3. 手机批准后，Android 可返回首页或进入系统权限页，但批准结果在原配对窗口期限内必须保持可回读。Windows 应自动确认 Active、完成会话验证并返回应用首页；不得出现“待确认/继续连接”，也不得自动连接或创建盘符。Android 首页的“已配对电脑”应立即保留该电脑，即使共享仍为停止。
4. Android 共享目录应保留原有七个目录，并新增“手机存储”。可选择任一项；选择“手机存储”时 Windows 挂载后应看到系统允许访问的内部共享存储顶层目录。
5. Android 点击“开始共享”，Windows 在设备卡片手动点击“连接”，确认盘符出现。共享中把 Android 界面退到后台，Windows 访问应继续；从 Android“设置 → 退出应用”确认退出后，共享应停止。
6. Android 停止共享，“已配对电脑”仍应显示该电脑，并可进入详情修改访问模式或本地移除。
7. Windows 进入设备设置，修改本地名称和备注并保存；返回首页及重启 r15 后应保持，且不得改变真实设备身份或手机端名称。
8. Android“设置 → 语言”使用返回手势/虚拟返回键应回到设置；设置等一级子页返回首页。首页首次返回提示再次返回，第二次返回在共享中退到后台、未共享时退出。
9. 分别从 Windows 和 Android 各验证一次本地移除：远端无需确认，本端记录与活动连接消失；之后必须按完整流程作为全新设备重新配对。

Android 所有 build type 均允许系统截屏。r15 只用于本轮验收，不覆盖正式包，也不作为以后候选的覆盖安装目标；下一轮必须使用新的 `rN`。
