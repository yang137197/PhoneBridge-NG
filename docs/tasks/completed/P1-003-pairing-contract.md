# P1-003 配对与凭据生命周期设计

- 状态：已完成设计任务；不表示产品配对已实现。
- 目标：关闭 D-03 的协议设计缺口，为正式 Windows/Android 配对和安全保存确定可验证契约。
- 范围：核对官方密码学/平台存储约束，定义首次确认、身份绑定、一次性授权、过期/重放/撤销、多个电脑的凭据隔离，以及 Keystore 与 DPAPI/Credential Manager 生命周期；给出正常与拒绝测试向量。
- 不做什么：不凭 mDNS/status 信任身份，不照搬上游长期明文密码，不发布或部署，不在本设计任务实现 UI、自动重连或批量改造 Android。
- 涉及文件：ARCHITECTURE、DECISIONS、PROTOCOL、PAIRING、SECURITY、TESTING、tests/contracts/pairing、审计材料与本任务记录。

## 实现

ADR-017 确定一次性 8 位码和 Bouncy Castle J-PAKE 三轮确认，经公有消息绑定稳定 CA 后使用严格 HTTPS 完成手机批准。定义限额、固定帧、每电脑随机 token、DPAPI Pending、Keystore 验证值、故障恢复和撤销。实验 USB 不成为产品配对要求。未改正式 Windows/Android 源码，仅加入独立离线库探针和设计向量。

## 测试

核对官方 RFC、DPAPI/Keystore/Backup 和固定 BC 两端源码。14 项库互操作与 11 项向量检查通过，退出码及合成材料可复核；检查产品源码/二进制保持与 P1-002 一致、上游干净及文档链接。见 [验收报告](../../audit/P1-003-VALIDATION.md)。

## 验收结果

设计限定范围通过，D-03 设计关闭。22 项产品配对验收均未运行；正式配对、安全凭据、UI 与重连仍未完成。下一任务为 P1-004 配对协议核心与帧校验，先实现两端纯协议并互操作，不接手机或网络监听。
