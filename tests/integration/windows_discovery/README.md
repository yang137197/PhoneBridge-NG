# Windows 发现实机测试

[real_device_discovery.py](real_device_discovery.py) 只用于已明确授权、已清空的 Redmi K40 实验手机。依赖 Phase 0 已安装的固定 P0-009 APK/test APK、ADB 调试授权、手机正常解锁及同 LAN；不安装 APK、不改权限或身份、不执行文件传输或删除。

先运行项目根目录 `scripts/Verify-Windows.ps1`。Python 使用现有 `.audit/venv/Scripts/python.exe`，沿用 P0 的前台测试宿主（实际文件共享通过 Wi-Fi，USB 只控制测试/读取非秘密校验信息）。测试前必须确认没有进行中的用户共享；脚本也会拒绝已运行的前台共享。

输入环境变量：`PHONEBRIDGE_TEST_SERIAL=61c9a964`、`PHONEBRIDGE_TEST_LOCAL_IP`（PC 同 LAN 地址）、`PHONEBRIDGE_TEST_PHONE_IP`（手机当前 WLAN 地址）；可用 `PHONEBRIDGE_DOTNET` 指定 SDK dotnet.exe。不得把凭据放进变量或命令行。

运行 `./.audit/venv/Scripts/python.exe tests/integration/windows_discovery/real_device_discovery.py`。约 95 秒，先启动 C# watcher，再启动既有手机宿主，检查 30 秒内出现、跨扫描轮次不闪烁、停共享后 40 秒观察窗口内撤销，最后等待诊断正常退出。输出位于 `.audit/runs/P1-001/real-<随机 ID>`。

95 秒是诊断时长，40 秒是本次实验观察门槛，均不是产品网络时限承诺。失败保留日志和手机现有样本，先复核 `result.json` 和 host 日志，不重复生成/清理文件。验收报告另列实际时间与失败尝试。
