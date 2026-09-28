# P2-014 验证记录 — v0.2.3 有界自动重连

日期：2026-09-28。结果：实现与自动验证通过；未做安装版或真机验收。

## 已确认根因

- `ReconnectPolicy` 原来只把退避增加到 30 秒，之后无限按 30 秒继续，没有次数或总时长上限。
- 自动候选消失后监督器不再发起连接，但界面会保留上一条倒计时，造成仍在固定重试的误导。
- Android 主动停止共享会关闭服务和 mDNS，Windows 通常只能看到传输失败或候选消失；当前协议不能可靠区分主动停止和普通网络中断。

## 已实现

- 每个设备会话的单次恢复轮次最多开始 6 次实际连接，且最多持续 2 分钟；没有同身份端点时只等待，不消耗次数。
- 退避保持为首次立即尝试，之后等待 2/5/10/20/30 秒；第 6 次失败后不再安排新尝试。
- 重连成功后保留原恢复预算，连续三个 5 秒健康周期通过后才清零；期间再次掉线继续原轮次。
- 活动或恢复连接明确收到 `share-not-ready` 时清除恢复意图，安全停止并提示用户重新共享后手动连接。
- 候选消失后显示有限等待，不保留旧倒计时；次数或时间耗尽后显示自动重连已停止，并通过现有安全停止释放可释放的盘符预留。
- 恢复尝试失败不会误释放恢复轮次已经持有的原盘符预留；首次连接新取得的预留仍在连接失败后释放。
- 安全停止仍服从既有脏缓存规则；待上传未确认时保留 rclone、盘符、缓存和所有权，不强制清理。
- Windows 版本更新为 `0.2.3`；Android 只更新 `versionCode 5` / `versionName 0.2.3`，没有改变手机运行逻辑。

## 自动验证

- 定向 `PhoneBridge.Connection.Tests`：29/29 通过。
- `scripts/Verify-Windows.ps1`：最终 locked restore、Release 构建 0 警告/0 错误，308/308 通过；TRX 位于 `.audit/windows-verification/20260928-122618/`。较早的 `.audit/windows-verification/20260928-121831/` 也通过，但最终记录包含恢复轮次盘符预留修正。
- `scripts/Verify-AndroidApp.ps1`：`BUILD SUCCESSFUL in 1m 53s`，123 tasks（29 executed、94 up-to-date），覆盖 Debug/Release/test APK、JVM 单元测试及 Debug/Release Lint。
- 构建后回读 Windows EXE 为文件版本 `0.2.3.0`、产品版本 `0.2.3`；未签名 Release APK manifest 为 `versionCode 5`、`versionName 0.2.3`、minSdk 26、targetSdk 36。该回读只验证构建元数据，不是正式签名制品。
- `git diff --check` 通过；输出仅有仓库现有 Windows 行尾转换提示。

## 未验证与已知边界

- 没有生成或安装 v0.2.3 安装候选，没有覆盖当前正式 v0.2.2。
- 没有实测手机主动停止后 2 分钟终止、候选重新出现后不自动挂载、用户手动恢复，或短暂 Wi-Fi 中断在预算内自动恢复原盘符。
- 没有真实制造待上传缓存后耗尽预算；数据保护结论来自未改动的安全停止实现与既有回归，不冒充本轮真机证据。
- v0.2.2 的全新机器首次配对、备注卷名和完整盘符矩阵未执行。P2-013 已按未执行关闭，不能记为通过。
- GitHub Latest、`.audit/delivery/output/` 和 `scripts/New-SourceDelivery.ps1` 仍绑定正式 v0.2.2；本任务不发布、不改标签、不生成正式制品。

## 下一步

随后任务 [P2-015 v0.2.3 安装候选与有界重连实机验收](../tasks/completed/P2-015-v0.2.3-installed-reconnect-acceptance.md) 已完成。
