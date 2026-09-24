# Android Phase 0 实验补丁

这些补丁是原始链路缺陷的最小修复，不是正式 NG Android 工程。适用上游仅为 `ysachin26/PhoneBridge@a378fec40561a4d18be2334f4de92ee02a0e7d0c`。来源和许可见 [NOTICE](../../NOTICE.md)。

## P0-004

文件：`P0-004-webdav-stream-correctness.patch`。

修改 5 个源文件：新增 HttpByteRange、RequestBody 及各自单元测试，修改 WebDavServer；没有更改构建依赖、UI 或 minSdk/targetSdk。

- GET 单字节区间、开放/后缀区间、206、416；读取同一文件句柄上的偏移，防止 rclone 32 MiB 后读到开头；Range 文件响应不透明 gzip。
- 非 PUT 请求正文最多 1 MiB，精确读取长度并限制时间；认证失败、未知 Transfer-Encoding、无效长度及短正文错误关闭连接，避免残留正文被解释为新请求。
- 多区间、未知单位按完整响应处理；没有强验证器时 If-Range 返回完整响应。未实现原子 PUT、完整路径安全或安全模式删除。

本机工作副本为 `.audit/p0-004-worktree`；基线 `.audit/upstream` 未改动。可用 `git apply --check` 在固定基线上预检，再在独立副本应用补丁。两个方向的 apply-check 和差异检查均已执行。

构建使用校验过的 `.audit/tools/gradle-9.3.1/bin/gradle.bat`，工程参数 `-p .audit/p0-004-worktree/android`，目标 `assembleDebug testDebugUnitTest`；JDK/SDK/GRADLE_USER_HOME 沿用 P0-003 隔离环境。`lintDebug` 单独执行仍失败，不能当作全部检查通过。

运行 Android 实验脚本前设置当前进程环境变量 `PHONEBRIDGE_AUDIT_TASK=P0-004`，其他前置条件见[实验说明](../../tests/integration/upstream_chain/README.md)。协议回归入口 `stream_regression.py`。只允许专用空白模拟器，不用于用户手机。

本补丁已通过 12 个单元测试、34 项真实 HTTPS 回归和非重复 100 MB 双向实际挂载校验。准确结果、散列与限制见 [P0-004 验收](../../docs/audit/P0-004-VALIDATION.md)。

## P0-005

文件：`P0-005-storage-safety.patch`。**这是对原始固定提交的累计补丁，已经包含 P0-004；在干净的固定基线上单独应用，不能叠加 P0-004。**

增加共享路径/目的地解析、目录句柄边界、根/链接保护、精确暂存上传及提交、COPY/MOVE/DELETE 一致边界、不可用共享选择拒绝和失败启动处理。更新元数据输出及测试；未改变依赖、minSdk/targetSdk。文件 SHA 与 APK SHA 见 [manifest](../../docs/audit/p0-005/manifest.json)。

工作副本 `.audit/p0-005-worktree`，同一 Gradle/JDK/SDK 环境，构建步骤 `assembleDebug testDebugUnitTest assembleDebugAndroidTest`，另检查 `lintDebug`。最终组合命令因原版两项 Lint error 退出 1；新增问题为 0，不宣称 Lint 成功。

设置 `PHONEBRIDGE_AUDIT_TASK=P0-005` 后按[实验说明](../../tests/integration/upstream_chain/README.md)验证。20 个单元、97 项真实 HTTPS、11 项 Android 文件操作通过，硬链接样本 1 项跳过；进程终止保护及非重复 100 MB 双向挂载通过。限定结论、已知限制及审核见 [P0-005](../../docs/audit/P0-005-VALIDATION.md)。

## P0-006

文件：`P0-006-android-compatibility.patch`。**对原始固定提交的累计补丁，包含 P0-004/P0-005，不叠加旧补丁。** 相对 P0-005 只修改应用内状态广播注册、按 API 限定导航栏主题，并添加对应原生测试；数据流/存储代码、依赖和 minSdk/targetSdk 不变。

独立副本 `.audit/p0-006-worktree`，沿用同一隔离环境，目标 `assembleDebug testDebugUnitTest assembleDebugAndroidTest lintDebug` 退出 0；20 单元通过，Lint 0 errors/128 warnings，新增诊断 0。最终测试 APK 在 API 26/34 各 14 项通过、1 项硬链接样本跳过；各 11 项运行/小文件与 2 项失败启动通过。本任务未重跑实际盘符 100 MB 矩阵。

补丁 SHA/APK SHA 与审核见 [验收报告](../../docs/audit/P0-006-VALIDATION.md)和 [manifest](../../docs/audit/p0-006/manifest.json)。复现入口与 API 选择见[实验说明](../../tests/integration/upstream_chain/README.md)；仅用于专用无个人资料模拟器。

## P0-007

文件：`P0-007-android-support-policy.patch`。对原始固定提交的累计补丁，包含 P0-004–006，不能叠加。相对 P0-006 共 6 个文件改动：compile/target 36、connectedDevice 类型、sticky 重建恢复/空服务释放、Insets、开机日志和原生测试。minSdk 26、依赖与协议/存储源文件保持不变。

独立副本 `.audit/p0-007-worktree`；构建四项目标退出 0，20 个单元通过，Lint 0 errors/126 warnings。API 26/36 各 16 个原生通过、1 个跳过，各 11 项运行与 2 项失败启动通过；API 36 的 7 项生命周期断言通过，入口末尾 UI 清理失败后的设置恢复已独立回读。准确范围、工具限制和 SHA 见 [验收报告](../../docs/audit/P0-007-VALIDATION.md)、[manifest](../../docs/audit/p0-007/manifest.json)。仍是实验 APK，无正式分发或真机链路通过声明。

## P0-009（当前，限定 TLS 验证通过）

文件：[P0-009-tls-identity.patch](P0-009-tls-identity.patch)。对固定原始提交的累计补丁，包含 P0-004–007，不叠加。相对 P0-007 修改 10 个文件：Keystore 身份/地址证书、HTTPS 失败停止、身份状态语义、中英文错误资源、单元/原生测试和受控的真实服务测试宿主。未改依赖、存储操作或 SDK 版本。

副本 `.audit/p0-009-worktree`；四项目标构建退出 0，25 单元通过，Lint 0 errors/117 warnings，补丁双向检查通过。API 26/36 和授权备用机各 8 项原生通过；模拟器和真机 rclone 正向/负向及恢复通过，手机真实 LAN 成功。旧 PKCS12 不自动迁移，新构建会明确停止共享。散列、真实结果和工具失败记录见 [验证记录](../../docs/audit/P0-009-VALIDATION.md)、[manifest](../../docs/audit/p0-009/manifest.json)。
