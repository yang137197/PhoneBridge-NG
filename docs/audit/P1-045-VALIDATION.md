# P1-045 Windows 正式安装器元数据验收

日期：2026-09-23。结果：通过。

## 已确认

- Inno Setup `AppVerName` 已从 `PhoneBridge NG 0.1.0 local preview` 改为 `PhoneBridge NG 0.1.0`；`VersionInfoDescription` 已从 `PhoneBridge NG local installer` 改为 `PhoneBridge NG installer`。
- 安装器 AppId、版本、按用户目录、依赖、WinFsp 检测/UAC 分支、卸载和自启动逻辑均未修改。
- 候选复用 P1-041 已验证 Windows publish 目录、固定 WinFsp 2.1.25156 MSI 和 Inno Setup 7.1.0；正式 Android APK未重建。
- 新安装器大小 81,083,725 字节，SHA-256 为 `02B63BE25590FD1F86BC661CF15B7A3941E2FE13440920AAA0F850B4E7961262`，文件描述为 `PhoneBridge NG installer`，产品名/版本为 `PhoneBridge NG` / `0.1.0`。
- 当前 Windows 11 专业版在已有 WinFsp 的条件下执行同版本静默覆盖安装，退出码为 0；三份 DPAPI 配对记录的集合散列前后一致。
- 安装期间及完成后没有启动 PhoneBridge 客户端或 rclone，没有创建 P:。
- 新安装器已提升到默认交付；manifest、SHA256SUMS、正式 APK、README 和重新生成的对应源码包一致。当前源码包 SHA-256 为 `6AD28364C763BD33D3F7499B2DEDE145C4450A15656579EBB1E40271004BCCD0`。

机器可读证据：`.audit/runs/P1-045/formal-installer.json`、`candidate.json` 及安装日志 `upgrade.log`；默认交付复核由 `.audit/runs/P1-044/source-delivery.json` 记录。

## 执行偏差与修正

首次内联编译命令因同时含计算路径和删除旧候选而被本机命令审核拒绝，未执行。改为固定路径和边界校验脚本后启动编译；工具调用在 30 秒观察窗口结束时，Inno Setup 压缩进程仍在运行，候选文件尚不完整。继续等待同一进程退出后，最终文件大小、SHA-256、版本资源和实际安装均独立通过；没有重启第二次编译。

## 未验证与边界

- 当前机已有 WinFsp，因此本任务没有触发内置 MSI和 UAC；该分支转入干净 Windows Sandbox 验收。
- Windows 安装器仍为 `NotSigned`。没有受信任 Authenticode 证书时继续如实显示未知发布者，不使用自签证书伪装信任。
- 本任务只改安装器元数据，没有重复手机链路或产品回归测试。
