# Android 配对存储原生复验

先运行根目录 `scripts/Verify-AndroidCredentials.ps1`。原生测试仅接受已启动且完成开机的两个专用 AVD：API 26 的 PhoneBridgeP0Api26 / emulator-5558，API 36 的 PhoneBridgeP0Api36 / emulator-5560。脚本复核 qemu、AVD 名称、API 和开机状态；不会自动启动、清空模拟器或访问真机。

在项目根目录，以已配置的 Python 执行：

```powershell
& .audit/venv/Scripts/python.exe tests/integration/android_credentials/run.py --api 26 --label review
& .audit/venv/Scripts/python.exe tests/integration/android_credentials/run.py --api 36 --label review
```

分别运行，低内存主机只启动一个 AVD。每个 label 对应全新的 `.audit/p1-006/api<版本>-<label>`，禁止覆盖旧证据。ADB 与测试 APK 路径固定在本地工具/构建目录；只安装 `org.phonebridge.credentials.test`，校验安装后 APK SHA-256。它使用真实 Keystore、app 私有文件和合成 token，无 HTTP 监听或共享用户文件。

26 项 JUnit 后运行 3 个独立进程场景：正常撤销后重开；临时密文 sync 后、rename 前杀进程；rename 后杀进程。恢复分别须为 Revoked / Active / Revoked，验证成功授权或拒绝和新进程 PID。文件更新前杀进程不等同撤销成功；杀进程不等同断电。工具退出前停止所属测试包，密文测试文件留在该包私有空间内。

硬链接用例记录实际分支：系统拒绝创建或库拒绝多链接记录。API 26/36 当前都为平台拒绝，不能据此声称运行覆盖了库的 nlink>1 分支。句柄用例重复打开 20 次，验证持锁时恰好两个所属句柄且均 CLOEXEC，释放后为零。

测试用例、合成 token 和进程 runner 不进入产品 AAR；库保留 internal checkpoint 钩子，公开工厂固定禁用。输入/权限/密文破坏测试仅作用于独立测试包随机目录。结果和首次失败分别见 [P1-006 验收](../../../docs/audit/P1-006-VALIDATION.md)。
