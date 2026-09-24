# Windows 发现、只读挂载、配对与凭据模块

P1-001 已建立 [.NET solution](PhoneBridge.Windows.slnx)，包括：

- `PhoneBridge.Discovery`：未认证候选、IPv4/IPv6 端点、TXT 校验、去重与撤销。
- `PhoneBridge.Discovery.Windows`：Windows DNS-SD DeviceWatcher、网络变化重建、12 秒扫描与两轮缺失移除、异步停止。
- `PhoneBridge.Discovery.Diagnostics`：简体中文默认/en-US 资源化诊断命令；只列候选，不配对或挂载。
- `PhoneBridge.Discovery.Tests`：41 项行为测试。
- `PhoneBridge.Mounting`：明确身份输入、固定 rclone 校验、只读盘符、创建时 Job 绑定、生命周期与失败清理。
- `PhoneBridge.Mounting.Diagnostics`：限时 JSON 实验入口，凭据仅经 stdin；不负责正式配对或持久保存。
- `PhoneBridge.Mounting.Tests`：33 项生命周期/输入/真实子进程测试；`PhoneBridge.ProcessFixture` 仅供测试 argv 往返，不进入产品依赖。
- `PhoneBridge.Credentials`：当前用户 DPAPI 整条记录、严格 CA、私有 ACL、原子提交/恢复、状态及修订号；[API 边界](src/PhoneBridge.Credentials/README.md)。CredentialsFixture 仅供本机合成测试。
- `PhoneBridge.Pairing`：固定 8 帧、J-PAKE/HKDF 与异常终止；由 `PhoneBridge.Connection` 接入网络授权与受保护记录。50 项协议测试与 Kotlin 互操作，PairingFixture 仅供管道测试。

`PhoneBridge.Connection` 与 `PhoneBridge.Desktop` 已实现 WPF 首次配对、手机确认的只读/安全/完全读写挂载、安全模式写入/确认删除、自动重连、托盘、自启动、本地脱敏诊断包和手动地址排障入口。开发预览通过根目录 `./scripts/Start-WindowsPreview.ps1` 构建并打开；当前默认交付提供单一入口的 Windows 安装包，包含自包含 .NET、固定 rclone，并在缺少 WinFsp 时运行已核验的官方 MSI。当前只允许一个活动手机挂载，尚无持久盘符偏好。日志与实际导出证据见 [P1-014](../docs/audit/P1-014-VALIDATION.md)，安装边界见 [交付说明](../docs/LOCAL_DELIVERY.md)。

P1-002 已完成备用机只读挂载/拒绝写入、身份与密码拒绝、正常卸载、宿主崩溃清理和跨进程盘符占用验证，见 [挂载验收](../docs/audit/P1-002-VALIDATION.md)。复现入口在 [windows_mount](../tests/integration/windows_mount/README.md)。只读诊断最多保持 120 秒；不是日常保持盘符的用户入口。P1-004 至 P1-024 的完成记录保留协议、存储、真机连接、三种模式、安全写入、生命周期、大文件、媒体 Seek、文件名边界和本地安装交付证据。安全模式使用直写以保持服务端“不覆盖已有文件”的最终结果，完全读写模式保留可恢复写缓存。

## 构建与验证

在项目根目录运行 `./scripts/Verify-Windows.ps1`。脚本使用本机已验证的 `.audit/tools/dotnet-10.0.401/dotnet.exe`，或通过 `-DotnetPath` 指定 SDK 的 dotnet.exe；无本地工具时使用 PATH。要求 `global.json` 锁定的 10.0.401，进行 locked restore、Release build、测试并保存 TRX。脚本只对当前子进程设置工具遥测退出选项，不安装驱动或连接手机。

SDK 官方 zip 与 SHA-512 见 [sdk.json](../docs/audit/p1-001/sdk.json)；工具安装在忽略目录，未修改本机全局 SDK。依赖/许可见 [DEPENDENCIES](DEPENDENCIES.md)。

诊断入口（项目根目录 PowerShell）：

```powershell
& ./.audit/tools/dotnet-10.0.401/dotnet.exe ./windows/src/PhoneBridge.Discovery.Diagnostics/bin/Release/net10.0-windows10.0.19041.0/PhoneBridge.Discovery.Diagnostics.dll --seconds 60
```

可加 `--json` 输出结构化事件，`--en-US` 使用英文，`--manual 192.168.1.10 8273` 添加临时候选。手动输入仅接受 IP 与端口，不接收密码、域名、URL 或文件路径；退出清空。诊断可按 Ctrl+C 停止，最多运行 300 秒。

候选 ID 只区分服务实例或手动端点；mDNS 指纹/名称不能建立信任，所有候选 `IsAuthenticated=false`。每 12 秒重新枚举，两轮没有有效记录时移除。实测停止共享后的撤销为 24.000 和 35.937 秒，不能据此承诺所有网络固定离线时限。网络切换/睡眠/真实 DHCP 尚待后续实机验收。
