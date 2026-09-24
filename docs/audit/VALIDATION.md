# P0-001 验证记录

日期：2026-09-19。上游提交：`a378fec40561a4d18be2334f4de92ee02a0e7d0c`。

## 环境与隔离

- Windows 本机；使用 Codex bundled Python 3.12.14 创建项目内 `.audit/venv`，只额外安装 pytest/zeroconf 及其依赖，未安装完整 GUI/打包依赖。
- 实际版本见 [requirements-audit.txt](requirements-audit.txt)。这是审计环境快照，不是产品依赖方案。
- 设置 `PYTHONUTF8=1`、`PYTHONDONTWRITEBYTECODE=1`、`PYTEST_DISABLE_PLUGIN_AUTOLOAD=1`；关闭 pytest cache provider。
- 上游测试进程的 APPDATA 指向 `.audit/test-appdata`，避免读写已有 PhoneBridge 配置；自定义复现使用临时目录和纯合成凭据。
- 上游测试中网络和外部进程关键路径使用 mock。没有运行应用入口、模拟服务器、真实 mDNS 广播或真实 rclone，没执行安装器、写注册表或连接手机。

## 实际执行与结果

| 项目 | 执行方式 | 结果 |
| --- | --- | --- |
| 上游测试 | 在 `.audit/upstream/desktop` 执行隔离 Python 的 `-m pytest tests -q -p no:cacheprovider --junitxml=../../upstream-tests.xml` | 107 passed, 1 skipped in 2.02s；退出码 0 |
| Skip 核对 | 解析 junit XML | `tests.test_utils.TestGetAvailableDriveLetters.test_returns_empty_on_non_windows`，原因 `Non-Windows only` |
| 定向复现 | 隔离 Python 执行 `docs/audit/reproduce_upstream.py .audit/upstream` | 7 项现象复现，退出码 0 |
| Python 语法 | Python `ast.parse` 遍历上游所有 .py | 21 个文件通过，未执行这些模块的入口 |
| 原源码未变 | `git -C .audit/upstream diff --exit-code` 及 `status --porcelain` | 退出码 0；工作区输出为空 |
| 来源 | `git rev-parse HEAD`、`git show -s`、`git ls-files` | 固定提交、日期和 83 个文件已回读 |
| GitHub 记录 | 官方 repo/issues/releases API 与本地 tags | 全部 Issues/PR 0、Release 0、tag 0；不是单看 README |
| 安装器 | PowerShell Get-AuthenticodeSignature + Get-FileHash | NotSigned；只读取文件，未执行；SHA-256 见 baseline.json |
| .NET | 官方支持策略、`dotnet --list-sdks`/`--list-runtimes` | 当前 LTS 10；本地无列出的 SDK，存在 10.0.11 等运行时 |
| 文档校验 | 本地脚本按固定提交核对 52 个源码引用、行号和文档相对链接；比较 AGENTS 与附件正文 | 两处越界尾行引用已修正；最终校验通过，AGENTS 与交接正文一致 |

保存的原始结果：[pytest 输出](upstream-tests.txt)、[复现 JSON](reproduction-results.json)、[基线](baseline.json)。详细 junit XML 仅存于本地 `.audit/`，不将其中本机绝对路径纳入交付文件。

## 重现方法

前提：在项目根目录，已有上述提交的干净 `.audit/upstream` 和安装了审计依赖的 `.audit/venv`。脚本会拒绝提交不符或已跟踪文件修改的参考副本。

```powershell
& '.\.audit\venv\Scripts\python.exe' '.\docs\audit\reproduce_upstream.py' '.\.audit\upstream'
```

`reproduced: true` 和退出码 0 表示成功观察到审计指出的行为，**不是安全测试通过**。并发测试的 rclone 是 mock；死锁测试使用真实 Python Lock 和原始函数，用活线程栈确认，线程在该独立 Python 进程退出时结束。没有翻译 Android 算法后把模型结果冒充 APK 实测。

## 未执行

Android Gradle 构建、APK 安装/运行、C# 构建、Python GUI、WinFsp 安装/驱动验证、真实 rclone、Explorer 文件操作、任何手机数据访问、TLS 攻击实测、大文件/断网/后台/性能测试均未执行。

因此本记录只能支持“审计完成”，不能支持“Phase 0 链路通过”“MVP 完成”或“生产可用”。
