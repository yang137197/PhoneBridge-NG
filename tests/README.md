# 测试目录

P1-001 已有 Windows 发现模块的 41 项行为测试和[真机发现脚本](integration/windows_discovery/README.md)，无覆盖率或 MVP 全量通过声明。运行入口为项目根目录 `scripts/Verify-Windows.ps1`；完整结果见 [P1-001](../docs/audit/P1-001-VALIDATION.md)。

P0-003 已加入[原始链路实验脚本](integration/upstream_chain/README.md)，实际在空白模拟器、rclone 与 WinFsp 运行，结果失败；详见[实测报告](../docs/audit/P0-003-VALIDATION.md)。

按 [TESTING](../docs/TESTING.md) 建立与实现相关的单元、协议、集成和设备测试。Android 模拟器、Galaxy S23 Ultra 真机、mock 进程和真实 Explorer 结果必须分别记录。

已有上游测试和离线复现证据见 [audit/VALIDATION](../docs/audit/VALIDATION.md)；它们不能替代本项目的安全、大文件或端到端验收。

P0-005 已运行文件操作/真实 HTTPS/实际 P: 复制；当前 P0-007 通过构建、20 个单元、Lint 及 API 26/36 的相关应用断言。准确结果、工具入口退出 1 的原因、独立清理回读及未验证项见 [P0-007](../docs/audit/P0-007-VALIDATION.md)。仍不是 NG 产品全量测试套件。

P0-008 新增授权备用机只读网络探针，已实测 mDNS 和真实 rclone TLS 拒绝；不含认证/文件传输。TLS 失败证据见 [P0-008](../docs/audit/P0-008-VALIDATION.md)，入口退出 0 不代表被测 rclone 通过。

P0-009 新增 `tls_instrumentation.py` 和 `tls_runtime.py`，使用真实 Android Keystore/TLS sockets 和 rclone 专属 ca-cert。25 单元通过，API 26/36/授权备用机各 8 项原生通过，模拟器与真机的实际服务验证通过，手机真实 LAN 成功。完整状态与历次工具失败见 [P0-009](../docs/audit/P0-009-VALIDATION.md)。该限定 TLS 任务已验收，P0-010 已完成真机最小文件链路，详见[记录](../docs/audit/P0-010-VALIDATION.md)。
