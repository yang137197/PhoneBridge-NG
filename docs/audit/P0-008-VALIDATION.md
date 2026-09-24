# P0-008 真机链路验证：TLS 阻断

2026-09-19。**本轮未通过。** 已在用户授权的备用 Redmi K40 安装并启动 P0-007 实验 APK，mDNS 发现成功；真实 rclone 在 TLS 握手处拒绝不含 IP SAN 的证书，因此未挂载、未复制文件。下一步为 P0-009 证书/身份绑定修复，修复后必须返回本任务的完整 CHAIN-01–06，不降低验收标准。

## 环境与授权

用户已确认备用机清空、可安装实验 APK，并完成 USB 调试授权。实际设备报告 Xiaomi M2012K11AC/alioth、Android 16/API 36；ROM 来源未核实。手机使用 Wi-Fi，电脑使用同一 IPv4 网段的有线网卡；电脑另有不同网段 Wi-Fi/VPN，本次 mDNS 明确绑定测试 LAN 网卡。这不是“电脑和手机都用 Wi-Fi”的测试。[预检](p0-008/preflight.json)

安装前确认同包名应用不存在，ADB 安装返回 Success，手机 base.apk SHA-256 与 P0-007 构建一致：`3d96a0c4f5aecf1f68cedc522392e06f7e5197db78fba97e891e0f71c0e86100`。没有改动应用代码或 APK。[安装记录](p0-008/install-setup.json)

手机 Download 有已有文件，未在该目录测试。共享选择预设为 Music，并检查只有两个 Android 缩略图元数据文件且无符号链接；没有读取它们的内容或执行文件写入/删除。应用的所有文件访问 app-op 已授予。ADB 授通知权限被系统以缺少 GRANT_RUNTIME_PERMISSIONS 拒绝；保留系统保护，通过正常手机界面让用户允许通知并启动 Music 共享，用户已确认完成。

## 实际结果

| 检查 | 结果与范围 |
| --- | --- |
| 安装与前台运行 | APK 散列一致，实际服务处于前台 |
| mDNS | 新建 Windows 监听后 0.218 秒发现该手机的 `_phonebridge._tcp.local.`，端口 8273，公告 https；手机此前已由用户启动共享，不是冷启动/重启自动恢复测试 |
| 公共证书取得 | 经明确授权的 USB ADB 转发只读取 TLS 公共证书，未发 HTTP 请求/密码，未导出私钥 |
| rclone 真正 LAN TLS | 启用 `--ca-cert`，没有 `--no-check-certificate`、认证凭据或代理环境；`lsd` 返回 1，错误为证书缺少 IP SAN |
| 盘符/复制 | 未执行，不能记通过；未向 LAN 发送认证凭据或文件 |
| 清理 | 应用强行停止、实际服务不存在，LAN 端口不可连接，ADB 测试转发移除，未出现 P:；APK 保留安装以供后续更新 |

见 [mDNS](p0-008/mdns.json)、[证书](p0-008/certificate.json)、[TLS 结果](p0-008/tls-probe.json)、[清理](p0-008/cleanup.json)、[manifest](p0-008/manifest.json)。证书 SHA-256 为 `0af441d83cf97aa3606842d1c5eefb5f970fcab08f109f4e463ea988c6ef6841`，实际解析的 SAN 列表为空。

探针程序退出 0 只说明观察与清理完成；被测 rclone 退出 1，验收仍失败。原始记录的 `observation_seconds: 30` 表示观察上限，不是实际等待 30 秒；归档已改为 `observation_limit_seconds`，发现时间取真实回调记录，未重测或改写该时间。个人网络地址只留在本地忽略目录，归档省略。

## 根因与官方依据

固定上游 TlsHelper 构建证书时仅设置 CN/签名，未加入 SubjectAlternativeName；真机证书解析结果与之相符。rclone v1.75.1（本机 Go 1.26.8）的实际错误：`cannot validate certificate ... because it doesn't contain any IP SANs`。Go 的地址校验使用 IPAddresses/DNSNames，旧 CN 不参与，因此仅信任公共证书无法补足地址标识。[Go 官方 x509 文档](https://pkg.go.dev/crypto/x509#Certificate.VerifyHostname)

rclone 官方 `--ca-cert` 用于校验证书；固定 v1.75.1 实现以指定 PEM 新建 RootCAs 池。它不能让缺失的 SAN 自动有效。本地 flags 未提供 certificate fingerprint/pinning 或 tls-server-name 参数，不虚构这些开关。[官方 TLS 说明](https://rclone.org/docs/#ssl-tls-options)、[固定版本实现](https://github.com/rclone/rclone/blob/v1.75.1/fs/fshttp/http.go#L243)

已确认 SAN 是当前阻断，但不能据此断言只增加一个 IP 就能完成正式身份方案：还必须验证 IP 改变、稳定设备身份、错证书拒绝、密钥安全保存及 TLS 失败不降级。D-04 仍未解决。

## 工具与代码审核

新增 [真机只读探针](../../tests/integration/upstream_chain/real_device_probe.py)，设备序号与两个 LAN 地址由当前进程环境传入，不写入跟踪代码；执行前校验实际设备/API/APK、Music 内容范围和选择、自启关闭、服务前台状态。USB 转发使用空闲端口及 no-rebind，实际 rclone LAN 操作清理继承的 RCLONE 配置与代理变量，不读取/传递认证密码；finally 停止精确应用并移除本次转发。

初次准备脚本把新安装尚不存在的 files 目录当作可列目录，明确取得 No such file 后改为检查应用根目录；通知授权失败则改走用户界面，没有继续尝试提权。最终探针入口实际运行。随后只有两个输出字段语义更正（观察上限、未执行共享目录写入），该字段更正后的入口未重复运行。

全局 AndroidRuntime 日志包含两天前另一个系统应用的崩溃，不能归因于 PhoneBridge；最终按该实验应用 UID 限定读取，未观察到其 FATAL EXCEPTION。没有清空手机日志，其他应用日志不归档为本项目运行证据。

## 后续边界

本次没有盘符、双向 100 MB、Explorer 操作或网络恢复通过证据。Phase 0、正式 Android/Windows 客户端与 MVP 均未完成。唯一下一任务为 [P0-009](../tasks/completed/P0-009-tls-identity.md)，先解决安全连接前提，再复验本任务；不使用跳过 TLS 校验来使复制测试变绿。
