# P2-012 验证记录 — GitHub Release v0.2.2

日期：2026-09-28。结果：通过。

## 发布身份

- Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.2>
- 标签：`v0.2.2`
- 标签提交：`825d70367e7839c96a6880e9bcb9d1a853d44ab0`
- 发布时远端 `main`：同一提交。
- 状态：非草稿、非预发布、Latest。
- 发布时间：`2026-09-28T03:55:34Z`。

## 资产回读

| 资产 | 字节 | GitHub SHA-256 |
| --- | ---: | --- |
| `PhoneBridge-NG-Setup-0.2.2.exe` | 81,144,097 | `246C5280812006129A0E77E2B1FB0440A634C6021608D90856AA6B67EB2960CE` |
| `PhoneBridge-NG-0.2.2.apk` | 3,508,478 | `908A7C93D5ABE750702E470F82A58555A03B201F9F85ABBD985382BF8DC6E1E4` |
| `PhoneBridge-NG-0.2.2-source.zip` | 1,013,578 | `C948CA3CC1B56E19BA2BD8A8E674B5763ED6DC181E67FBEEA8F246D1014DC0FD` |
| `README.txt` | 4,028 | `662491B5962BBE51CF8E9EF8488C25BFAB25DEFC4F78450DA236DEAC5AE05F88` |
| `SHA256SUMS.txt` | 363 | `CF09669D555408AB0C6DE40385EECCA201BC96882DEE921FDBD4898B877BF305` |
| `delivery-manifest.json` | 1,984 | `8C849A3033FD1E0A55718D05A5769FA2FFA25BE6E2742BEB33086E914316796B` |

草稿阶段 6/6 资产的名称、大小、GitHub `digest` 和 uploaded 状态均与本地匹配后才公开；公开后再次回读 Release 状态、Latest 列表、远端 `main` 和标签提交。

## 本地镜像

- `.audit/delivery/output/` 已逐文件复制并比较摘要后切换为同一组 `v0.2.2` 资产。
- 旧镜像保留于 `.audit/delivery/output-v0.2.0-pre-v0.2.2-20260928/`，未删除。

## 已知边界

- Windows 安装器仍未做 Authenticode 产品签名。
- Release 不提供自动更新，也不向应用商店发布。
- 全新机器首次安装、首次配对、备注卷名和完整盘符冲突矩阵仍由 P2-013 独立验收，不能因 Release 已发布写成通过。
