# P1-043 — 提升正式签名默认交付

状态：已完成。日期：2026-09-23。

## 目标

把 P1-041 生成并经 P1-042 真机验收的正式第三方侧载产物提升为 `.audit/delivery/output` 唯一默认交付，并验证产物、清单、散列和说明一致。

## 范围

- 只提升已经验收的正式 APK、相同 Windows 安装器、README、散列文件和交付清单。
- 在同一卷使用暂存目录和目录重命名切换；失败时恢复原默认交付。
- 独立核对正式 APK签名、文件 SHA-256、manifest、SHA256SUMS 和默认目录文件集合。
- 更新交付与项目状态文档。

## 不做什么

不重新构建二进制；不修改产品功能；不重复真机链路；不发布到应用商店、GitHub Release 或外部位置；不执行安装或卸载。

## 涉及文件

- `.audit/delivery/p1-041-formal/output`：已验收正式源产物。
- `.audit/delivery/output`：提升后的唯一默认交付目录。
- `.audit/runs/P1-043`：本地提升脚本和机器可读证据。
- 本任务记录、验收报告及交付状态文档。

## 实现

- 对 P1-041 正式源目录先核对精确文件集合、manifest 发行类型、APK/安装器/README 散列、SHA256SUMS 和 APK证书。
- 将五个正式文件复制到 `.audit/delivery` 同卷唯一暂存目录，复核后通过目录重命名切换默认 `output`。
- 新默认目录复核成功后删除临时备份；正式隔离源继续保留为已验证回滚副本。

## 测试

- 提升前后文件集合和 SHA-256 已记录到 `.audit/runs/P1-043/promotion.json`。
- 新默认目录只有五个预期文件，不含 `local-test` APK。
- manifest 为 `third-party-sideload` / `third-party-release`，APK证书摘要匹配正式长期身份。
- manifest、SHA256SUMS、APK、安装器和 README 的散列全部一致。
- 暂存和临时备份目录均不存在，正式隔离源仍存在。

## 验收结果

已验收的正式签名产物已成为 `.audit/delivery/output` 唯一默认交付。该目录可用于第三方侧载；对外分发仍需同时准备完整对应源码及许可材料。
