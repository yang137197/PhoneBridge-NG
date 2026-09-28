# P2-016 验证记录 — GitHub Release v0.2.3

日期：2026-09-28。结果：通过。

## 发布前门禁

- 发布前 GitHub Latest 仍为正式 `v0.2.2`，非草稿、非预发布；远端 `main` 为 `2ac42b3bed6a4a632b78bfb5a24ec7ad322ffff5`。
- `v0.2.3` 标签和 Release 均不存在；本地工作树干净，候选提交 `927135e3b51a901076b067378658e708ae805d75` 是远端 `main` 的快进后代。
- P2-015 六项候选制品、安装版升级和真实有界重连验收均已通过；本任务没有重新构建二进制。

## 发布身份

- Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.3>
- Release ID：`398054793`
- 标签：`v0.2.3`
- 标签提交：`927135e3b51a901076b067378658e708ae805d75`
- 发布时远端 `main`：同一提交。
- 状态：非草稿、非预发布、Latest。
- 发布时间：`2026-09-28T08:12:46Z`。

## 资产回读

| 资产 | 字节 | GitHub SHA-256 |
| --- | ---: | --- |
| `PhoneBridge-NG-Setup-0.2.3.exe` | 81,156,605 | `FF86F6EB412F811372B1CF36B44939CBBAFF78EB5390F031B5F86740390ED76A` |
| `PhoneBridge-NG-0.2.3.apk` | 3,508,478 | `E6965EE66289339E5AB313FEC5EBF059A49EA68B45BCFCF48337C0D9861E68B6` |
| `PhoneBridge-NG-0.2.3-source.zip` | 1,017,699 | `97E1C852C7A1259F517F4995E4A11AF53E64AB44C3B21CB42260BC454C8E6A9F` |
| `README.txt` | 4,028 | `2C17661F1E1F091169D0EA13F180FEA8DD01ED6808FB656947B47E80274A6392` |
| `SHA256SUMS.txt` | 363 | `41CF34713EB8BECD329CF1CD3112F063C6A671845ABD21874ACF273F6AB82732` |
| `delivery-manifest.json` | 1,984 | `D91B291680571EEFDBD2A3C6E9A547CDEB6926DBE63560264596511CC06EE5B5` |

草稿阶段先回读 6/6 资产的文件名、大小、GitHub `digest` 和 uploaded 状态；全部与本地候选一致后才公开。公开后再次回读 Latest、Release 状态、六项资产、远端 `main` 和标签提交，结果仍一致。

## 本地镜像

- `.audit/delivery/output/` 已通过独立暂存目录逐文件比较摘要后切换为同一组 `v0.2.3` 资产。
- 原 `v0.2.2` 镜像保留于 `.audit/delivery/output-v0.2.2-pre-v0.2.3-20260928-161319/`，未删除。

## 已知边界

- Windows 安装器仍未做 Authenticode 产品签名。
- Release 不提供自动更新，也不向应用商店发布。
- 全新 Windows 首次安装、全新电脑配对、备注卷名和 USB/多盘符人工矩阵尚未执行，由 P2-017 独立验收；不能因 Release 已发布写成通过。
