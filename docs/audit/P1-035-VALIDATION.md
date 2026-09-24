# P1-035 Android 长期第三方发行签名路径验收

日期：2026-09-23。结果：通过限定验收。测试签名与长期第三方发行签名已严格分离；正式长期私钥尚未生成。

## 已确认

- `LocalTest` 默认模式仍使用标准 Debug key，生成 `PhoneBridge-NG-0.1.0-local-test.apk`，manifest 明确标记 `local-preview` 和 `local-test`。
- `ThirdPartyRelease` 要求项目目录外 keystore、非 `androiddebugkey` 别名、当前进程密码环境变量和 64 位十六进制证书 SHA-256。
- keytool 在输出目录重建前读取证书；Android Debug 身份或摘要不匹配时失败。apksigner 签名后再次核对 v2 签名和同一证书摘要。
- manifest 不包含 keystore 路径、别名、密码或环境变量名称，只记录签名模式、证书摘要、APK文件名与散列。
- 缺少 keystore、项目内 keystore、Debug 别名、缺少密码环境变量和证书摘要不匹配五项负向用例全部退出 1。
- 一次性项目外 RSA-3072 密钥完成完整发行构建；测试 APK名为 `PhoneBridge-NG-0.1.0.apk`，SHA-256 `3E31E2BE6A5FB34F9C7ADBFFFE43280EA1C5E4A28ADA9C23AEE36A5CEFFE5AC4`，证书摘要 `8F01CBD2573BF62AB15CC0A2E79506C6DD71AD5C858435BC9A32BA6409C40D5B`，v2 验证通过。该证书仅为一次性验收身份。
- 随机测试密码未出现在发行构建日志；测试私钥及测试发行目录已经删除，项目内无 `.p12`、`.jks` 或 `.keystore`。
- 默认 LocalTest 交付随后完整重建成功。当前本地 APK散列保持 `B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC`；安装器 SHA-256 更新为 `4E02E996E36B7AB19EB78ACCA9E88E7CD09A4137445BAFA5093B7A9F6914CF32`，只因随包说明及交付脚本变化。发布 DLL仍与 P1-030 相同。

机器可读证据：`.audit/runs/P1-035/release-signing-verification.json`。

## 未验证与边界

没有生成正式长期私钥，没有安装测试发行 APK，也没有改变 Android/Windows 运行代码。正式密钥一旦用于安装，后续升级必须永久使用同一密钥；因此只有在受保护主存储和独立备份位置明确后才能创建。

Windows 安装器仍未作 Authenticode 产品签名；无 WinFsp 的干净 Windows 安装分支仍待独立环境验收。本任务不发布 GitHub Release或应用商店。
