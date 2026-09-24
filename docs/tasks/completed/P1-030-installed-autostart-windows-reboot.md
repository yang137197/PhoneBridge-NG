# P1-030 安装版自启动收口

状态：已完成。最终需求为随 Windows 启动只启动客户端并进入托盘，不自动连接或挂载；电脑重启后由用户手动点击连接。

## 最终范围

保留当前用户 Startup 文件夹中的 `PhoneBridge NG.lnk` 作为自启动状态源。快捷方式精确指向安装版 `PhoneBridge.Desktop.exe --startup`，默认关闭且只由用户勾选启用。

删除启动自动连接、唯一设备自动选择、启动期配对记录轮询、30/60/150 秒进程恢复、内部恢复参数和对应测试。保留用户手动连接后的正常挂载，以及活动连接遇到短暂网络故障时的既有自动恢复。

## 历史取证

本任务共执行 6 次真实 Windows 重启。第一次证明本机未执行新增 Run 值，因此改用 Startup 快捷方式；后续轮次确认快捷方式能从安装路径隐藏启动客户端。围绕旧自动挂载要求进行的配对目录恢复和进程重试取证保留在审计记录中，但不再驱动产品实现。第 6 次重启在用户明确取消自动挂载要求后终止，没有继续增加重启。

## 最终验证

Release 构建 0 warnings/0 errors，全套 Windows 280/280 通过，Desktop 51/51。验证目录为 `.audit/windows-verification/20260923-121932/`。

最新安装器 SHA-256 为 `210A4492F0A78F91AD46C95C1E9E5F09CBCBD12B023CCF1D78EFB4E50C0EF097`；安装 DLL 与发布 DLL SHA-256 均为 `F0FF0BADC9DAB5F18B8E7128CCC972EDEDBBC6E450E204551EB199D533381C09`。

Samsung 正在共享且 TCP 8273 可达时，安装版 `--startup` 观察 40.661 秒：只有一个隐藏客户端，rclone 为 0，P: 不存在，没有恢复子进程。证据为 `.audit/runs/P1-030/manual-mount-installed-smoke.json`。

验收后已结束测试客户端和 rclone，并恢复测试前的自启动关闭偏好。
