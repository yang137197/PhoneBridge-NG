# P2-012 — 发布 GitHub Release v0.2.2

状态：已完成。日期：2026-09-28。

## 目标

把 P2-011 已验收的 exact-head 六项正式制品发布为 GitHub `v0.2.2` Release，不重新构建或改变二进制。

## 已完成

- 远端 `main` 快进到 `825d70367e7839c96a6880e9bcb9d1a853d44ab0`。
- 标签 `v0.2.2` 精确绑定同一提交。
- 以草稿上传 Windows 安装器、Android APK、对应源码 ZIP、README、SHA256SUMS 和 delivery manifest。
- 草稿阶段逐项核对 GitHub 文件名、大小、SHA-256 和 uploaded 状态，6/6 一致后公开并设为 Latest。
- 本地 `.audit/delivery/output/` 已切换为同一组 `v0.2.2` 资产；旧 `v0.2.0` 镜像保留为可恢复备份。

## 验收结果

通过。Release 为非草稿、非预发布并已设为 Latest；公开后的六项 GitHub 资产与本地正式交付继续一致。

Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.2>

完整记录见 [P2-012 验证](../../audit/P2-012-VALIDATION.md)。
