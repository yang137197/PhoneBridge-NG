# P2-010 验证记录 — v0.2.2 盘符显示名与设备级分配

日期：2026-09-28。分支：`codex/v0.2.1-v0.2.2`。

## 已确认

- Windows 配对记录 schema 3 保存 Auto 或固定 D–Z；旧 schema 1/2 继续读取并默认 Auto。
- 设备设置原子保存设备备注与盘符偏好；挂载中的修改只在下一次连接生效。
- Auto 从 D 到 Z 选择首个未被系统或 PhoneBridge 会话占用的盘符；固定盘符冲突失败关闭，不静默改盘。
- rclone 卷名使用稳定的设备身份服务器名和可读的备注/设备名共享名；共享名经过 Windows 非法字符、尾随字符和 UTF-16 长度清洗。
- Windows 桌面与 Android 版本元数据均为 `0.2.2`；当前正式 `0.2.0` 源码交付脚本会拒绝版本不一致的源树。

## Windows 自动验证

```text
.audit/tools/dotnet-10.0.401/dotnet.exe build windows/PhoneBridge.Windows.slnx --configuration Release --no-restore --disable-build-servers -maxcpucount:1
成功：0 warnings，0 errors

.audit/tools/dotnet-10.0.401/dotnet.exe test windows/PhoneBridge.Windows.slnx --configuration Release --no-build --no-restore --disable-build-servers
通过：306；失败：0；跳过：0
```

分项：Discovery 54、Pairing 50、Credentials 67、Mounting 47、Connection 27、Desktop 61。

新增测试覆盖 schema 1/2 兼容、schema 3 偏好持久化、非法固定盘符、Auto 首选/跳过占用、多会话预留、固定盘符冲突、D–Z 耗尽、卷名清洗/长度/稳定性、同名设备隔离、rclone 参数和双语资源键。

## Android 与脚本验证

```text
gradlew :app:testDebugUnitTest :app:assembleDebug :app:assembleRelease :app:assembleAndroidTest :app:lintDebug :app:lintRelease
BUILD SUCCESSFUL；123 tasks：29 executed，94 up-to-date

PowerShell Parser：Build-LocalDelivery.ps1、Build-UiAcceptance.ps1、New-SourceDelivery.ps1，3/3 无语法错误
```

Android Debug/Release 输出元数据均为 `versionCode 4`、`versionName 0.2.2`；Windows EXE 文件版本为 `0.2.2.0`。

## 隔离候选

`scripts/Build-UiAcceptance.ps1 -Revision r22` 成功生成独立数据根和 Android 包名的测试候选：

```text
PhoneBridge-NG-Windows-v0.2-ui-preview-r22.zip
SHA-256 EDC3AFB73A9802366ED77F1FD271829A3C45F2BDA70FC9C1C6C48CE4E878D494

PhoneBridge-NG-v0.2-ui-preview-r22.apk
SHA-256 27480526E72E3B3AD0689F83A9B8DCA6B30C75AE0CF965EAA074350A4C250615
```

Windows 预览使用独立数据修订 `0.2.2.22`；Android 包名为 `org.phonebridge.ng.uipreviewr22`。预览 Windows 进程已按路径和 PID 停止，已安装正式版进程仍来自 `%LOCALAPPDATA%\Programs\PhoneBridge NG\PhoneBridge.Desktop.exe`。候选未安装、未发布，也不是正式交付入口。

## 尚未验证

- 真实手机备注在 Explorer 中的最终显示和非法字符清洗后的可见结果。
- 真实 USB、网络盘和多手机共同占用盘符时的 Auto 分配与固定盘符冲突文案。
- 手机停止共享、设备离线后的实际确认时延及 Explorer 红叉盘符刷新。
- `v0.2.0` 正式安装与真实配对数据覆盖升级到 `v0.2.2` 后的 schema 迁移、设置保持和重连。
- 当前自动化环境未提供可枚举的原生 Windows 应用窗口，因此没有完成实际桌面视觉检查。

## 边界

本任务没有创建标签、GitHub Release 或正式安装器/APK，没有改写 `.audit/delivery/output/`，也没有覆盖当前已安装的 `v0.2.0`。
