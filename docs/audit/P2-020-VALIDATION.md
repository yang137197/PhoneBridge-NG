# P2-020 验证记录 — GitHub Release v0.2.4

日期：2026-09-29。结果：通过。

## 发布前门禁

- GitHub Latest 仍为正式 `v0.2.3`，非草稿、非预发布且有六项资产；远端 `main` 为 `2650d203af8572bb297c0801a7bcd6b86393c2f1`。
- `v0.2.4` 标签和 Release 均不存在；本地工作树干净，候选提交 `98e17a182ec61eb6d71382544c3d450a61c1f949` 是远端 `main` 的快进后代。
- P2-019 六项候选制品、正式基线升级和真机基础链路验收已经通过；本任务没有重新构建或改变二进制。

## 发布身份

- Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.4>
- Release ID：`398871286`
- 标签：`v0.2.4`
- 标签提交：`98e17a182ec61eb6d71382544c3d450a61c1f949`
- 发布时远端 `main`：同一提交。
- 状态：非草稿、非预发布、Latest。
- 发布时间：`2026-09-29T06:22:12Z`。

## 资产回读

| 资产 | 字节 | GitHub SHA-256 |
| --- | ---: | --- |
| `PhoneBridge-NG-Setup-0.2.4.exe` | 81,172,645 | `3461D050CE4FA09B5A02C1A1F7E0B39E02F2F02FE40E24A0117E5090A012C13E` |
| `PhoneBridge-NG-0.2.4.apk` | 3,528,958 | `578E135C03EED32C8EC70ECAB4EDCAE023F4637E7A0BB1B7C4F03909B693DD1F` |
| `PhoneBridge-NG-0.2.4-source.zip` | 1,056,506 | `FB738E79670B5915A35AF7EE1A0280F4A61CF93618301CCF19E46A46DD26EF66` |
| `README.txt` | 4,028 | `B017FE7E0DFB416BF7309656F8DC16040A54994BB96D12F5B2BA7E231453C25B` |
| `SHA256SUMS.txt` | 363 | `35E5123BDFC486F64CABA6DCAAACF3A88EA10CCD9A39CA1A90131A95DC3E87FC` |
| `delivery-manifest.json` | 2,038 | `A1283D54C00C61DC8A152F9C2F4332DD677AAA1273FF1DF30A34B0FFC565D742` |

公开后回读 6/6 资产的文件名、大小、GitHub `digest` 和 uploaded 状态，全部与本地候选一致；同时再次确认 Latest、Release 状态、远端 `main` 和标签提交。

## 本地镜像

- `.audit/delivery/output/` 经独立暂存目录逐文件比较摘要后切换为同一组 `v0.2.4` 资产。
- 原 `v0.2.3` 镜像保留于 `.audit/delivery/output-v0.2.3-pre-v0.2.4-20260929/`，未删除。

## 已知边界

- Windows 安装器未配置 Authenticode 产品签名。
- Android 为第三方侧载发行，安装和更新仍由 Android 系统确认。
- Windows 与 Android 在“当前已是最新版”路径已经验证；未来真实新版本存在时的应用内下载与系统安装确认路径尚未验证。
- 全新 Windows 首次安装和首次配对尚未执行，由 P2-021 使用本 Release 的公开资产独立验收。
