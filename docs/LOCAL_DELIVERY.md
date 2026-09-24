# 本地安装交付说明

PhoneBridge NG `0.1.0` 采用第三方本地侧载，目标为 Windows 11 x64 与 Android 8.0（API 26）及以上。当前任务不发布 GitHub Release、不上传应用商店，也不提供自动更新。

## 构建产物

在项目根目录执行：

```powershell
./scripts/Build-LocalDelivery.ps1
```

脚本不下载依赖、不安装驱动、不连接手机。它只使用项目 `.audit/tools` 与 `.audit/downloads` 下已经核验的固定工具，输出到被忽略的 `.audit/delivery/output`：

- `PhoneBridge-NG-Setup-0.1.0.exe`：Windows 正式单一安装入口，自包含 .NET 10、固定 rclone 1.75.1，并内置官方 WinFsp 2.1.25156 MSI。只有系统未检测到 WinFsp 时才会运行该 MSI并显示系统管理员确认；安装器显示名和文件描述不含本地预览标签。
- `PhoneBridge-NG-0.1.0-local-test.apk`：默认 `LocalTest` 模式使用当前开发用户的 Android 标准 Debug 测试证书，仅用于本机验收。
- `PhoneBridge-NG-0.1.0.apk`：只有显式选择 `ThirdPartyRelease`、通过项目外专用密钥和预绑定证书摘要校验后才会生成，用于长期第三方侧载。
- `README.txt`：与当前产品界面一致的最短安装、配对、日常连接、正确结束和排障步骤；安装后同样位于 Windows 程序目录。
- `PhoneBridge-NG-0.1.0-source.zip`：与正式二进制对应的 Android/Windows 源码、构建脚本、测试、GPL、NOTICE 和第三方许可，不含签名私钥、密码或本机构建缓存。
- `SHA256SUMS.txt` 与 `delivery-manifest.json`：安装器、APK、说明文件、源码包的散列、版本和 Android 签名证书摘要。

`.audit/delivery/output` 是当前唯一交付目录，脚本每次构建时都会先重建它；`.audit/runs` 仅保存历史验证证据，不能从那里选择安装包交付。

正式二进制构建并完成验收后执行 `./scripts/New-SourceDelivery.ps1`。该脚本只从项目源码目录收集文件，排除 `.audit`、版本库元数据、构建输出、缓存、测试结果和私钥文件类型；源码 ZIP 内含逐文件 SHA-256 清单。脚本回读 ZIP 后通过同卷暂存更新默认 manifest 与 SHA256SUMS。每次二进制或源文件变化后都必须重新运行。

Windows 安装程序当前没有 Authenticode 产品签名，Windows 可能显示未知发布者。`LocalTest` 构建的 APK 文件名和清单明确标为 `local-test`，不能冒充长期签名版本。正式长期私钥已在 P1-041 建立于项目目录外，并在独立 USB 介质完成一致性备份；P1-042 已在 Samsung 上验证从测试证书迁移、锁屏访问和正式同签名覆盖升级。P1-043 已把同一正式产物提升到当前默认交付目录，manifest 为 `third-party-sideload` / `third-party-release`。

## Android 签名模式

### 一次性建立长期签名身份

正式长期密钥只初始化一次。主密钥默认保存到 `%LOCALAPPDATA%\PhoneBridge-NG\Signing\phonebridge-release.p12`；备份目录必须由操作者明确指定，并应位于独立的加密 U 盘、外接硬盘或其他独立受保护介质。脚本要求主密钥位于本地固定磁盘；本地备份卷会按 Windows 物理磁盘号核对，同一磁盘上的其他分区或盘符一律拒绝。可移动卷、不同物理磁盘的固定卷和网络卷可以作为备份位置；网络存储的后端是否真正物理独立仍由操作者确认。

先在当前 PowerShell 窗口私下输入同一个强密码两次。输入不会显示，也不会进入命令历史：

```powershell
$env:PBNG_ANDROID_STORE_PASSWORD = Read-Host '输入签名密码' -MaskInput
$env:PBNG_ANDROID_KEY_PASSWORD = Read-Host '再次输入同一密码' -MaskInput
```

然后执行一次初始化，并把备份路径替换为真实独立介质：

```powershell
./scripts/Initialize-AndroidReleaseSigning.ps1 `
  -BackupDirectory 'E:\PhoneBridge-NG-Signing-Backup'
