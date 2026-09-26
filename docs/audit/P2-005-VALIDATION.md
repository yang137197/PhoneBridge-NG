# P2-005 按设备隔离的多会话验证

日期：2026-09-26。状态：已完成。Samsung SM-S9180 与 Redmi K40 已在同一 Windows r20 隔离候选中同时挂载两个盘符，分别完成小文件双向复制；停止 Redmi 后 Samsung 会话继续浏览和传输。

## 制品

| 平台 | 本地文件 | SHA-256 | 大小 |
| --- | --- | --- | ---: |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r20.zip` | `0728748900DE20C0FAC22EB06DC7DFCE16F4EB9EA8D3033D66309A7DCE529A31` | 115,435,951 bytes |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview-r20.apk` | `004D5C39A672727B3CE61B6F4D9379918781CA1F636D3803079BD0D7B4978F0A` | 4,139,107 bytes |

两者均为本地验收候选，不是正式发布包。Windows 程序版本为 `0.2.0.20`，Android 包名为 `org.phonebridge.ng.uipreviewr20`。最终实测 Windows 进程使用 `--ui-preview` 和隔离根 `%LOCALAPPDATA%\PhoneBridge-NG-UiPreview\0.2.0.20`。

## 实现

- `ConnectionClient` 构造时绑定一个非空 `device_id`，所有配对、连接、会话检查、删除确认、备注更新和本地移除在读取凭据前校验设备一致性；每个实例继续独占一个既有 `ReadOnlyMountManager`。
- `DeviceSession` 独立持有连接客户端、挂载、盘符预留、当前操作取消令牌、健康监督、重连退避和进程内日志序号；`DeviceSessionCoordinator` 只管理会话集合、盘符冲突和全会话安全停止。
- WPF 设备卡片、连接、断开、取消、打开文件、健康检查和自动重连均按所选设备会话执行。托盘只聚合全部会话状态；诊断导出仍是全局短操作。
- 每设备使用独立会话临时根、rclone 子进程和随机 loopback RC 端口。既有 `VfsCache-v1/<证书 SHA-256>` 根保持不变，避免升级后迁移或遗失可恢复的脏缓存。
- 诊断日志 schema 升为 2，只增加进程内正整数 `session`；不写入设备 ID、设备名、地址、盘符、路径或凭据。

## 自动验证

- `scripts/Verify-Windows.ps1 -ResultsDirectory .audit/p2-005/windows-final`：locked restore 成功，Release 构建 0 警告/0 错误，294/294 测试通过。
- 分项结果：Desktop 54、Discovery 54、Mounting 45、Pairing 50、Connection 25、Credentials 66。
- 新增测试覆盖两个会话的操作取消与重连状态隔离、同盘符冲突在第二个 rclone 前失败、每设备会话目录隔离、跨设备客户端在访问凭据前拒绝、一个会话操作失败时退出仍取消并停止其余会话，以及独立会话目录不改变既有身份缓存根。
- r20 `assembleUiPreview lintUiPreview` 共 39 个任务成功；同一 APK 已安装到 Samsung 与 Redmi。

## 真实双设备最小链路

- 用户截图确认 Windows 同时显示 `M2012K11AC` 与 `SM-S9180` 为“已连接/安全模式”，对应 P: 与 E:；资源管理器同时显示两个 PhoneBridge 网络盘。
- 系统回读确认 P: 使用 Redmi `192.168.100.197`、独立 rclone 和 RC `127.0.0.1:14191`；E: 使用 Samsung `192.168.100.238`、另一 rclone 和 RC `127.0.0.1:14197`。两个配置路径均位于 r20 隔离根下不同的设备哈希目录。
- Redmi P: 的 66-byte 文件从本机写入、从盘符读取并回读本机，三处 SHA-256 均为 `347DA09F5C03C585F083F5EACF0C63F09F3CF124FF92336E252C5EF29F87ABB3`。
- Samsung E: 的 68-byte 文件完成相同双向路径，三处 SHA-256 均为 `CB765EC383B9C95600688D2561447B8AC4C31913816D2C6E5466E9A707D641B8`。
- 通过 ADB 停止 Redmi r20 后等待 22 秒，P: 的 rclone 已退出；Samsung E: 和其 rclone 保持。E: 仍能列目录，并再次完成本机写入、盘符读取和回读本机，三处 SHA-256 均为 `E40E337B50CFC479F7DC5108E2AA515EF71CFE0B626F66B96FCE83033B2B4C63`。
- r20 隔离日志共 121 条，全部为 schema 2；设备会话只显示序号 1、2。对两台设备名称、型号、地址和盘符模式的扫描为 0 命中。
- 用户从托盘正常退出 r20 后回读：`PhoneBridge.Desktop` 为 0、rclone 为 0，P:/E: 均不再存在；隔离根保留两条配对记录，正式配对目录保持 0 条。

## 操作偏差与边界

- 首次实测误用了不带 `--ui-preview` 的开发启动脚本，写入正式 `%LOCALAPPDATA%\PhoneBridge-NG`。发现后停止并清理该轮新配对；清理回读显示原有 3 条正式配对记录也已不存在，且未找到可恢复副本。最终全部通过证据已在正确的 r20 隔离根重新执行；旧正式配对如仍需要只能重新配对。
- ADB 清理测试标记时设备连接关闭，未能验证或删除手机端测试文本；这些文件名均以 `PhoneBridge-P2-005` 开头，不影响产品状态，可由用户直接删除。
- 本任务没有重复大文件、睡眠、重启、升级、P2-004 记录管理或设备备注流程。正式签名 APK、Windows 安装器和正式升级仍未刷新。

## 唯一下一任务

开始 P2-006：按既定设计收口 Windows/Android UI、多会话接线和完整中英文覆盖；不在本任务开始 P2-007 正式图标与交付刷新。
