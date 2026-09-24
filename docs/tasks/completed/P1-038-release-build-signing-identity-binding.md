# P1-038 — 发行构建绑定签名身份记录

状态：已完成。日期：2026-09-23。

## 目标

让 `ThirdPartyRelease` 构建直接消费 P1-036 生成的身份记录，并在构建前把身份文件、keystore 文件散列、别名、证书摘要、主题和有效期绑定为一个不可手工拼错的输入。

## 范围

收口 `Build-LocalDelivery.ps1` 的发行签名接口；本地测试模式保持不变。使用项目外一次性身份覆盖正确构建及篡改/误配置失败关闭，并重建默认本地测试交付。

## 不做什么

不生成正式长期私钥，不保留测试私钥，不发布 APK，不安装 APK，不修改 Android/Windows 运行代码，不改变当前版本号。

## 涉及文件

- `scripts/Build-LocalDelivery.ps1`：身份记录解析与多重绑定。
- `docs/LOCAL_DELIVERY.md`、`DECISIONS.md`：正式构建入口和边界。
- 本任务、验收记录和机器可读证据。

## 实现

- `ThirdPartyRelease` 新增单一 `AndroidSigningIdentity` 输入；默认指向初始化工具的主身份记录。发行模式拒绝手工 `AndroidKeystore` 覆盖，本地测试模式拒绝混入发行身份。
- 身份记录必须位于项目外普通目录并包含完整 schema；keystore 文件名只能是同目录 `.p12` 普通文件。
- 构建前核对固定产品/用途/PKCS12/别名、keystore SHA-256、证书 SHA-256、主题、起止时间和当前有效状态。JSON自动解析为 `DateTime` 时按实际类型转 UTC，避免本地时区二次解释。
- 密码环境变量保持默认名称且只在进程内读取；构建后的 APK再次核对 v2 签名及同一证书摘要。manifest 不增加密钥或身份路径。

## 测试

- PowerShell 语法解析通过。
- 缺少身份、项目内身份、旧式 keystore 绕过、本地测试混入身份、缺少必填属性、keystore 散列不符、证书摘要不符和证书主题不符八项用例均退出 1。
- P1-036 初始化工具生成的一次性项目外 RSA-4096 身份完成完整 `ThirdPartyRelease` 构建；APK v2 验证和 manifest 证书摘要一致。
- manifest 未检出临时身份/keystore 路径、密码、密码环境变量名；日志未检出随机测试密码。
- 一次性主副私钥和临时发行目录已删除，项目内长期发行密钥为 0。
- 默认 `LocalTest` 交付完整重建成功：安装器 SHA-256 `F2608670F32B706C45581F1050D69D47083B6448D5AE32A5BCC50DCF95ABF9A0`；APK SHA-256 保持 `B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC`；发布 DLL仍为 `F0FF0BADC9DAB5F18B8E7128CCC972EDEDBBC6E450E204551EB199D533381C09`。

## 验收结果

通过限定验收。身份绑定发行路径和默认本地测试交付均已完成；正式长期身份仍未生成，本任务不发布或安装产物。
