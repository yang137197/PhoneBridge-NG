# P2-003 UI 验收候选记录

日期：2026-09-24。状态：r2 候选已按用户首轮反馈更新，等待复验；本记录不是任务完成声明。

## 制品

| 平台 | 本地文件 | SHA-256 | 边界 |
| --- | --- | --- | --- |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r6.zip` | `299BAB6469021FDB1E1364F756917DEFBE8E39718D066ACB2178C186A7C7EAA9` | framework-dependent 本地验收包；`--ui-preview` 只隔离互斥体，不隔离用户数据 |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview.apk` | `126C27759F345C27A94B9D4D0026B46CA7B2334AF2731A8E81C2BEB0F081DBA7` | `org.phonebridge.ng.uipreview`，本地调试签名，与正式包并存，不可作为正式升级包 |

## 已验证事实

- Windows 完整验证为 Release 0 警告/0 错误、280/280 测试通过；最终 XAML 样式修正后 Desktop 51/51 通过。
- Windows 实际窗口依次打开并回读设备首页、添加手机、设备设置、常规设置、高级排障和关于六页。蓝色主按钮使用白字，禁用状态和危险按钮均按冻结变量呈现；未点击连接、挂载、删除、撤销或导出。
- Windows 已把 A“桥接文件”导出为多尺寸 ICO 和 PNG，并用于 EXE、窗口及页内品牌图标；发布后的 EXE 图标和实际窗口均已回读。
- Android 正式验证脚本共 123 个 Gradle 任务执行成功，覆盖 Debug/Release、测试 APK、单元测试和 Debug/Release lint；`uiPreview` assemble/lint 另行通过。
- Samsung `R5CW429DKDN` 已同时保留正式 `org.phonebridge.ng` v0.1.0 和验收 `org.phonebridge.ng.uipreview` v0.2.0-ui-preview。启动器已显示 A 图标；首页、设置菜单和语言子页均已截图与 UI hierarchy 回读。
- Android 设置和返回均使用 48dp 图标按钮；真机层级边界为 135×135 px。设置页已改为可扩展菜单，语言选择位于独立子页。
- `uiPreview` 资源明确允许截图，便于本地视觉验收；正式 Debug/Release 资源仍禁止截图并继续设置 `FLAG_SECURE`，没有放宽正式应用安全边界。

## 未验证

- 用户尚未复验 r2 实际视觉；因此 P2-003 保持 active。
- Android 配对会话和已有电脑详情没有在真实状态下打开；正式共享和配对数据未为视觉截图而改变。
- Windows English 切换尚未接线；Android 通知/前台服务运行期间的即时语言刷新、两端全量错误/对话框/托盘文案、无障碍、字体放大、横屏、高对比度和不同 DPI 未验证。
- 多设备核心没有开始；多个设备卡片仍由现有发现/保存记录聚合产生，不能证明双设备并行连接。

## 唯一下一任务

用户复验 Windows r2 验收窗口和 Samsung 上的验收应用，确认 A 图标、Android 图标尺寸与设置菜单层级，或列出限定调整项。确认前不开始 P2-004 多设备核心。
