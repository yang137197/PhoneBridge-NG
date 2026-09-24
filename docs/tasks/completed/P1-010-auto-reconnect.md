# P1-010 锁屏共享、断线卸载与自动重连

状态：已完成。P1-009 验收后的限定任务。

## 目标

保证用户开始共享后普通锁屏仍可使用；让已保存且仍受信任的设备在网络或共享暂时消失时有界卸载，并在同一设备重新出现后自动恢复严格会话与原盘符挂载，不产生重复进程、重复盘符或静默身份变更。

## 范围

更新锁屏电源所有权、连接监督状态机与恢复契约；组合 Android 前台服务、电池优化授权、既有 mDNS 重枚举、严格 session、MountManager、写队列保护和 WPF 状态。覆盖普通锁屏、主动停止、网络消失、恢复、退避、取消及应用退出。

## 不做什么

不做厂商私有后台白名单、托盘、自启、安装器、发布、手机开机自动共享、PC 睡眠唤醒全矩阵或大文件断线恢复。身份或 CA 变化时不自动重新配对。

## 涉及文件

Android MainActivity/SharingService/运行锁与 Manifest；Windows Connection/Discovery/Mounting/Desktop、资源与测试；架构、决策、测试和验收文档。

## 实现

Android 共享服务在完整生命周期持有非超时的部分 WakeLock 与 MulticastLock，并在所有停止/失败路径释放。Redmi K40 首轮锁屏证明确认未豁免时网络不可达，因此开始共享前检查电池优化状态；未豁免时只通过 Android 官方系统确认页请求用户决定，返回后重新检查，拒绝则不启动。未使用厂商私有接口。

Windows 每 5 秒通过保存 CA 和凭据检查严格 session，连续三次暂时性失败后按安全卸载规则退出 rclone；身份、授权或记录变化立即失败关闭。恢复只接受同一 `device_id`，重新验证 CA/session/模式并恢复原盘符，退避为 2/5/10/20/30 秒；主动卸载、撤销、忘记或退出会清除恢复意图。

## 测试

Windows 覆盖 session 健康检查、三次失败去抖、成功复位、同一身份匹配、退避与主动抑制。Android 覆盖运行锁明确释放和电池优化请求只绑定当前包。Redmi K40/API 36 分别复现未豁免锁屏失败，并验证授权后锁屏读写、手机端独立哈希、Wi-Fi 中断安全卸载及锁屏状态自动恢复。

## 验收结果

限定验收通过。Windows Release 构建 0 warnings/0 errors，221 项测试通过；Android Debug/Release/test APK、单元测试和双配置 Lint 最终通过，两个 P1-010 真机仪器用例通过。

授权后手机处于 `SCREEN_STATE_OFF`、Keyguard 显示且系统为 Dozing 时，部分 WakeLock 仍为 ACQ；P: 读取既有文件并写入合成文件成功，电脑和手机端 SHA-256 均为 `d56efab04e7ccb6a3ebec8726a49e636a9353eb92690d9fe3ce5ace41594906b`。关闭 Wi-Fi 后 P: 与 rclone 按三次失败规则消失；恢复 Wi-Fi 后在手机仍锁屏时自动恢复 P: 并再次读取相同文件。最后安全卸载，P:、rclone、桌面进程、Android 服务和 WakeLock 均已退出。详见[验收记录](../../audit/P1-010-VALIDATION.md)。

本任务未验证三星、数小时/数天长期锁屏、IP 改变、大文件传输中断或待上传缓存恢复。PC 睡眠或设备重启期间本就不要求继续传输，后续若测试只核对中断安全和恢复状态。项目仅第三方分发；用户接受共享期间的额外耗电，停止共享后必须无 PhoneBridge 后台服务。后续 [P1-011 Samsung 真机兼容验收](P1-011-samsung-acceptance.md)已独立完成。
