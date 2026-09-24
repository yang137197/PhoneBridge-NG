# P1-010 锁屏共享与自动恢复验收

日期：2026-09-21。结果：通过限定任务验收。该结论只覆盖 Redmi K40/API 36、当前 Windows 11 开发机、小型合成文件和一次 Wi-Fi 中断，不代表长期运行、大文件恢复、三星兼容或整个 MVP 已通过。

## 已完成

- Android 前台共享服务持有由服务生命周期明确释放的非超时部分 WakeLock 与 MulticastLock，去掉旧六小时静默到期。
- 首轮真机失败推翻原设计后，开始共享增加电池优化授权门禁：只打开 Android 官方、绑定当前包的确认界面；返回后重新检查，拒绝或无法打开则保持停止。
- Windows 增加严格 session 健康检查、三次失败去抖、安全卸载、同一 `device_id` 恢复、严格 CA/凭据/模式复验、2/5/10/20/30 秒退避和主动停止抑制。

## 自动验证

- `scripts/Verify-Windows.ps1` 最终产物位于 `.audit/windows-verification/20260921-111641/`：Release 构建 0 warnings/0 errors；54 项发现、36 项挂载、50 项配对、18 项 Connection、63 项凭据，共 221/221 通过。较早的 `.audit/windows-verification/20260921-111559/` 因新增测试使用无效 client ID 失败，测试数据修正后全量重跑通过，失败证据保留。
- `scripts/Verify-AndroidApp.ps1` 最终完成 Debug APK、未签名 Release APK、test APK、JVM 单元测试及 Debug/Release Lint；`BUILD SUCCESSFUL in 1m 17s`，123 tasks（49 executed、74 up-to-date）。第一次加入授权流程后 Lint 如实失败 4 项：直接豁免用途、两条缺失英文资源和无效 SDK 版本判断；补齐英文、移除无效判断，并以 ADR-022 的真机核心功能失败证据对 BatteryLife 作精确抑制后重跑通过。
- Redmi K40/API 36 上，`batteryOptimizationRequestTargetsOnlyThisPackage` 与 `sharingRuntimeLocksRemainHeldUntilExplicitClose` 两项仪器测试通过。

## 真机链路

未豁免电池优化时，手机锁屏后系统为 Dozing/Keyguard 显示，PhoneBridge WakeLock 被标为 `DISABLED`；手机内部仍显示 `wlan0` 地址与 8273 监听，但 Windows 的 ping/TCP 均不可达。连续三次健康检查后 P: 消失，rclone 退出，WPF 显示等待同一已配对手机重新出现。这一轮是修复前失败证据。

安装修复版后，用户通过系统确认允许忽略电池优化并开始共享。Windows 在此前安全卸载后自动恢复原 P:，严格 session 显示正常。第二轮锁屏中，系统仍为 Dozing、`SCREEN_STATE_OFF` 且 Keyguard 显示，但 PhoneBridge WakeLock 保持 `ACQ`，8273 可达。跨三个健康检查周期后，P: 成功读取既有合成文件并写入 `/phonebridge-p1-010-lockscreen.txt`；电脑回读、ADB 独立手机回读与 SHA-256 一致：`d56efab04e7ccb6a3ebec8726a49e636a9353eb92690d9fe3ce5ace41594906b`。

保持手机锁屏关闭 Wi-Fi 后，约 18 秒回读 P: 不存在、rclone 数量为 0，桌面客户端仍在并等待相同身份。重新打开 Wi-Fi 后，手机恢复原地址，WakeLock 继续保持；Windows 自动恢复 P: 和单个 rclone，再次读取上述文件和哈希成功。随后通过 WPF 安全卸载并关闭客户端，ADB 停止测试共享；最终 P:、rclone、桌面进程、Android 服务和活动 WakeLock 均不存在。结构化摘要见 [live-chain.json](p1-010/live-chain.json)。

## 未验证与已知限制

- 没有验证 Samsung Galaxy S23 Ultra、其他 OEM、电池优化设置被用户撤销后的在途行为、数小时/数天锁屏或系统杀进程。
- 没有验证手机 IP 改变、路由器重连、大文件传输中断、磁盘满或待上传缓存恢复；本次断网前已确认小文件落盘。PC 睡眠或设备重启期间不要求继续传输，若后续执行只核对中断安全、唤醒/重启后恢复和无僵尸资源。
- 自动恢复意图只保留在当前桌面进程生命周期；托盘、自启与开机后自动恢复尚未实现。
- 电池优化豁免会增加耗电，用户已明确接受共享期间的这一代价。项目只作第三方 App 分发，不安排品牌应用商店审核；直接请求仍以核心局域网服务在 API 36 真机锁屏后不可达的证据作精确 Lint 抑制。

## 下一步

后续任务 [P1-011 Samsung 真机兼容验收](../tasks/completed/P1-011-samsung-acceptance.md)已按独立范围完成；其三星证据不回填为 P1-010 的 Redmi 证据。
