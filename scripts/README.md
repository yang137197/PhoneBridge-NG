# 工具脚本

[Verify-Windows.ps1](Verify-Windows.ps1) 验证固定 SDK、locked restore、Release 编译与 Windows 行为测试。可指定 DotnetPath / ResultsDirectory；不下载、提权或连接手机。已执行的隔离设备实验脚本放在 [tests/integration/upstream_chain](../tests/integration/upstream_chain/README.md)，不能当作正式安装入口。

[Build-LocalDelivery.ps1](Build-LocalDelivery.ps1) 生成本地交付物。脚本固定并核验 .NET 10.0.401、Gradle 9.3.1、rclone 1.75.1、WinFsp 2.1.25156 和 Inno Setup 7.1.0，执行 locked runtime restore、自包含 Windows 发布、Android Release 构建、zipalign、签名及安装器编译，输出安装程序、APK、SHA-256 与 manifest 到 `.audit/delivery/output`。默认 `LocalTest` 使用隔离 Debug 证书；`ThirdPartyRelease` 只接受 [Initialize-AndroidReleaseSigning.ps1](Initialize-AndroidReleaseSigning.ps1) 生成的项目外身份记录，并绑定 keystore 与证书。脚本不下载或安装依赖、不连接设备、不发布，使用边界见 [本地交付说明](../docs/LOCAL_DELIVERY.md)。

[New-SourceDelivery.ps1](New-SourceDelivery.ps1) 在正式二进制构建和验收后生成 `PhoneBridge-NG-0.1.0-source.zip`，排除 `.audit`、版本库元数据、构建缓存和私钥文件类型，写入并回读逐文件散列清单，再通过同卷暂存把源码包加入默认 manifest 与 SHA256SUMS。它不读取签名身份、不重新构建二进制、不联网或发布；每次新的正式二进制构建后都必须重新运行。

[Initialize-AndroidReleaseSigning.ps1](Initialize-AndroidReleaseSigning.ps1) 一次性生成项目外的 Android 长期 PKCS12 身份、独立路径备份和无秘密身份记录。它拒绝覆盖已有文件，正式执行前必须先选择独立受保护备份介质。

[Test-CleanWindowsInstall.ps1](Test-CleanWindowsInstall.ps1) 为没有 WinFsp/PhoneBridge 的 Windows 11 x64 测试机提供首装预检和显式安装验收；当前日常电脑已有 WinFsp 时会失败关闭，不应卸载驱动伪造测试环境。默认拒绝提升会话；Windows Sandbox 固定 `WDAGUtilityAccount` 只能由隔离验收生成器显式放行。

[New-CleanWindowsSandboxAcceptance.ps1](New-CleanWindowsSandboxAcceptance.ps1) 为 Windows 11 专业版的 Windows Sandbox 生成一次性干净首装配置。Sandbox 禁用网络，只读映射正式交付和验收脚本，只允许独立证据目录回写；启动后自动运行 `Test-CleanWindowsInstall.ps1` 的预检和安装核对。该脚本不启用系统功能，只有显式 `-Launch` 才打开已经可用的 Sandbox。

[Verify-AndroidCredentials.ps1](Verify-AndroidCredentials.ps1) 验证 Android 配对存储的 Debug/Release AAR、测试 APK 与两套 Lint；固定 Gradle/Android 工具，严格依赖锁与 SHA-256 校验，失败即停止。可指定 GradlePath/JdkHome/AndroidSdk，恢复调用前环境变量；不下载 SDK、不启动或安装设备。原生测试须另行使用[专用模拟器入口](../tests/integration/android_credentials/README.md)。

后续脚本按具体任务添加，注明用途、前置条件、影响、输入输出、失败/停止条件及实际验证方式；不能隐藏下载、提权、驱动安装或真实文件删除。

[Start-WindowsPreview.ps1](Start-WindowsPreview.ps1) 构建并打开 P1-008 WPF 开发预览。默认使用项目隔离工具，也可传入 DotnetPath/RclonePath；核验固定 rclone SHA-256，locked build 后将其复制到开发输出的 tools 目录。`-NoLaunch` 只准备输出。需要已有 WinFsp，脚本不下载或安装依赖；不会更改系统环境，重建前应关闭已有预览。它不是发布/安装脚本。

[Verify-AndroidApp.ps1](Verify-AndroidApp.ps1) 构建 NG App 的 Debug/未签名 Release、测试 APK、Kotlin 协议单元测试和两套 Lint。严格锁和散列核对，不操作设备；[配对服务复验](../tests/integration/android_pairing/README.md)另行执行。

[Build-UiAcceptance.ps1](Build-UiAcceptance.ps1) 为每轮 UI 验收生成不可复用的 `rN` 修订：Windows 使用独立程序集版本和 LocalAppData 数据根，Android 使用独立应用 ID 并以全新安装部署；脚本拒绝覆盖既有修订。可选 `-DeviceSerial` 时只安装该验收 APK并配置已确认的本地测试权限，不修改正式包或正式配对数据。

现有审计复现工具保留于 [docs/audit](../docs/audit/VALIDATION.md)，它用于观察上游缺陷，不是产品通过测试的证据。后续测试数据/原始证据可用已忽略的 `.audit/runs/`，禁止存入用户秘密和真实文件。
