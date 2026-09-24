# P0-006 Android 兼容性 Lint 阻断

- 状态：已完成限定兼容性修复，2026-09-19；不代表 Phase 0 整体验收
- 目标：修复当前实验 Android 的两个既有 Lint error，并验证相关启动/停止行为。
- 范围：在 P0-005 上最小修复 API 27 主题属性与广播接收器注册兼容性；按官方 SDK/AndroidX 文档核对最低版本调用，保留版本分支，补必要运行证据。
- 不做什么：不通过隐藏 Lint/添加 blanket suppression 宣称通过，不为消除警告盲目升级依赖；不启动 WPF 重写、不连接真实手机或发布 APK。
- 涉及文件：独立实验修复副本、补丁、主题资源、MainActivity、对应检查和文档。

## 验证要求

先回读当前源代码与两项完整 Lint 诊断。构建、单元及 lintDebug 必须分别记录终态；新增问题不能被旧警告掩盖。验证广播接收器生命周期、正常启动/停止和 P0-005 缺失目录拒绝行为。低版本运行条件不足时明确未验证，不把静态 Lint 当所有 Android 版本验收。

继续保留 Phase 0 未决项：D-02 支持版本与前台服务方案、同 Wi-Fi 真机/mDNS、完整 Explorer 操作、大文件与重连矩阵。用户提供备用手机为 Redmi K40 / Android 16，尚未连接，实际版本/ROM 与兼容性未核对。

## 实现

已核对 P0-005 的两项完整诊断。独立 `.audit/p0-006-worktree` 应用累计补丁后，将 API 27 导航栏属性移至 values-v27，通用颜色由公共父主题继承；通过现有 AndroidX Core 1.12.0 的 ContextCompat 注册非导出接收器。依赖、minSdk/targetSdk 不变，不隐藏诊断。

新增真实 Android 主题解析、接收器暂停/恢复/重建及外部 shell 冒充状态的验证，后者带应用内发送的阳性对照。准备 Android 26 与 34 的隔离运行证据。

## 验收结果

构建退出 0，20 项单元通过；Lint 为 0 errors/128 warnings，两项原有 error 消失，没有新增诊断。Android 8.0/API 26 与 Android 14/API 34 各 14 项原生通过、1 项硬链接样本跳过，各 11 项 UI/HTTPS/小文件检查与 2 项失败启动检查通过。主题、广播隔离及生命周期已有运行证据；前序文件操作代码未变，两版本均执行原生存储回归。

审核已核对最终源码、应用/测试 APK 散列、补丁正反 apply-check、依赖和共享范围未变。两台 AVD 与端口均退出，样本保留。工具在 API 26 的目录恢复与日志清理问题、处理及重跑结果如实记录于 [验收报告](../../audit/P0-006-VALIDATION.md)。

真机、Android 16、长时间后台、同 Wi-Fi/mDNS、完整 Explorer 和大文件矩阵未验证。下一任务 [P0-007](P0-007-android-support-policy.md)。
