# P1-009 安全模式写入与删除保护验收

日期：2026-09-21。结果：通过限定任务验收。该结论不代表缓存故障恢复、大文件矩阵、三星兼容或整个 MVP 已通过。

## 已完成

- Android 按已配对客户端模式执行写入授权；SAFE 只允许创建新目标和重命名到不存在目标。PUT 使用共享根内暂存、完整接收、同步与最终提交，提交时重新核对授权和目标。
- 普通 SAFE DELETE 保持拒绝。删除控制 API 生成绑定客户端、规范路径和目标快照的 30 秒一次性确认；执行尝试先消费，过期、变化、错误客户端和重放均不能删除。
- Windows 将访问模式传给挂载层。READ_ONLY 使用 VFS cache off；SAFE/READ_WRITE 使用按设备身份固定且受租约保护的 `writes` 缓存。正常卸载在退出 rclone 前核对受认证 `vfs/queue` 与 `vfs/stats`，状态不干净时保留盘符、进程和缓存归属。
- WPF 显示实际访问模式，并提供具体手机路径的删除预览与最终确认；中英文资源同步更新。

## 自动验证

- `scripts/Verify-Windows.ps1` 的最终产物在 `.audit/windows-verification/20260921-103205/`：Release 构建 0 warnings/0 errors；54 项发现、36 项挂载、50 项配对、15 项 Connection、63 项凭据，共 218/218 通过。摘要见 [tests.json](p1-009/tests.json)。
- `scripts/Verify-AndroidApp.ps1` 在最终资源修改后通过 Debug APK、未签名 Release APK、test APK、JVM 单元测试及 Debug/Release Lint；`BUILD SUCCESSFUL in 1m 21s`，123 tasks（30 executed、93 up-to-date）。
- Redmi K40/API 36 的 26 项存储与 19 项服务仪器测试均分别通过。一次完整运行先完成 44 项，最后一个 120 秒期限用例因手机息屏未返回；单独重跑该用例在 121.109 秒通过。因此可确认 45 个用例逐项通过，不能记为一次不中断的 45/45 运行。

## 真机链路

备用 Redmi K40（Android 16/API 36）使用已保存配对恢复严格 session，并由正式 WPF 挂载 `P:` 安全模式。rclone 进程参数实际包含 `--vfs-cache-mode writes`、稳定的设备缓存目录、`--vfs-write-back 0s`、32 GiB 上限、2 GiB 最低剩余空间、168 小时最大年龄和 5 秒轮询；没有 `--read-only`。

Explorer 在 `Music` 中创建目录并重命名为 `p1-009-safe-folder`；创建的 7 字节 RTF 文件最终写回为 `/p1-009-explorer-upload.rtf.rtf`。ADB 在挂载之外独立确认手机端目标存在，证明结果不只停留在 Windows 目录视图。

WPF 删除预览显示精确路径、类型、大小及 30 秒限制。文件确认在等待用户期间过期，执行返回“目标或状态已变化，本次操作未执行”，目标仍存在；重新取得确认后删除成功。随后同一路径完成目录确认删除。ADB 最终回读为 `FILE_ABSENT`、`DIR_ABSENT`。

客户端随后执行安全卸载并显示“当前无挂载盘符”。独立检查确认 `P_PRESENT=False`、`RCLONE_ABSENT`，设备缓存目录仍保留；桌面客户端退出后为 `DESKTOP_ABSENT`。Android 共享服务停止，ADB 转发列表为空。结构化摘要见 [live-chain.json](p1-009/live-chain.json)。

## 未验证与已知限制

- 没有执行 100 MB、1/5/10/20 GB 写入矩阵、视频随机 Seek、大目录或三星设备测试。
- 没有用 Explorer 实测覆盖已有目标；该行为只由服务端和 Windows 自动测试证明为拒绝。没有执行真实断网、磁盘满、rclone 异常退出及重启后的缓存恢复；ADR-021 只定义并实现保留/恢复边界。
- Android UI 的“已配对电脑”列表在部分仪器测试或进程生命周期后可能显示为空；同一 Windows 保存记录仍能通过严格 session 并挂载，说明凭据本身没有丢失，但 UI 刷新问题尚未定位。
- Explorer 在盘符卸载后仍可短暂显示卸载前的缓存列表；独立 `Test-Path P:\` 与进程检查确认实际挂载已消失。

## 下一步

后续任务 [P1-010 断线卸载与自动重连](../tasks/completed/P1-010-auto-reconnect.md)现已完成；当时的下一步是先冻结离线判据、待上传保护和主动停止抑制规则，再实现连接监督器。
