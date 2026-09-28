# P2-011 验证记录 — v0.2.2 正式候选与升级

日期：2026-09-28。结果：正式候选和 `v0.2.0 → v0.2.2` 升级门禁通过。

## 前一正式版本

- GitHub Release：`v0.2.0`，非草稿、非预发布。
- 标签提交：`60c52b729f2052fa933870bfc175df01d78f17be`。
- GitHub 六项资产均为 uploaded；摘要与 P2-008 记录一致。
- Windows 安装版：`0.2.0.0`；Android：`versionCode 2`、`versionName 0.2.0`。

## v0.2.2 二进制候选

| 制品 | SHA-256 |
| --- | --- |
| `PhoneBridge-NG-Setup-0.2.2.exe` | `246C5280812006129A0E77E2B1FB0440A634C6021608D90856AA6B67EB2960CE` |
| `PhoneBridge-NG-0.2.2.apk` | `908A7C93D5ABE750702E470F82A58555A03B201F9F85ABBD985382BF8DC6E1E4` |
| `README.txt` | `662491B5962BBE51CF8E9EF8488C25BFAB25DEFC4F78450DA236DEAC5AE05F88` |
| `PhoneBridge-NG-0.2.2-source.zip` | `C948CA3CC1B56E19BA2BD8A8E674B5763ED6DC181E67FBEEA8F246D1014DC0FD` |

- Windows 安装器文件/产品版本为 `0.2.2.0` / `0.2.2`，仍未做 Authenticode 产品签名。
- Android 为 package `org.phonebridge.ng`、`versionCode 4`、`versionName 0.2.2`、minSdk 26、targetSdk 36。
- APK v2/v3 签名验证通过，唯一签名证书 SHA-256 为 `66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4`，与 `v0.2.0` 完全一致。
- 对应源码包含 302 个源码文件和 303 个 ZIP 条目；逐文件清单、必需入口、私钥标记扫描及最终回读全部通过。
- 首个二进制候选的安装包内仍复制旧 `0.2.0` 交付说明，核对时即拒绝；修正文档后重新构建，未把该候选用于安装或发布。

## 原位升级保持

### Windows

- 升级前没有 PhoneBridge 挂载盘或 rclone，客户端从托盘正常退出。
- 安装器静默原位安装退出码 `0`；安装后 EXE 为 `0.2.2.0`。
- 配对文件 `3ce4642a387d08e1e6978c9f4dfa1af8c985429bd68bb74443fc5f0d8b744e5a.pairing` 升级前后长度均为 1190，SHA-256 均为 `2838A37D00804AEF08E8E75734D3AA8DEAB238D5F8E00CCC7B1C4D346C2E4358`。
- v0.2.2 启动日志加载同一 `session:1`，没有自动连接、rclone 或盘符。

### Android

- `adb install -r` 返回 `Success`；`firstInstallTime` 保持 `2026-09-26 19:58:44`，证明不是卸载重装。
- 安装后的 `base.apk` SHA-256 为 `908A7C93D5ABE750702E470F82A58555A03B201F9F85ABBD985382BF8DC6E1E4`，与候选字节级一致。
- 升级前后 UI 都显示已配对电脑 `LY`；升级后仍显示“完全读写（允许覆盖和删除）”。

## 真实连接与停止共享

- 当前系统盘为 `C:`、`D:`；Auto 分配得到 `E:`。
- 系统回读 `E:` 为网络盘，ProviderName 为 `\\pbng-3ce4642a38\M2012K11AC`；根目录可读并列出真实目录。
- 日志记录 `ConnectionCompleted/Success`、`MountStateChanged/Mounted`、`WinFspChecked/Healthy` 和连续健康检查成功。
- 手机停止共享后，从首次失败健康检查到 `MountStateChanged/Stopped` 为约 17.6 秒；确认丢失到停止约 1.5 秒。
- 停止后 `E:` 消失、rclone 退出、Windows 客户端继续运行。界面截图显示设备“未连接/离线”，底部为重连失败提示，右侧为“当前无挂载盘符”，不再显示“手机会话正常”。
- 截图证据 SHA-256：`556FB40D1126D1BA606B8FB060BF35BB13E5B76C32B8A144060A6D9EC77FFDCF`，本地路径 `.audit/runs/P2-011/windows-after-phone-stop.png`。

## 尚未验证

- 全新 Windows 机器的实际首次安装、首次配对和 UAC/WinFsp 分支。
- Explorer 使用设备备注而非设备原名的真实显示。
- 固定盘符冲突失败关闭，以及插入真实 USB/已有网络盘时的完整顺序矩阵。

上述项目必须按 [全新机器验收清单](../V0.2.2_CLEAN_MACHINE_ACCEPTANCE.md) 独立执行；当前不能写成已通过。
