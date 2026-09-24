# P1-035 — Android 长期第三方发行签名路径

状态：已完成。日期：2026-09-23。

## 目标

将本地 Debug 测试 APK 与长期第三方发行 APK明确分离，使发行构建只接受项目目录外的专用密钥、进程环境中的密码和预先绑定的证书 SHA-256，并在任何不一致时失败关闭。

## 范围

扩展本地交付脚本的 Android 签名配置、产物名称、manifest 和使用说明；保留现有 LocalTest 默认模式。使用一次性、项目外的隔离密钥验证发行路径，验证后删除测试私钥和测试发行产物。

## 不做什么

不生成正式长期私钥，不决定用户的最终备份介质，不把密码写入参数、日志、manifest 或仓库；不购买 Windows Authenticode 证书，不发布 Release 或应用商店，不修改 Android/Windows 运行代码。

## 涉及文件

- `scripts/Build-LocalDelivery.ps1`：签名模式、预检、签名和 manifest。
- `docs/LOCAL_DELIVERY.md`、`docs/INSTALL_LOCAL.txt`：测试/发行边界和安全用法。
- 本任务、验收记录和机器可读证据。

## 实现

`Build-LocalDelivery.ps1` 现在有 `LocalTest` 与 `ThirdPartyRelease` 两种互斥签名模式。默认模式继续生成带 `local-test` 的 Debug 签名 APK。发行模式要求显式提供项目目录外 keystore、非 Debug 别名、两个当前进程密码环境变量名称和预期证书 SHA-256；在重建输出前读取证书并拒绝 Debug 身份或摘要不一致，签名后再次从 APK复核证书。

发行模式只把环境变量名称传给 keytool/apksigner，密码值不进入命令行、构建日志、manifest 或散列清单。发行产物名为 `PhoneBridge-NG-0.1.0.apk`，manifest 标记 `third-party-sideload`/`third-party-release`；测试产物继续标记 `local-preview`/`local-test`。说明文件改为从 manifest 读取实际 APK 文件名，并记录首次切换签名会失去原测试版应用数据。

新增 ADR-029 和来源修改说明。构建脚本不生成、复制或备份正式私钥；实际长期密钥留到保存与备份边界明确后单独生成。

## 测试

- PowerShell 语法解析通过。
- 五项发行预检负向用例均在构建前退出 1：缺少 keystore、keystore 位于项目内、Debug 别名、密码环境变量缺失、证书摘要不匹配。
- 使用项目目录外的一次性 RSA-3072 测试密钥完成完整 `ThirdPartyRelease` 构建；manifest 为 `third-party-sideload`，APK文件名为 `PhoneBridge-NG-0.1.0.apk`，签名模式和证书 SHA-256 与预期一致。
- 测试发行 APK v2 签名通过，SHA-256 为 `3E31E2BE6A5FB34F9C7ADBFFFE43280EA1C5E4A28ADA9C23AEE36A5CEFFE5AC4`；构建日志未出现随机测试密码。
- 验证完成后，一次性私钥和测试发行目录均删除；项目内 `.p12`、`.jks`、`.keystore` 为 0。
- 默认 LocalTest 完整重建成功：Android `BUILD SUCCESSFUL`，Windows publish/Inno Setup 成功；APK散列仍为 `B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC`，当前安装器 SHA-256 为 `4E02E996E36B7AB19EB78ACCA9E88E7CD09A4137445BAFA5093B7A9F6914CF32`。
- 机器可读证据：`.audit/runs/P1-035/release-signing-verification.json`。

## 验收结果

已完成并通过限定验收。长期发行签名入口已具备失败关闭的代码和完整隔离验证，同时没有创建或遗留可误用的正式/测试私钥。产品运行代码没有改变，因此不重复真机、传输、睡眠或重启测试。

当前仍未生成正式长期 Android 私钥，也没有稳定发行 APK。唯一下一任务是在用户确定的受保护保存及独立备份位置创建正式长期密钥，记录公开证书摘要，并用本任务入口生成首个稳定第三方 APK；这一步会建立以后升级不可更换的应用身份。
