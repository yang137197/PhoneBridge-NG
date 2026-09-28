# P2-016 — 发布 GitHub Release v0.2.3

状态：已完成。日期：2026-09-28。

## 目标

把 P2-015 已验收的 exact-head 六项正式制品发布为 GitHub `v0.2.3` Release，不重新构建或改变二进制。

## 已完成

- 发布前动态确认 Latest 仍为正式 `v0.2.2`，远端 `main` 未漂移，`v0.2.3` 标签和 Release 均不存在。
- 远端 `main` 快进到 `927135e3b51a901076b067378658e708ae805d75`，带注释标签 `v0.2.3` 精确绑定同一提交。
- 以草稿上传 Windows 安装器、Android APK、对应源码 ZIP、README、SHA256SUMS 和 delivery manifest。
- 草稿阶段逐项核对 GitHub 文件名、大小、SHA-256 和 uploaded 状态，6/6 一致后公开并设为 Latest。
- 公开后重新核对 Release 状态、六项资产、远端 `main` 和标签提交；本地正式交付镜像也已切换为同一组制品，原 v0.2.2 镜像保留。

## 验收结果

通过。Release 为非草稿、非预发布并已设为 Latest；公开后的六项 GitHub 资产与本地正式交付继续一致。

Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.3>

完整记录见 [P2-016 验证](../../audit/P2-016-VALIDATION.md)。
