# P1-024 本地可安装交付包验收

日期：2026-09-22。结果：通过限定范围。本结论证明当前开发机和 Samsung 测试机可使用本地安装形态，不表示公开签名发行、所有 Windows 环境或所有 Android 厂商已经通过。

## 交付产物

`scripts/Build-LocalDelivery.ps1` 使用固定工具链完成 locked runtime restore、自包含 Windows 发布、Android Release 构建、zipalign、本地测试签名和安装器编译。脚本不下载、不安装依赖、不连接设备，也不发布产物。

最终输出位于被 Git 忽略的 `.audit/delivery/output`：

| 产物 | 大小 | SHA-256 |
| --- | ---: | --- |
| `PhoneBridge-NG-Setup-0.1.0.exe` | 81,082,283 字节 | `C786ABCD597A384A8C464C67D3F16763BA8302FC3ADCA1B5C61691D1DCBE50B9` |
| `PhoneBridge-NG-0.1.0-local-test.apk` | 3,483,622 字节 | `B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC` |

manifest 记录 Windows 11 x64、.NET SDK 10.0.401、自包含 runtime 10.0.12、Android minSdk 26/targetSdk 36。APK 通过 v2/v3 签名验证，证书 SHA-256 为 `7D5EC604BC5C40BC294617BD62BD00E2C4E4063F8326783E34D26CEE0CA7CBFF`；连续最终构建得到相同 APK 哈希。Windows 安装程序 Authenticode 状态为 `NotSigned`，与本地预览边界一致。

## 依赖和许可

- rclone 1.75.1 Windows amd64 SHA-256 为 `033EEE51C9AD47C2DE2624B6674D355274BCD6CF0027A5F85DB4437BA24AE81C`。
- WinFsp 2.1.25156 官方 MSI SHA-256 为 `073A70E00F77423E34BED98B86E600DEF93393BA5822204FAC57A29324DB9F7A`，NAVIMATICS LLC Authenticode 签名有效。
- Inno Setup 7.1.0 编译器的 Pyrsys B.V. Authenticode 签名有效。
- 安装内容包含项目 GPL/NOTICE、rclone、WinFsp、Inno Setup、.NET 许可和第三方通知；Windows 界面保留 WinFsp 归属与项目链接。

## Windows 实际安装生命周期

首次静默安装返回 0，安装到 `%LOCALAPPDATA%\Programs\PhoneBridge NG`，开始菜单快捷方式、卸载注册信息、固定 rclone 与许可材料存在。安装版以 `--startup` 启动后约 13 秒自动连接已配对 Samsung 并挂载 P:，根目录可读取，且只有一个桌面进程和一个 rclone。

客户端和 P: 仍运行时再次启动安装器，应用互斥体使安装返回失败；原桌面进程、rclone 和 P: 保持，不发生强杀或半覆盖。应用正常卸载并退出后，覆盖升级返回 0，配对文件散列保持不变。

卸载返回 0，应用目录与快捷方式删除；测试设置的精确自启动值被删除，`Pairings-v1`、`Sessions` 和可能用于恢复的本地数据保留，WinFsp 注册项及 `winfsp-x64.dll` 保留。最终产物再次覆盖安装返回 0，两份 `.pairing` 记录及 `.store.lock` 前后散列完全相同，用户此前关闭的自启动保持关闭，日志记录安装成功且无需重启。最终 Windows 状态为桌面进程 0、rclone 0、P: 不存在。

## Samsung 覆盖安装与共享

Samsung SM-S9180/API 36 上，最终 APK 使用与已安装测试版相同的证书执行 `adb install -r` 成功。版本从 `0.1.0-dev` 更新为 `0.1.0`，`firstInstallTime=2026-09-20 21:30:49` 保持不变；当前 `versionCode=1`、minSdk 26、targetSdk 36。

覆盖安装后自动打开应用并开始共享，Music 目录、`LY` 配对和安全模式均保留；Android 前台服务运行且 8273 监听，安装版 Windows 已能挂载。收尾时由 ADB 自动停止共享，回读无前台共享服务、8273 监听或 PhoneBridge WakeLock。

## 自动化验证

- `scripts/Verify-Windows.ps1`：Release build 0 warnings、0 errors；Desktop 50、Discovery 54、Mounting 44、Pairing 50、Connection 18、Credentials 63，共 279/279 通过。TRX 位于 `.audit/windows-verification/20260922-160455`。
- `scripts/Verify-AndroidApp.ps1`：Debug/Release、test APK、Kotlin 单元测试及 Debug/Release Lint 成功；Gradle `BUILD SUCCESSFUL`，123 个 task 完成或保持最新。
- `scripts/Build-LocalDelivery.ps1`：最终执行成功，生成的 manifest、SHA256SUMS 与独立 `Get-FileHash` 一致。

## 未验证和边界

当前电脑已安装 WinFsp，因此“系统完全没有 WinFsp时显示 UAC 并安装官方 MSI”的真实分支未执行；脚本已核验 MSI 散列/签名，安装器编译通过，但不能据此声称该分支已有实机证据。未在第二台干净 Windows、Windows ARM64、真实 API 26 手机或其他厂商手机重复安装。

Windows 安装程序没有 Authenticode 产品签名，Android APK使用本机测试证书；二者只用于本地第三方侧载。项目未创建 GitHub Release、应用商店提交、在线更新或下载器，也未重复大文件、视频和文件名测试。
