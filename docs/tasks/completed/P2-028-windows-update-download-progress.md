# P2-028 — Windows 更新下载进度反馈

状态：已完成。日期：2026-09-30。

## 目标

修复 Windows 客户端确认下载新版本后只有静态“正在下载”文字、没有可见进度的问题；显示目标版本、实时百分比和确定型进度条，同时保持现有 Release 校验、大小上限、SHA-256 和安装确认流程不变。

## 范围

- 下载服务按已验证的 Release 资产大小回报已接收字节、总字节和百分比。
- Windows 左侧更新区域在下载期间显示目标版本、进度条和实时百分比；下载结束、失败或退出时关闭进度显示。
- 已校验缓存命中时直接回报 100%；异常下载不残留临时安装器。
- 补充定向自动测试并运行完整 Windows 门禁。

## 不做什么

- 不修改 GitHub Release 来源、更新检查策略、安装器校验或系统安装确认流程。
- 不实现后台下载、断点续传、通知下载或静默安装。
- 不在本任务开发 Windows/Android 首次使用引导。
- 不安装候选、不推送、不打标签、不创建 GitHub Release。

## 涉及文件

- `windows/src/PhoneBridge.Desktop/UpdateService.cs`
- `windows/src/PhoneBridge.Desktop/MainWindow.xaml`
- `windows/src/PhoneBridge.Desktop/MainWindow.xaml.cs`
- Windows 中英文资源
- `windows/tests/PhoneBridge.Desktop.Tests/UpdateServiceTests.cs`
- `windows/tests/PhoneBridge.Desktop.Tests/LocalizationTests.cs`

## 实现

- `UpdateService.DownloadAsync` 新增可选进度接收器，以已通过 Release 元数据校验的资产大小为总量，在开始时回报 0%，并在百分比变化时回报已接收字节、总字节和 0–100 的确定型百分比；避免每个 128 KiB 数据块都重复刷新相同百分比。
- 命中已存在且大小、SHA-256 均通过校验的安装器时直接回报 100%，不发起重复下载。
- Windows 左侧版本/检查更新区域增加下载进度面板，持续显示“正在下载版本 x：y%”及水平进度条；底栏同步显示相同信息。
- 下载完成并通过完整性校验后沿用原安装确认；失败、取消或操作结束时隐藏并清空进度面板。语言切换时正在显示的进度文字会同步刷新。
- 原有官方 GitHub URL、正式 Release、固定安装器名、大小上限、Content-Length、实际字节数、SHA-256、临时文件清理及启动前再次校验均未修改。

## 测试

- 通过：Desktop 定向 8/8；覆盖首次 0%、至少一个中间百分比、最终 100%、字节单调递增、已校验缓存仅回报 100%，以及中英文进度资源。
- 通过：`scripts/Verify-Windows.ps1`；Release 构建 0 警告、0 错误，Windows 335/335 测试通过。TRX 位于 `.audit/windows-verification/20260930-104145/`。
- 未运行：真实新版本安装器下载时的安装版视觉验收。公开 Latest 仍为当前正式 `v0.2.5`，本任务没有伪造 Release 或放宽更新安全校验来触发该路径。

## 验收结果

自动验证范围通过。Windows 更新下载已具备真实字节驱动的目标版本、百分比和确定型进度条，完整性与系统安装确认边界不变。真实新 Release 的下载显示和后续安装仍须在正式候选验收中验证，不能由当前同版检查或 mock 测试代替。
