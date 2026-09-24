# P1-044 正式对应源码交付包验收

日期：2026-09-23。结果：通过。

## 已确认

- 默认交付新增 `PhoneBridge-NG-0.1.0-source.zip`，SHA-256 为 `A2DF7C45C7FF5CE16323C5A6165C8D57DCA7FF81057F363526F83CF576875AFB`，大小 771,137 字节。
- ZIP 固定根目录为 `PhoneBridge-NG-0.1.0-source`，包含 263 个源码文件及 1 个 `SOURCE-MANIFEST.json`；共 264 个条目。
- 源码范围包含 Android、Windows、构建脚本、测试、当前产品文档、GPL、NOTICE 和第三方许可；必需入口 `LICENSE`、`NOTICE.md`、`scripts/Build-LocalDelivery.ps1`、`android/app/build.gradle.kts` 和 `windows/PhoneBridge.Windows.slnx` 均存在。
- `.audit`、`.git`、编译输出、缓存、测试结果和常见私钥文件类型不进入 ZIP；所有源文件拒绝重解析点和异常大文件。
- 创建后独立回读全部 ZIP 路径、条目数、长度和逐文件 SHA-256，全部匹配内置清单；没有绝对路径、反斜杠或 `..` 路径条目。
- 内容扫描未发现当前本机用户绝对路径、实际配对码或私钥 PEM 标记。
- 默认 `delivery-manifest.json` 已记录源码包文件名、SHA-256、格式、GPL 许可位置和源文件数；`SHA256SUMS.txt` 同时覆盖安装器、正式 APK、README 和源码包。
- 二进制保持不变：正式 APK SHA-256 仍为 `E53F618DB6888F14811A98BBC0EFD7965D72E44E84CE28D27C5840C427B1F0C3`，Windows 安装器仍为 `B357083FD1B53491BE9F7C2E1378450B31BAC7FBA47EF63363D39F9158F280A1`。

机器可读证据：`.audit/runs/P1-044/source-delivery.json`；生成与验证入口：`scripts/New-SourceDelivery.ps1`。

## 执行偏差与修正

首次生成后独立检查发现 PowerShell 在重写 JSON 时把 `_utc` 时间显示为本地 `+08:00`。时间瞬间未改变，但字段名要求明确 UTC；脚本改用 `ConvertFrom-Json -DateKind String` 并把构建时间规范化为 `+00:00`，随后重新生成最终源码包和清单。最终 `built_at_utc` 与 `packaged_at_utc` 均为明确 UTC。

## 未验证与边界

- 源码包证明当前交付中存在完整对应源文件和许可材料，不替代第三方法律意见或外部安全审计。
- Windows 安装器仍没有 Authenticode 产品签名。
- 真实无 WinFsp 的干净 Windows 首装仍缺独立机器证据。
- 当前仓库尚无提交基线；源码 ZIP 以逐文件散列固定内容，但没有 Git commit ID。
