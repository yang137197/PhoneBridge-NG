# P2-024 验证记录 — GitHub Release v0.2.5

日期：2026-09-29。结果：通过。

## 发布前门禁

- GitHub Latest 为正式 `v0.2.4`，非草稿、非预发布且有六项资产。
- 远端 `main` 为 `62d6b06cbc634d3baf57a486db7ce6948eab78f7`；P2-023 验收 head 为 `11a58c51203e62ab77443bea15403ad7609e945e`，可从远端 `main` 快进。
- 本地工作树干净；本地和远端均无 `v0.2.5` 标签，GitHub 无 `v0.2.5` Release。
- 六项候选资产的文件集合、大小和 SHA-256 已在发布前重新核对；本任务没有重新构建或改变资产。

## 发布身份

- Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.5>
- Release ID：`399034507`
- 标签：`v0.2.5`
- 注释标签对象：`8091afbe4f58433fd636ce90da26ec2bf7f12c92`
- 标签提交：`11a58c51203e62ab77443bea15403ad7609e945e`
- 发布时远端 `main`：同一提交。
- 状态：非草稿、非预发布、Latest。
- 发布时间：`2026-09-29T10:24:30Z`。

## 资产回读

| 资产 | 字节 | GitHub SHA-256 |
| --- | ---: | --- |
| `PhoneBridge-NG-Setup-0.2.5.exe` | 81,177,962 | `2A1209B9935BE2EA6A3DD3AC083A98F48E119801D99FCD616F5375BDA455DB2E` |
| `PhoneBridge-NG-0.2.5.apk` | 3,528,958 | `CEF0172E316B8F31C9A2B80389C83FDF51C6C86D29AB7CF73E8559FE1B77CEAB` |
| `PhoneBridge-NG-0.2.5-source.zip` | 1,061,066 | `7947175333B5A5166B8457CA15111C380E6FD3B7AF8824B1BB5D01FAE5946555` |
| `README.txt` | 4,028 | `3580F5A9195E6C201862F94B9F80791AAB9AE7B91BF65A2039D898546E20C788` |
| `SHA256SUMS.txt` | 363 | `EBFCCD77549296B1E8C98DC5A6CE2C9359BB2BA78233EB3E0BE28C9294D238FA` |
| `delivery-manifest.json` | 2,038 | `55761053695B788070A0DEF7217E9DA7DBE665591140CF2232723BB99E6C6A74` |

公开后回读 6/6 资产的文件名、大小、GitHub `digest` 和 uploaded 状态，全部与本地候选一致；Latest、远端 `main` 和标签提交同时复核通过。

## 本地镜像

- `.audit/delivery/output/` 已通过同卷暂存和逐项 SHA-256 比较切换为 v0.2.5 六项资产。
- 原 v0.2.4 镜像保留于 `.audit/delivery/output-v0.2.4-pre-v0.2.5-20260929-182522/`。

## 已知边界

- Windows 安装器未配置 Authenticode 产品签名。
- Android 为第三方侧载发行，安装和更新仍由 Android 系统确认。
- v0.2.5 已验证“当前已是最新版”所需策略与自动测试；真实未来更高版本的应用内下载和系统安装确认路径尚无对应 Release 可供端到端验收。
