# P2-007 验收记录 — 正式图标与 v0.2.0 交付收口

日期：2026-09-26。结果：通过。

## 验收边界

- `0.2.0` 是首个正式升级基线；不定义或验证 `0.1.0 → 0.2.0` 升级路径。
- 本轮验证最终图标、正式制品、全新安装和单台 Samsung 的最小真实链路；不重复 P2-005 双设备、既有大文件或睡眠测试。
- P2-007 本身未创建 GitHub Release；用户随后授权的 P2-008 已把本次 exact-head 制品发布为 `v0.2.0`。

## 构建与自动化证据

- Windows Release 构建：0 warnings / 0 errors。
- Windows 完整测试：298/298 通过。
- 托盘与桌面针对性测试：58/58 通过。
- 正式 APK 使用既有长期签名身份；证书 SHA-256：`66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4`。

## 正式制品

| 制品 | SHA-256 |
| --- | --- |
| `PhoneBridge-NG-Setup-0.2.0.exe` | `F3856096DB4882071C4B61B9522ADDFE1CD30F3E33C654A1C2B935BCEBB5EFC7` |
| `PhoneBridge-NG-0.2.0.apk` | `5F31E9B0E3EC20C25715014053802C97304443D8A4279BF27F9E82B198F76CF8` |
| `PhoneBridge-NG-0.2.0-source.zip` | `1388DD6EB69A7B48B04A2A541B58D02D143A094FE240BA5AB8043EEEB92C9B8A` |

- Windows 安装器与已安装 EXE 均回读为 `0.2.0`；已安装 EXE 与正式 publish EXE 的 SHA-256 均为 `A17702BBF9C71E60F9C447912F5EC2E2583F5407C73AD590161F3B6D909BB9F2`。
- APK manifest 回读：package `org.phonebridge.ng`、versionCode `2`、versionName `0.2.0`。
- Samsung 安装后的 `base.apk` 已拉回，SHA-256 与正式 APK 完全相同。
- delivery manifest 为 `third-party-sideload` / `third-party-release`，版本 `0.2.0`。
- 源码 ZIP 含 297 个源码文件和 298 个 ZIP 条目，并显式包含 `docs/design/v0.2/icons/tray/*.svg` 可编辑托盘图标源文件；私钥标记扫描通过。

## 全新安装与真实链路

- Windows `0.1.0-local` 已卸载，原 `%LOCALAPPDATA%\PhoneBridge-NG` 数据改名备份后安装 `0.2.0`；安装日志明确记录 `Installation process succeeded` 且无需重启。
- Samsung `SM-S9180` 上旧 `org.phonebridge.ng` 已卸载；手机端临时 APK 与本地 APK 散列一致后安装成功，安装时间与更新时间均为本次全新安装时间。
- 用户确认所有文件访问与电池优化系统权限；ADB 回读 `MANAGE_EXTERNAL_STORAGE: allow` 和 PhoneBridge 电池白名单。
- 两端完成一次真实配对；Windows 生成一条新配对记录，Samsung 显示一台已配对电脑。
- Samsung 共享 `Music` 后，Windows 正式安装版启动 rclone 并挂载 `P:`；`P:\` 可读且根目录列举成功。
- 用户点击断开后，`P:` 消失、rclone 退出，PhoneBridge Windows 客户端继续运行。

## 已知边界

- Windows 安装器未做 Authenticode 产品签名，仍可能显示未知发布者。
- P2-008 已发布 GitHub `v0.2.0` Release；`.audit/delivery/output/` 保留为与 Release 一致的本地镜像。
- 未来 `0.3+` 必须以本次 `0.2.0` 长期签名身份和真实数据为升级保持基线，另立任务验证。
