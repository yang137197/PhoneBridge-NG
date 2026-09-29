# P2-024 — 发布 GitHub Release v0.2.5

状态：已完成。日期：2026-09-29。

## 目标

把 P2-023 已验收的 exact-head 六项正式制品发布为 GitHub `v0.2.5` Release，不重新构建或改变二进制。

## 范围

- 发布前实时确认 Latest、远端 `main`、候选 head、标签、Release 和六项本地资产。
- 推送候选分支，仅在可快进时把同一验收 head 推送到 `main`。
- 创建精确绑定候选 head 的注释标签 `v0.2.5`，发布非草稿、非预发布的正式 Release 并上传六项既有资产。
- 发布后回读 Latest、标签提交、远端 `main`、资产状态、大小和 GitHub SHA-256。

## 不做什么

- 不重新构建、不修改或替换 P2-023 已验收的六项资产。
- 不强推、不移动标签、不删除或覆盖历史 Release。
- 任一远端引用或资产不一致时停止，不通过重试掩盖漂移。

## 验收结果

通过。发布前 Latest 仍为正式 `v0.2.4`，远端 `main` 为 `62d6b06cbc634d3baf57a486db7ce6948eab78f7`，本地验收 head `11a58c51203e62ab77443bea15403ad7609e945e` 是其快进后代；`v0.2.5` 标签和 Release 均不存在，工作树干净，六项资产完整。

候选分支与远端 `main` 均推送到验收 head。注释标签 `v0.2.5` 的标签对象为 `8091afbe4f58433fd636ce90da26ec2bf7f12c92`，剥离后精确指向 `11a58c51203e62ab77443bea15403ad7609e945e`。

GitHub Release ID 为 `399034507`，非草稿、非预发布并已设为 Latest；6/6 资产均为 uploaded，文件名、大小和 GitHub `digest` 与本地候选逐项一致。本地 `.audit/delivery/output/` 已切换为同一组 v0.2.5 资产，原 v0.2.4 镜像保留。

Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.5>

完整记录见 [P2-024 验证](../../audit/P2-024-VALIDATION.md)。
