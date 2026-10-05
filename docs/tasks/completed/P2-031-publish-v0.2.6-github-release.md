# P2-031 — 发布 GitHub Release v0.2.6

状态：已完成。日期：2026-10-05。

## 目标

把 P2-030 已验收的 exact-head 六项正式制品发布为 GitHub `v0.2.6` Release，不重新构建或改变二进制。

## 范围

- 发布前实时确认 Latest、远端 `main`、候选 head、标签、Release 和六项本地资产。
- 推送候选分支，仅在可快进时把同一验收 head 推送到 `main`。
- 创建精确绑定候选 head 的注释标签 `v0.2.6`，发布非草稿、非预发布的 Latest Release 并上传六项既有资产。
- 发布后回读 Latest、标签提交、远端 `main`、资产状态、大小和 GitHub SHA-256。

## 不做什么

- 不重新构建、不修改或替换 P2-030 已验收的六项资产。
- 不启动下一版本开发。
- 任一远端引用或资产不一致时停止，不通过覆盖或替换已发布内容掩盖漂移。

## 验收结果

通过。发布前 Latest 仍为完整正式 `v0.2.5`，远端 `main` 为 `360f47fbfff82f267b39f3a68db6b12700dcc68b`，P2-030 最终验收 head `be39e69ab188e3e418a37a36041bd2451f6b5bf5` 是其快进后代；`v0.2.6` 标签和 Release 均不存在，工作树干净，六项资产完整。

候选分支与发布时远端 `main` 均推送到验收 head。注释标签 `v0.2.6` 的标签对象为 `1a33b1abfb30bc9dc29e7d3ee0b16b132517fda4`，剥离后精确指向 `be39e69ab188e3e418a37a36041bd2451f6b5bf5`。

GitHub Release ID 为 `403349675`，非草稿、非预发布并已设为 Latest；6/6 资产均为 uploaded，文件名、大小和 GitHub `digest` 与本地正式镜像逐项一致。

Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.6>

完整记录见 [P2-031 验证](../../audit/P2-031-VALIDATION.md)。
