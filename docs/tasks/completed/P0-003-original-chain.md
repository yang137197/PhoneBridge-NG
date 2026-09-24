# P0-003 原始技术链路验证

- 状态：执行完成，验收失败（2026-09-19）；Phase 0 未通过
- 目标：在可丢弃 Android 环境证明 HTTPS/WebDAV → rclone → WinFsp → Explorer 能挂载、读写、双向复制与正常退出。
- 范围：开发依赖预检、官方工具准备、固定上游构建、隔离数据链路验证、证据记录。
- 不做什么：不开始新版 WPF 客户端，不在主手机个人数据上运行上游，不将模拟器结果当三星实机验收。
- 涉及文件：本记录、README、TESTING、docs/audit 下脱敏证据；工具和原始日志在被忽略的 .audit 内。

## 实现

固定上游 a378fec40561a4d18be2334f4de92ee02a0e7d0c，全程未修改产品源码。准备官方 JDK/Gradle/Android SDK/rclone，校验下载；用户明确授权并完成 WinFsp 安装。在专用空白 Android 14 模拟器构建/安装原版 APK，执行实际 HTTPS → rclone → WinFsp → P: 文件操作及独立 Android 哈希复核。将实验脚本归档于 tests/integration/upstream_chain，详细证据见[实测报告](../../audit/P0-003-VALIDATION.md)。

## 测试

预检：Windows 11 专业版 10.0.26200，64 位，15.8 GB 内存，C 盘可用 214.3 GB；已存在 hypervisor。当前进程非管理员。PATH 无 Java、adb、rclone；常规位置未检出 Android SDK、WinFsp；.NET SDK 列表为空。

官方 emulator-check 实测 WHPX 可用，模拟器正常启动。APK assembleDebug 成功；Lint 2 errors/128 warnings，组合构建退出码 1；Android 单元测试 NO-SOURCE。

实测 P: 可挂载，HTTPS 未认证返回 401。非重复区段 100 MB 下载从 32 MiB 起错误重复源文件开头，哈希不一致；独立 Range 请求返回完整文件 200；PROPFIND 正文导致下一 OPTIONS 返回 400。首轮上传过早校验的问题已纠正，最终该样本在手机完整落盘。未验证的路径没有追记为通过。

审核了对应 GET/PROPFIND 源码与验证脚本；保留第一次误报和纠正记录。结束时确认无待写入后退出受管挂载，停止 Android 服务/模拟器并移除转发，缓存保留，上游 tracked 内容不变。

## 验收结果

未通过。CHAIN-04 明确失败；mDNS 同 LAN、Explorer 视觉操作、备用真机和完整双向非重复样本均未通过验收。下一任务 P0-004 最小修复已复现的 WebDAV 问题后重新验证；没有开始 Phase 1，也没有声明产品可用。
