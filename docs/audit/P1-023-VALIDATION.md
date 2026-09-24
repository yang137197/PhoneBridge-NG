# P1-023 三种访问模式与用户设置验收

日期：2026-09-22。结果：通过限定范围。

## 实现结果

Android 已配对电脑列表为每台电脑提供只读、安全和完全读写选择。选择内容直接调用既有 `PairingStore.updateMode`，持久提交后关闭该电脑的旧连接；下一次严格 session 返回新模式。默认仍为安全模式，没有新增协议或权限类型。

Windows 设备列表增加“手机确认模式”，挂载状态继续显示实际挂载模式。已挂载期间检测到手机模式变化时，客户端先保存经过 TLS 和凭据验证的 session 模式，再安全卸载旧盘符并提示重新连接；不能由 Windows 本地提高权限。

## Samsung 真机结果

Samsung SM-S9180/API 36 在 `Music/PhoneBridge-P1-023` 专用目录完成一次三模式验证：

- 安全模式成功创建新文件；覆盖既有文件时 PowerShell 调用仍未报告远端冲突，但 Android 原文件哈希保持 `ed6ec5cc...28d6`，没有替换落盘。
- 只读模式下 Windows 创建文件被拒绝，Android 独立检查确认目标不存在。
- 完全读写模式覆盖既有文件成功，Android 独立回读内容为 `readwrite-replacement`，SHA-256 为 `8f70c445...67ac5`。
- 每次手机切换模式后旧 P: 均自动卸载；Windows 行显示新的手机确认模式，重新连接后的状态分别显示只读、完全读写和最终恢复的安全模式。

结构化摘要见[真机证据](p1-023/access-modes-live.json)。测试数据均为合成小文件。

## 自动化验证

- 固定 Android 工具链的 Debug/Release、test APK、单元测试和 Debug/Release Lint 通过。
- Samsung 上定向 instrumentation 1/1 通过，覆盖模式持久化、旧连接关闭、只读拒绝写入及完全读写覆盖。
- Windows Connection 18/18、Desktop 50/50；完整 `Verify-Windows.ps1` 为 279/279，Release 构建 0 warnings、0 errors。

## 限制与清理

未重复大文件、视频、断网或多设备测试；这些与模式入口无直接关系。安全模式覆盖冲突仍存在 WinFsp/rclone 关闭错误不回传给 PowerShell 的界面限制，但最终手机与重新读取的盘符保持原文件，P1-022 已消除脏缓存假象。

最终模式恢复为安全模式。专用手机目录、共享服务、监听端口、P:、rclone 和 Windows 客户端进程均已清理。唯一下一任务为 P1-024 本地可安装交付包。
