# PhoneBridge NG v0.2.0 UI 验收入口

状态：P2-003 UI 复验已通过；以下 r14 是 P2-004 的全新隔离候选，加入本轮确认的配对后导航、配对记录持久显示、共享目录、返回/退出语义和 Windows 本地名称/备注。文件位于 Git 忽略的 `.audit/ui-acceptance/`，不是正式发布包。

## Windows

- 文件：`.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r14.zip`
- SHA-256：`49071B242838B773B52E11408B3239553D4E1610BC9F21F5D9A7E3066A98F846`
- 大小：115,427,254 bytes
- 验收启动：解压后运行 `PhoneBridge.Desktop.exe --ui-preview`。
- 隔离数据根：`%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.14`

当前本机已打开 r14 验收窗口。该窗口不读取正式 `%LOCALAPPDATA%\PhoneBridge-NG` 或旧候选数据；隔离配对数为 0，正式配对目录未改动。

## Android

- 文件：`.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r14.apk`
- SHA-256：`BA98F337E772AD6BAB3956018921822E3695B0B75EE561FF899A315A0E1A59BD`
- 大小：4,134,327 bytes
- 包名：`org.phonebridge.ng.uipreviewr14`
- 版本：`0.2.0-ui-preview-r14`，versionCode 2

r14 已在 Samsung 实机定向验证后清除应用数据并重新授权必要系统权限。准备结束时首页显示“共享已停止”和“尚无已配对电脑”，未保留 PhoneBridge 身份或配对记录。

## 验收顺序

1. Android 保持“共享已停止”。Windows“我的手机”不得出现未配对手机或“已连接”。
2. Android 点击“配对新电脑”，Windows 点击“添加手机”。发现配对广播后应显示手机；完整输入 8 位码后点击“配对”，再在手机批准。
3. 配对成功后 Windows 应自动返回应用首页，但不得自动连接或创建盘符；Android 首页的“已配对电脑”应立即保留该电脑，即使共享仍为停止。
4. Android 共享目录应保留原有七个目录，并新增“手机存储”。可选择任一项；选择“手机存储”时 Windows 挂载后应看到系统允许访问的内部共享存储顶层目录。
5. Android 点击“开始共享”，Windows 在设备卡片手动点击“连接”，确认盘符出现。共享中把 Android 界面退到后台，Windows 访问应继续；从 Android“设置 → 退出应用”确认退出后，共享应停止。
6. Android 停止共享，“已配对电脑”仍应显示该电脑，并可进入详情修改访问模式或本地移除。
7. Windows 进入设备设置，修改本地名称和备注并保存；返回首页及重启 r14 后应保持，且不得改变真实设备身份或手机端名称。
8. Android“设置 → 语言”使用返回手势/虚拟返回键应回到设置；设置等一级子页返回首页。首页首次返回提示再次返回，第二次返回在共享中退到后台、未共享时退出。
9. 分别从 Windows 和 Android 各验证一次本地移除：远端无需确认，本端记录与活动连接消失；之后必须按完整流程作为全新设备重新配对。

Android 所有 build type 均允许系统截屏。r14 只用于本轮验收，不覆盖正式包，也不作为以后候选的覆盖安装目标；下一轮必须使用新的 `rN`。
