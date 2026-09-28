# P2-009 验证记录 — v0.2.1 挂载状态一致性

日期：2026-09-28。分支：`codex/v0.2.1-v0.2.2`。

## 已确认

- Windows 桌面版本为 `0.2.1`；底部状态由全部设备会话聚合，不再读取单一选中会话。
- 已配对设备行使用稳定 `device_id`；候选实例 ID 变化不改变保存设备行身份。
- 候选消失只加速严格会话检查，不直接授权卸载；成功复核会恢复普通三次失败策略。
- `share-not-ready` 和达到门槛的暂时性失败都复用安全卸载与同身份重连路径。
- 挂载管理器只在实际盘符缺席后调用 Shell 移除通知；盘符仍存在时保持 `StopFailed` 且不通知。

## 自动验证

```text
.audit/tools/dotnet-10.0.401/dotnet.exe build windows/PhoneBridge.Windows.slnx --configuration Release --no-restore
成功：0 warnings，0 errors

.audit/tools/dotnet-10.0.401/dotnet.exe test windows/PhoneBridge.Windows.slnx --configuration Release --no-build --no-restore
通过：303；失败：0；跳过：0
```

分项：Discovery 54、Pairing 50、Credentials 66、Mounting 46、Connection 26、Desktop 61。

首轮完整测试曾由 `LingeringDriveCannotBeReportedStopped` 发现通知状态没有跨卸载失败重试保留；修复后该测试同时证明失败时 0 次通知、确认移除后恰好 1 次通知，随后全量门禁通过。

## 尚未验证

- 真实 Android 停止共享后的端到端卸载时延。
- 真实 Windows Explorer 对 `SHCNE_DRIVEREMOVED` 的可见刷新效果。
- 真实待上传写缓存下的 UI 文案；安全保留行为由既有状态机和自动测试覆盖，未冒充真机证据。

## 边界

本任务没有创建安装器、APK、正式候选、标签或 GitHub Release，也没有修改 `v0.2.0` 正式发布资产。
