# P0-006 Android 兼容性验证

2026-09-19。结论：本任务两项既有 Lint error 已修复，构建及相关 Android 8.0/API 26、Android 14/API 34 隔离运行测试通过。此结论不等于 Phase 0、Android 全版本或 MVP 验收通过。

## 实现与依据

相对 P0-005 只增加或修改 4 个文件：MainActivity、通用主题、values-v27 主题、CompatibilityTest。P0-005 的 12 个数据流/存储源文件及测试按 LF 归一后完全相同；构建配置、依赖、Manifest、minSdk 26、targetSdk/compileSdk 34 未变，见 [范围审核](p0-006/scope-review.json)。

- `NewApi`：`windowLightNavigationBar` 需要 API 27。将该属性放入 values-v27，通用主题与高版本主题显式继承同一 Base.Theme.PhoneBridge，保留既有颜色及状态栏样式。[Android 主题文档](https://developer.android.com/develop/ui/views/theming/themes)
- `UnspecifiedRegisterReceiverFlag`：旧版本分支缺少接收器导出限制。使用既有 AndroidX Core 1.12.0 的 `ContextCompat.registerReceiver(..., RECEIVER_NOT_EXPORTED)`；保持 onResume 注册、onPause 注销，应用内状态广播正常接收。[Android 广播文档](https://developer.android.com/develop/background-work/background-tasks/broadcasts)

未新增 Lint suppression 或忽略规则。按 ID、severity、message 比较诊断多重集合，两项 error 消失，128 项 warning 原样保留，新增诊断为 0；不能将“零错误”表述为“零警告”。完整差异见 [Lint 比较](p0-006/lint-comparison.json)。

## 可追溯产物

固定上游 `a378fec40561a4d18be2334f4de92ee02a0e7d0c`，原始副本干净。独立修复副本 `.audit/p0-006-worktree`。

| 产物 | SHA-256 |
| --- | --- |
| [累计补丁](../../android/patches/P0-006-android-compatibility.patch) | `e87c82015010ec776025babc95358fb98a9eb0b4ef2c3169843a44988e46fa5c` |
| Debug APK，9,649,827 字节 | `9373fa5e1e1d807f941a011d49cdb7552c82deb840e88fe0d05ee4905994e898` |
| 最终原生测试 APK | `ba0ec129e60010b0df023c48f7be504d18f0f188d41db3402caefc399995b394` |

补丁包含 P0-004/P0-005，直接用于干净固定基线，不叠加旧补丁。最终补丁为 LF，原始基线 apply-check、修复副本 reverse-check、git diff --check 均退出 0。P0-005 的历史补丁与证据不改写。源文件原始字节及 LF 归一散列见 [source-hashes](p0-006/source-hashes.json)。

沿用已校验 JDK 21、Gradle 9.3.1、AGP 9.1.1、Kotlin 2.2.10 和隔离 SDK。组合目标 `assembleDebug testDebugUnitTest assembleDebugAndroidTest lintDebug` 退出 0。审核时补强测试：要求外部广播命令确实执行完成，再检查 UI 未被伪造；单独重建测试 APK 退出 0，两台模拟器均执行最终测试 APK，产品 APK 未变。见 [构建证据](p0-006/build.json)。

## 实际结果

同一构建的 20 项 JVM 单元测试全部通过，见 [单元结果](p0-006/unit-results.json)。API 26 使用官方 `system-images;android-26;default;x86_64` revision 1，新增专用 AVD PhoneBridgeP0Api26；API 34 沿用 PhoneBridgeP0。均为无个人资料的 AOSP x86_64 模拟器，WHPX、隐藏窗口、ADB 回环。系统实际属性分别记录于 [API 26](p0-006/api26/environment.json) 和 [API 34](p0-006/api34/environment.json)。每次测试先核对 AVD 名称及安装 APK 散列。

| 检查 | API 26 | API 34 | 证据 |
| --- | --- | --- | --- |
| Android 原生测试 | 14 通过、1 跳过 | 14 通过、1 跳过 | [26](p0-006/api26/instrumentation-results.json)、[34](p0-006/api34/instrumentation-results.json) |
| UI/HTTPS 与小文件检查 | 11 项通过 | 11 项通过 | [26](p0-006/api26/compatibility-runtime.json)、[34](p0-006/api34/compatibility-runtime.json) |
| 共享目录失效/未知选择 | 2 项通过 | 2 项通过 | [26](p0-006/api26/storage-scope.json)、[34](p0-006/api34/storage-scope.json) |
| 停止、端口关闭、模拟器退出 | 通过 | 通过 | [26](p0-006/api26/cleanup.json)、[34](p0-006/api34/cleanup.json) |

原生测试覆盖主题继承；应用内状态更新；暂停期间不接收、恢复及重建后重新接收；外部 shell 广播无法伪造状态，并有应用内同载荷的阳性对照。还实际执行 P0-005 的原生文件操作测试，补足 API 26 的路径/文件描述符运行证据。唯一跳过项是在系统拒绝建立硬链接样本时的硬链接测试，跳过不计通过。

每个系统的 11 项运行检查包含三轮停止/启动/回到后台再恢复（9 项），1,000,000 字节 HTTPS PUT/GET 及手机端独立 SHA-256 校验（1 项），AndroidRuntime 无 FATAL EXCEPTION（1 项）。合成文件 SHA-256 为 `1bf9fd40df4e777d63f6110f9137357b7f86c46f654d9cf6127b3275c071ec5b`。这只是相关小文件回归，P0-005 的 100 MB 挂载记录属于前一版本，本任务未重跑该矩阵。

不可用共享选择检查等待超过前台服务启动时限，确认 UI 为停止状态且 HTTPS 不监听；未回退至整个外部存储。最后核对两台 AVD 已退出、测试转发移除、无残留本地模拟器进程、P: 不存在，缓存与合成样本保留，见 [主机清理](p0-006/host-cleanup.json)。未连接或修改用户手机。

## 测试工具问题与处理

API 26 首次目录失效检查在恢复样本目录时失败：设备 `mv --help` 明确不支持 `-T`。先确认 Download 不存在、备用目录仍在，使用不覆盖的重命名恢复并核对目录 inode。脚本随后按系统版本使用兼容参数、拒绝已有目标并核对 inode；重新执行两项检查通过，没有删除或合并目录。

API 26 安装测试 APK 后，`logcat -c` 重复报 `failed to clear the 'main' log`，发生在原生测试启动前。已捕获命令输出；操作系统内部根因未确定。AOSP 对应源码确认这是日志清理操作的失败分支：[Android 8.0 logcat](https://android.googlesource.com/platform/system/core/+/refs/tags/android-8.0.0_r45/logcat/logcat.cpp)。测试本身无需清空日志，最终脚本改为保留并读取既有 AndroidRuntime 日志，有崩溃则停止调查；不将清理失败计为测试通过。修改后的入口在 API 26 运行成功；API 34 原生测试执行时仍使用清理日志的旧入口，其最终测试 APK 与断言完全相同。恢复记录见 [harness-recovery](p0-006/harness-recovery.json)。

## 审核与限制

已回读最终差异，确认注册与注销仍成对、只接收应用内状态，主题父级无循环，两种资源分支均有运行证据，且没有更改文件协议/共享范围。最终测试 APK 与两系统记录一致；通过断言数量及测试终态判定，未把 adb 退出 0 单独当成功。

尚未验证 API 27–33、35/36、三星/红米厂商系统、真实 Wi-Fi/mDNS、锁屏长时间运行、重启恢复、完整 Explorer 操作和大文件/断网矩阵。用户提供备用机为 Redmi K40 / Android 16；实际版本、ROM 与数据隔离状态尚未通过连接核对。

128 项既有 Lint 警告、正式配对/凭据保护/TLS 身份绑定/安全模式删除等问题仍在；无可日常使用的 NG 客户端或安装包。下一任务仅为 P0-007：依据现代 Android 官方限制及 API 36 实测，确定 D-02 支持版本与前台共享服务方案；不自动将本结果视为 Phase 0 收口。
