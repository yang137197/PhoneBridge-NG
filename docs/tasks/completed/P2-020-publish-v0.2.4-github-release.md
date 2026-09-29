# P2-020 — 发布 GitHub Release v0.2.4

状态：已完成。日期：2026-09-29。

## 目标

把 P2-019 已验收的 exact-head 六项正式制品发布为 GitHub `v0.2.4` Release，不重新构建或改变二进制。

## 范围

- 发布前动态确认 Latest 仍为正式 `v0.2.3`、远端 `main` 未漂移、`v0.2.4` 标签和 Release 均不存在。
- 核对安装器、APK、对应源码 ZIP、README、SHA256SUMS 和 manifest 的本地文件集合、大小和 SHA-256。
- 把候选分支推送到远端，并仅在 `main` 可快进时把同一验收 head 推送到 `main`。
- 创建精确绑定该 head 的注释标签 `v0.2.4`，创建非草稿、非预发布 Release 并上传六项既有资产。
- 公开后回读 Latest、标签提交、六项 GitHub 资产摘要和本地默认镜像。

## 不做什么

- 不修改产品代码、不重新构建二进制、不覆盖或删除旧 Release。
- 不把全新机器首次配对写成已经通过；该验收继续使用发布后的真实资产独立执行。
- 任一引用、签名、文件集合、大小或摘要不一致时立即停止，不通过强推、改标签或重传不同文件绕过。

## 验收结果

通过。发布前 Latest 仍为正式 `v0.2.3`，远端 `main` 未漂移，`v0.2.4` 标签和 Release 均不存在。候选分支与远端 `main` 快进到 `98e17a182ec61eb6d71382544c3d450a61c1f949`，注释标签 `v0.2.4` 精确绑定同一提交。

GitHub Release 为非草稿、非预发布并已设为 Latest；六项资产的文件名、大小、GitHub `digest` 和 uploaded 状态与本地候选逐项一致。本地 `.audit/delivery/output/` 已切换为同一组 `v0.2.4` 资产，原 `v0.2.3` 镜像保留。

Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.4>

完整记录见 [P2-020 验证](../../audit/P2-020-VALIDATION.md)。
