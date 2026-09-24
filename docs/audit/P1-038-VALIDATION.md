# P1-038 发行构建绑定签名身份记录验收

日期：2026-09-23。结果：发行身份绑定路径通过；正式长期身份尚未生成。

## 已确认

- `ThirdPartyRelease` 只接受 `AndroidSigningIdentity`；默认路径与 P1-036 初始化工具一致。手工 `AndroidKeystore` 覆盖被拒绝，`LocalTest` 也拒绝发行身份输入。
- 身份记录和 keystore 必须位于项目外普通路径。构建绑定 schema、产品、用途、PKCS12、固定别名、keystore 文件名/SHA-256、证书 SHA-256、主题、有效期和当前有效状态。
- PowerShell `ConvertFrom-Json` 会把 ISO 时间变为 `DateTime`；实现按实际对象类型统一转 UTC 后再比较证书时间，已消除本地时区二次解释造成的偏差。
- 缺少身份、项目内身份、旧式 keystore 绕过、本地测试混入身份、缺少证书主题、keystore 散列不符、证书摘要不符、证书主题不符八项负向用例全部退出 1。
- 一次性项目外 RSA-4096 身份完成完整发行构建。测试 APK v2 签名通过，证书 SHA-256 为 `A8395F10A51D80F536E77128BC58339B92B239CC734A743474C530D6F13F00FA`，APK SHA-256 为 `388ADFFB733D93F6E8C8ED7233EF3BAC1A21D9DA28E740CC31219F73546A0443`；两者只属于已删除的临时身份和产物。
- manifest 不含身份/keystore 路径、密码或密码环境变量名称；构建日志未检出随机测试密码。
- 临时主副私钥及临时发行输出已删除，项目内长期发行密钥为 0；保留的 `.audit/android-user/debug.keystore` 仅用于 `LocalTest`。
- 文档收口后默认 `LocalTest` 交付完整重建成功。当前安装器大小 81,089,558 字节、SHA-256 `F2608670F32B706C45581F1050D69D47083B6448D5AE32A5BCC50DCF95ABF9A0`；APK仍为 3,483,622 字节、SHA-256 `B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC`；README SHA-256 `A93F928FBC226F8BC209C73242C5CF320535040B927E76BC5EE1AA9E184503DE`。发布 DLL仍为 P1-030 的 `F0FF0BADC9DAB5F18B8E7128CCC972EDEDBBC6E450E204551EB199D533381C09`，说明产品运行代码未变化。

机器可读证据：`.audit/runs/P1-038/identity-bound-release-verification.json`。

## 未验证与边界

没有生成正式长期私钥，没有安装或分发正式证书签名的 APK。正式身份创建仍要求操作者先提供独立受保护备份目录。临时发行 APK只验证构建和签名边界，未在手机安装。
