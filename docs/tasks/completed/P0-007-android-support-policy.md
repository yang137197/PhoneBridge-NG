# P0-007 Android 支持版本与前台共享方案

- 状态：已完成；限定版本/生命周期任务，不代表 Phase 0 全部通过
- 目标：解决 D-02，依据官方规则及现代 Android 运行证据确定最低/目标版本和前台共享服务方案。
- 范围：核对 Android 15/16 对前台服务类型、时间限制、后台/重启启动的要求；在专用 API 36 模拟器验证当前实现与适用的最小修正，写明可承诺和需要用户操作的行为。
- 不做什么：不通过降低 targetSdk、关闭系统保护或厂商私有 hack 绕过限制；不将模拟器视为红米/三星验收，不发布 APK、不开始 WPF 重写。
- 涉及文件：官方依据、DECISIONS/ARCHITECTURE/TESTING、独立实验副本与必要最小补丁及测试；先记录复现/约束再修改。

## 实现

方案先记录于 ARCHITECTURE，再以独立副本 `.audit/p0-007-worktree` 修改。保留 minSdk 26，compile/target 36；改用 connectedDevice 前台共享，处理 sticky 服务 null intent、停止/查询空服务、系统栏 Insets。依赖与文件协议/存储代码未变。对应 ADR-013，D-02 已解决。

## 测试

先在 API 36 的 P0-006 APK 复现进程重建不恢复共享与缩短 dataSync 时限后的崩溃。最终构建和 20 个 JVM 单元通过，Lint 0 errors/126 warnings、无新增诊断。API 26/36 各 16 个原生通过、1 个硬链接跳过，各 11 项运行检查及 2 项失败启动通过；API 36 另有 7 项服务/实际重启断言通过。

生命周期入口最终 UI 清理失败退出 1，finally 恢复已独立回读通过；不将该入口记为整体通过。最后移除重复 UI 清理的完整序列未重跑。storage_scope、stop_android 均成功，主机回读确认模拟器、转发、P: 已退出，保留缓存。

## 验收结果

产物、审核、官方来源、工具问题和限制见 [P0-007 验收](../../audit/P0-007-VALIDATION.md)。通过范围限于专用 AOSP；未证明真机、Doze、长期锁屏、全部 Android 版本或 MVP。用户已授权空白备用机测试，ADB 只读确认 Android 16/API 36 与型号；当前未安装真机 APK。

下一任务仅为 [P0-008 真机同 Wi-Fi 链路](P0-008-real-device-chain.md)。不开始 WPF 重写。
