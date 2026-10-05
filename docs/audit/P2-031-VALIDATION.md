# P2-031 验证记录 — GitHub Release v0.2.6

日期：2026-10-05。结果：通过。

## 发布前门禁

- GitHub Latest 为正式 `v0.2.5`，非草稿、非预发布且有六项完整资产。
- 远端 `main` 为 `360f47fbfff82f267b39f3a68db6b12700dcc68b`；P2-030 最终验收 head 为 `be39e69ab188e3e418a37a36041bd2451f6b5bf5`，可从远端 `main` 快进。
- 本地工作树干净；本地和远端均无 `v0.2.6` 标签，GitHub 无 `v0.2.6` Release。
- 六项候选资产的文件集、大小和 SHA-256 已在发布前重新核对；源码 ZIP 中的 README、开发规则、交接和用户指南与候选 head 内容一致。

## 发布身份

- Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.6>
- Release ID：`403349675`
- 标签：`v0.2.6`
- 注释标签对象：`1a33b1abfb30bc9dc29e7d3ee0b16b132517fda4`
- 标签提交：`be39e69ab188e3e418a37a36041bd2451f6b5bf5`
- 发布时远端 `main`：同一提交。
- 状态：非草稿、非预发布、Latest。
- 发布时间：`2026-10-05T03:50:24Z`。

## 资产回读

| 资产 | 字节 | GitHub SHA-256 |
| --- | ---: | --- |
| `delivery-manifest.json` | 2038 | `161005d561be5b0d692f6ef49e150e621e4e667bc0a6eeda86e4bf81b0dbc363` |
| `PhoneBridge-NG-0.2.6-source.zip` | 1074845 | `b057e4ca9ffbb94e4897fa93315bd387ee9f2eca1089b1c9ecbc072c1ff23c02` |
| `PhoneBridge-NG-0.2.6.apk` | 3533110 | `fcba3c719976e8c9d5399bfb1c464551be9800d8b703d367ab94ed4d598eb2f9` |
| `PhoneBridge-NG-Setup-0.2.6.exe` | 81179557 | `55b979feb1888c0415137ecfca79b551e2c055201e22736c9389f80d9bf4c155` |
| `README.txt` | 4028 | `8e763558c8350f0b0f58f5922e94b1af971c9bf9039f9474828e4cf925145df8` |
| `SHA256SUMS.txt` | 363 | `162aad9bf3d2c82c2cd4b297d2b317eafd62936c79ca083fd95a90582caf9ad2` |

6/6 资产状态均为 `uploaded`；文件名、字节数和 GitHub `digest` 与 `.audit/delivery/output/` 逐项一致。

## 结论

`v0.2.6` 已以 P2-030 最终验收 head 正式发布，标签、发布时 `main`、Latest 和六项公开资产均已回读一致。发布后只新增本记录及仓库状态说明，未替换标签或已发布资产。
