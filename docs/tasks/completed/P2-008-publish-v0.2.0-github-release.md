# P2-008 — 发布 GitHub Release v0.2.0

状态：已完成。日期：2026-09-26。

## 目标

把 P2-007 已验收的 exact-head `0.2.0` 六项正式制品发布为 GitHub Release，不重新构建、不改变二进制、不扩大功能范围。

## 范围

- 标签 `v0.2.0` 精确绑定提交 `60c52b729f2052fa933870bfc175df01d78f17be`。
- 上传 Windows 安装器、Android APK、对应源码 ZIP、README、SHA256SUMS 和 delivery manifest。
- 先以草稿上传并核验 GitHub 端资产摘要，再公开并设为 Latest。

## 验收结果

通过。Release 为非草稿、非预发布并已设为 Latest；6/6 资产均为 uploaded，GitHub 端大小与 SHA-256 全部匹配本地正式交付。

Release：<https://github.com/yang137197/PhoneBridge-NG/releases/tag/v0.2.0>
