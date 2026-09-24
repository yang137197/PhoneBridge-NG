# P0-010 真机局域网文件链路

日期：2026-09-19。**P0-010 限定链路验收通过：双向 100 MB、真实 LAN、WinFsp 盘符、用户确认的 Explorer 可见性及最终退出均有证据。初次复制入口和界面复验入口各退出 1，工具收尾修复由独立回归补证；不声称完整入口全绿。Phase 0 最小技术链路门槛已满足，正式产品与 MVP 未完成。**

## 环境与范围

使用已授权的 Redmi K40（设备报告 alioth / M2012K11AC、Android 16 / API 36）；Windows 11、WinFsp 2.1.25156、rclone 1.75.1。手机 Wi-Fi 与电脑有线网卡处于同一 LAN，不能写成两端同时 Wi-Fi。APK/累计补丁与 P0-009 相同，没有修改 Android 产品代码、依赖、权限或系统设置；散列见 [manifest](p0-010/manifest.json)。ROM 来源未独立验证。

新增 [真机链路脚本](../../tests/integration/upstream_chain/real_device_chain.py)和[只读界面复验脚本](../../tests/integration/upstream_chain/real_device_view.py)。仅使用新目录 `/sdcard/Music/P0-010-84926bfb5cd4`，P: 也只映射这个目录。原有 `.thumbnails/.database_uuid` 和 `.nomedia` 保留，大小、修改时间和 inode 前后相同。样本与本机缓存保留，不自动删除。

TLS 使用上一任务确认的设备 CA，执行前从授权 USB 回读公有 CA 并比对既有 SHA-256；没有重新配对或接受新的身份。rclone 的真实 LAN 连接启用 ca-cert 和地址校验。认证只在内存与子进程环境中传递，RC 仅监听回环且有临时认证；不把环境变量当正式凭据保险库。手机 100 MB 样本在手机本地由随机源生成，PC 样本按区段生成；USB 不承载待验收的大文件传输。

## 实测结果

| 用例 | 结果 | 证据 |
| --- | --- | --- |
| CHAIN-01 发现 | 通过 | 新监听器先于服务启动，3.469 秒发现匹配的 HTTPS 服务；这是本次受控启动观察，不是长期发现率 |
| CHAIN-02 HTTPS/认证 | 通过 | 开启身份/地址校验，未认证 401、认证后 200，身份指纹保持 |
| CHAIN-03 挂载/Explorer | 通过 | P: 实际枚举和读写成功；独立只读重挂后，用户确认四个指定项目均可见，详见[界面复验](p0-010/explorer-recheck.json) |
| CHAIN-04 手机 → PC | 通过 | 100,000,000 字节及 49 字节 UTF-8 文本经 P:/WinFsp/rclone/LAN 复制，手机/PC 独立 SHA-256 相同 |
| CHAIN-05 PC → 手机 | 通过 | 新文件写入 P:；VFS 无待上传后，手机端两次独立大小/散列一致，另一次全新 HTTPS LAN 读取再次一致，未用缓存回读代替手机落盘 |
| 创建/重命名 | 通过 | P: 创建 new-folder，再改为 renamed-folder；Android 独立确认新目录存在、旧目录不存在 |
| CHAIN-06 退出 | 通过，工具失败另列 | 两次挂载的 rclone 均退出 0，P: 消失、共享停止；修复后的宿主收尾回归 OK (1 test)，最终独立回读无应用/挂载进程、转发或控制文件，原有元数据保持，见[最终状态](p0-010/final-state.json) |

| 样本 | 精确字节数 | 手机与 PC 一致 SHA-256 |
| --- | --- | --- |
| phone-100MB.bin | 100000000 | `a00685541c25a9585dcfbef6b66884cd3fbb88cb3932da03b248647b2b7af71f` |
| pc-upload-100MB.bin | 100000000 | `3d498edc7bba04f6c094dd49811ceee4a7912b9292bd4aaa6b251965da065fad` |
| phone-origin.txt | 49 | `7374185329b3a98d96e953d8ed38bfe1c528ca5dd6b8298bdead468f65c5251e` |

逐步结果见 [chain-results](p0-010/chain-results.json)。原始证据在 `.audit/runs/P0-010/P0-010-84926bfb5cd4`。记录的单次复制时长不构成吞吐基准。

## 入口失败与界面复验

首轮自动界面工具检测到用户正在操作资源管理器，并报告窗口最小化/输入冲突，因此停止自动输入。程序等待界面确认超时，完整入口退出 **1**；这不能改写为退出 0。此前复制和手机持久性验证均已完成，随后正常卸载，用户确认盘符已消失。

独立界面复验只读重新挂载同一个已经核验的样本目录，先核对两份大文件散列、既有身份与 APK；不重复复制或写入手机。再次退出与用户观察另存证据，不覆盖首轮失败记录。

用户确认四个项目可见后，只读 rclone 正常退出 0，但测试宿主未及时结束，该入口退出 1。复现“手机回到桌面后停止测试”，JDWP 栈停在 `InstrumentationActivityInvoker.startEmptyActivitySync → finishActivity → ActivityScenario.close`；系统日志明确拒绝测试框架 EmptyActivity 的后台启动，结果 102。此处有直接证据，不能笼统归因于动画或手机息屏。

P0-010 的 Python 宿主包装在退出前通过标准 `am start` 恢复公开测试界面到前台，再发出原来的停止信号。未改 APK、系统权限或厂商后台策略。修复后的[回归](p0-010/shutdown-regression.json)确认桌面处于 RESUMED，再结束宿主，原生结果 OK (1 test)、入口退出 0，控制文件和服务均已清理。完整 100 MB 入口没有因纯收尾改动重复执行，原始入口的退出 1 保留。

诊断打印曾因 Windows GBK 编码失败，栈文件已写入且 finally 已停止应用；第一次回归状态解析只匹配 topResumedActivity，而本机输出 ResumedActivity，因此断言失败，但宿主实际已正常结束。改为读取两种字段并等待状态到达后，上述最终回归通过。这些是验证工具失败，不计作应用功能测试通过。

## 审核与限制

代码核对：绑定单一授权设备、固定 APK/rclone/身份；只新建 UUID 子目录和新文件；不覆盖既有文件；凭据不在参数/配置/日志输出；上传完成须由手机散列确认；失败保留缓存；正常卸载前确认没有待上传。测试入口不是正式 Windows 客户端，也不是安全模式实现。

rclone 日志仍出现根路径 `symlinks not supported without the --links flag`，此前模拟器也有相同日志；本轮挂载、读写和散列验证没有因此失败。不为消除日志而开启链接支持，不宣称日志无错误。

未验证：人工 Explorer 复制/拖放、1/5/10/20 GB、断网恢复、PC 休眠、真机重启/换 IP、长期后台、三星兼容和正式一次性配对/凭据保护。新版 WPF 和 MVP 尚未完成。

依据：[rclone Windows mount](https://rclone.org/commands/rclone_mount/#mounting-modes-on-windows)、[VFS 状态](https://rclone.org/rc/#vfs-stats)、[正常退出](https://rclone.org/rc/#core-quit)。实验具体结果以本次输出与独立回读为准。

宿主退出依据：[AndroidX InstrumentationActivityInvoker](https://github.com/android/android-test/blob/main/core/java/androidx/test/core/app/InstrumentationActivityInvoker.java)，finishActivity 会先启动 EmptyActivity；本机具体阻塞由实际调用栈和拒绝日志确认。