```

脚本固定生成 PKCS12、RSA-4096、`phonebridge-release` 别名，拒绝项目内路径、同一物理磁盘备份、弱密码、不同密码、嵌套目录和任何已有目标文件。它先核对主副 keystore 的 SHA-256 与证书身份，再写入两份相同的 `android-release-identity.json`；该记录不含密码和环境变量名称。主目录 ACL 仅允许当前用户与 SYSTEM。备份介质可能不支持 Windows ACL，因此备份 keystore 依靠强密码和介质自身保护。

若提交过程中发生异常，脚本保留已经提交的密钥或身份文件，不自动删除潜在的唯一私钥；不要重新生成，先核对现有主副文件。成功后记录身份文件中的 `certificate_sha256`，并清除当前进程的密码变量：

```powershell
[Environment]::SetEnvironmentVariable('PBNG_ANDROID_STORE_PASSWORD', $null, 'Process')
[Environment]::SetEnvironmentVariable('PBNG_ANDROID_KEY_PASSWORD', $null, 'Process')
```

长期签名身份丢失后，已安装 APK无法用新密钥原位升级，因此主密钥、独立备份和密码必须一起长期保存。

默认命令保持本地测试行为：

```powershell
./scripts/Build-LocalDelivery.ps1
```

长期第三方侧载必须显式使用发行模式。密码值预先放入当前 PowerShell 进程的环境变量；命令只传环境变量名称，密码不会进入命令行、manifest 或散列清单：

```powershell
./scripts/Build-LocalDelivery.ps1 `
  -AndroidSigningMode ThirdPartyRelease `
  -AndroidSigningIdentity "$env:LOCALAPPDATA\PhoneBridge-NG\Signing\android-release-identity.json"
```

`PBNG_ANDROID_STORE_PASSWORD` 和 `PBNG_ANDROID_KEY_PASSWORD` 是默认环境变量名；只有确需使用其他名称时才传相应参数。发行模式不接受手工指定 keystore、别名或证书摘要。它从身份记录所在目录定位 keystore，并在构建前核对：身份 schema/用途、项目外普通文件、固定别名、keystore SHA-256、证书 SHA-256、主题、有效期和当前有效状态。签名后再次读取 APK证书并复核同一摘要；任一条件不符则不生成可交付清单。构建结束后应从当前进程清除密码环境变量。

勾选“Windows 启动时自动运行 PhoneBridge”后，客户端登录时只进入托盘，不自动连接手机、不启动 rclone、不创建盘符。电脑重启后需要打开客户端并手动点击连接。

## 安装、升级与卸载

Windows 客户端按当前用户安装到 `%LOCALAPPDATA%\Programs\PhoneBridge NG`。安装程序检测到客户端仍在运行时会停止，不会强制结束进程或盘符；先在托盘选择“退出”，让客户端完成安全卸载后再升级或卸载。

升级覆盖应用文件，但保留 `%LOCALAPPDATA%\PhoneBridge-NG` 中的 DPAPI 配对记录、日志、会话及可能尚待恢复的 VFS 缓存。卸载同样保留这些数据，避免把未完成写入当作可删除临时文件；只在当前用户自启动值仍精确指向本次安装路径时删除该值。WinFsp 可能被其他软件使用，因此卸载 PhoneBridge NG 时不移除 WinFsp。

Android 使用 `adb install -r` 覆盖安装时，只有签名证书相同才保留配对与设置。当前已安装的 `local-test` APK沿用本机测试证书；首次切换到长期发行证书必须先卸载测试版，因此配对记录不能跨签名迁移。以后所有升级必须继续使用同一长期私钥和证书。

### 无 WinFsp 的干净 Windows 首装验收

只在一台真正没有安装 WinFsp、也没有安装 PhoneBridge NG 的 Windows 11 x64 测试机上执行。不要为了补证卸载日常电脑正在使用的 WinFsp。先把本项目和当前 `.audit/delivery/output` 带到测试机，在普通用户 PowerShell 中运行预检：

```powershell
pwsh -NoProfile -File ./scripts/Test-CleanWindowsInstall.ps1 `
  -DeliveryDirectory 'C:\PhoneBridge-Test\output' `
  -EvidenceDirectory 'C:\PhoneBridge-Test\evidence' `
  -Mode Preflight
```

预检只读核对 Windows 版本/架构、非提升会话、无待重启、无 WinFsp/PhoneBridge、manifest 和安装器散列。`eligible=true` 后才运行同一命令并把模式改为 `InstallAndVerify`。安装器会为内置 WinFsp MSI显示一次系统 UAC，脚本本身不提权、不自动重启、不启动客户端或 rclone。

返回码 `0` 表示首装和核对通过且无需重启；`3010` 表示安装成功但必须重启后再使用；`2` 表示前置条件不满足，安装器没有运行；`3` 表示安装后核对失败。证据目录包含 JSON与 Inno Setup 日志，必须与交付目录分开保存。该脚本只验收安装分支，不连接手机或证明盘符链路。

P1-046 已在无 WinFsp/PhoneBridge 的 Windows 11 x64 Sandbox 中完成上述首装，退出码 0，全部安装后断言通过。Sandbox 固定管理员账户只通过内部验收生成器的显式限定开关放行；普通测试机仍必须按上面的非提升会话流程执行。

## 许可与来源

安装目录包含项目 GPL、来源修改说明、第三方许可、.NET 许可和第三方通知。WinFsp 使用官方未修改安装包；用户界面和文档保留：WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos，[项目仓库](https://github.com/winfsp/winfsp)。如果把二进制交给其他人，必须同时提供同一默认交付目录中的 `PhoneBridge-NG-0.1.0-source.zip`。
