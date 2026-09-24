# Android 配对服务复验

使用专用 `PhoneBridgeP0Api26` / `emulator-5558` 或 `PhoneBridgeP0Api36` / `emulator-5560`。入口逐项核对 AVD、qemu、API 和 boot_completed；不接受真实手机序列号。安装独立 `org.phonebridge.ng` 和 test APK，不替换 P0 `com.phonebridge`。需要本机已有 SDK、AVD、固定 Gradle/JDK/.NET 工具；不下载或提权。

先运行 [Verify-AndroidApp](../../../scripts/Verify-AndroidApp.ps1)；原生测试：

```powershell
& ./.audit/venv/Scripts/python.exe tests/integration/android_pairing/native.py --api 26 --label native-final
```

该入口执行 ServiceTests 和复用的 StoreTests，安装后核对 APK 散列。包含真实 120 秒期限测试，正常耗时约三分钟；600 秒为观察上限。结果进入 `.audit/p1-007/api26-native-final`，已存在目录不覆盖。结束强制停止 NG 测试应用，保留隔离密文和合成文件用于核查。

C# 联动先用固定 .NET 构建 `windows/tests/PhoneBridge.PairingNetworkFixture`（Debug、`-p:RestoreLockedMode=true`）。AVD 用 `-feature -Wifi` 启动，使用模拟以太网 10.0.2.15；这只影响该测试 AVD。PAKE 需要 TCP FIN 半关闭，不能用会同时终止反向流的 ADB forward。模拟器的 `redir` 路由到传统客体地址；当前 API 26 Wi-Fi 网卡为 192.168.232.2 时不可用。控制通道和 HTTPS 可使用 ADB loopback。

```powershell
& ./.audit/venv/Scripts/python.exe tests/integration/android_pairing/bridge.py --api 26 --label bridge-final
```

联动入口只给上述测试应用授予测试存储/通知权限，在 Music 写入固定合成文本。真实 MainActivity 的按钮通过 instrumentation 在主线程点击；没有给产品添加绕过批准的接口。测试专属 BridgeTestRunner 仅在测试 APK 中提供本地控制端口，短码只经匿名管道/TCP 暂存在内存，不进命令行或结果文件。C# 使用真实 PAKE 核心、严格链/SAN 检查、隔离目录下的 CurrentUser DPAPI。结果记录非秘密状态/哈希、不同进程 PID 和已安装 APK 哈希；停止共享后回读监听已关闭，最后移除本次转发。

此复验覆盖批准前拒绝、UI 批准、session 激活、文件读取、离开界面后的 grant 失效、进程重启后 Active 回读、自身撤销及重启后仍拒绝。最后在真实 Service 注入测试配对密文损坏，要求其停止共享并关闭监听；测试 finally 仅恢复合成密文，产品没有自动修复。手动停止后新进程启动不等于系统自动恢复、整机重启或锁屏测试。Windows 存储保持 RevocationPending 并禁止挂载，远端 204 不在测试宿主中冒充完整“忘记设备”产品流程。

失败时先保留结果中的固定 stage 和异常类型再定位；不输出包正文/短码/token/原始异常。测试不会触发 rclone/WinFsp 或 Explorer；也不证明真实 Wi-Fi/mDNS、厂商后台、写入保护或整个 MVP。
