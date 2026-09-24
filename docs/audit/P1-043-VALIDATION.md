# P1-043 提升正式签名默认交付验收

日期：2026-09-23。结果：通过。

## 已确认

- 提升源为 P1-041 构建并经 P1-042 Samsung 真机验收的 `.audit/delivery/p1-041-formal/output`，没有重新构建二进制。
- 提升前默认目录包含 `PhoneBridge-NG-0.1.0-local-test.apk`；提升后默认目录只包含正式 `PhoneBridge-NG-0.1.0.apk`、Windows 安装器、README、SHA256SUMS 和 manifest 五个文件。
- 默认 `delivery-manifest.json` 的 `release_kind` 为 `third-party-sideload`，Android `signing_mode` 为 `third-party-release`。
- 默认 APK SHA-256 为 `E53F618DB6888F14811A98BBC0EFD7965D72E44E84CE28D27C5840C427B1F0C3`，`apksigner` 证书 SHA-256 为 `66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4`。
- Windows 安装器 SHA-256 保持 `B357083FD1B53491BE9F7C2E1378450B31BAC7FBA47EF63363D39F9158F280A1`；README 与当前 `docs/INSTALL_LOCAL.txt` 一致。
- manifest、SHA256SUMS 和三个交付文件的散列互相一致；暂存和备份目录均已删除，隔离正式源仍保留用于回滚。

机器可读证据：`.audit/runs/P1-043/promotion.json`。

## 提升方式

先把五个正式文件复制到 `.audit/delivery` 内的唯一暂存目录并完成同样校验，再把旧 `output` 重命名为临时备份、把暂存目录重命名为新 `output`。新默认目录复核通过后删除临时备份；任一异常会在目标缺失时恢复旧目录。

## 未验证与边界

- 本任务没有重新构建、安装或重复真机链路；二进制运行证据沿用 P1-041/P1-042 对同一 SHA-256 产物的直接证据。
- Windows 安装器仍没有 Authenticode 产品签名，会显示未知发布者。
- 真实无 WinFsp 的干净 Windows 首装仍缺独立机器证据。
- 对外分发时仍必须同时提供该版本完整对应源码及 GPL/第三方许可材料；本任务只提升现有二进制交付入口。
