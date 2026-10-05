# P2-030 v0.2.6 正式候选验收记录

日期：2026-09-30。结果：通过。

## 1. 动态正式基线

- GitHub Latest：`v0.2.5`。
- 状态：非草稿、非预发布，六项资产均为 uploaded。
- 发布时间：`2026-09-29T10:24:30Z`。
- 标签剥离后提交：`11a58c51203e62ab77443bea15403ad7609e945e`。
- GitHub 资产 digest 与本地正式镜像的文件名、大小和 SHA-256 逐项一致。

## 2. v0.2.6 候选制品

| 文件 | 字节 | SHA-256 |
| --- | ---: | --- |
| `delivery-manifest.json` | 2038 | `161005D561BE5B0D692F6EF49E150E621E4E667BC0A6EEDA86E4BF81B0DBC363` |
| `PhoneBridge-NG-0.2.6-source.zip` | 1074845 | `B057E4CA9FFBB94E4897FA93315BD387EE9F2ECA1089B1C9ECBC072C1FF23C02` |
| `PhoneBridge-NG-0.2.6.apk` | 3533110 | `FCBA3C719976E8C9D5399BFB1C464551BE9800D8B703D367AB94ED4D598EB2F9` |
| `PhoneBridge-NG-Setup-0.2.6.exe` | 81179557 | `55B979FEB1888C0415137ECFCA79B551E2C055201E22736C9389F80D9BF4C155` |
| `README.txt` | 4028 | `8E763558C8350F0B0F58F5922E94B1AF971C9BF9039F9474828E4CF925145DF8` |
| `SHA256SUMS.txt` | 363 | `162AAD9BF3D2C82C2CD4B297D2B317EAFD62936C79CA083FD95A90582CAF9AD2` |

- 默认本地入口：`.audit/delivery/output/`。
- 候选快照：`.audit/delivery/P2-030-v0.2.6-candidate-r1/output/`。
- v0.2.5 正式镜像归档：`.audit/delivery/v0.2.5-published-output/`。
- 源码 ZIP：322 个源文件、323 个条目；私钥标记扫描通过，本地审计/构建产物和私钥扩展名被排除。

## 3. 自动门禁

- Windows Release 构建：0 warning、0 error。
- Windows 测试：338/338 通过。
- Android：123 个 Gradle 任务成功；29/29 JVM 测试、Debug/Release Lint 通过。
- APK：`org.phonebridge.ng`，`versionName=0.2.6`，`versionCode=8`，target SDK 36，v2/v3 签名通过。
- Android 证书 SHA-256：`66FF69D71637D215C2F104DB95B93CC1F991FA1F23580F95AF3316B0763B14D4`。
- Windows 安装器：`FileVersion=0.2.6.0`，`ProductVersion=0.2.6`，Authenticode `NotSigned`（第三方侧载的已知边界）。

## 4. 双端全新应用数据安装

- Windows 和 Android 均卸载旧应用并清除对应应用数据；未备份或恢复配对数据。
- 两端第一次启动均显示完整四步引导；返回首页后不再自动弹出。
- Windows“关于 → 使用帮助”和 Android“设置 → 使用帮助”均可再次打开完整四步内容。
- 新建真实配对、开始共享、Windows 连接、E: 挂载、文件读取、单击断开和盘符移除通过。
- Windows 与 Android 真实检查更新均显示“当前已是最新版本”。
- 本轮是当前电脑的全新应用数据安装，不是全新 Windows 机器验收。

## 5. 正式 v0.2.5 → v0.2.6 原位升级

1. 从保留的 v0.2.5 六项正式镜像重新建立基线，Windows 和 Android 均确认版本 0.2.5。
2. 在 v0.2.5 上真实配对；Windows 配对文件为 `32b4afc617b4469b6a4ae7cf0e8d8492f0a51c4b920e68726c6b32221fd2e33e.pairing`，1190 字节，SHA-256 `F5EBFABBF57C1228722F37CE98686AA48A017C3E8246C3CDD1A2C1CB508D05FA`。
3. 不卸载、不清除数据，直接安装 v0.2.6 安装器和 `adb install -r --no-incremental` APK。
4. 升级后 Windows 为 0.2.6，配对文件数量、大小和 SHA-256 不变；Android 为 `versionCode=8` / `versionName=0.2.6`，`firstInstallTime=2026-09-29 21:03:32` 不变。
5. Android 配对电脑 `LY` 保留，Windows 配对卡片保留；两端都没有误弹首次引导。
6. 开始共享后 Windows 成功连接并挂载 E:；卷标 `M2012K11AC`，总容量 114133266432 字节，验收时可用空间约 92.4 GB。
7. 成功读取 `E:\phonebridge-p1-007-origin.txt`，内容为 `phonebridge-p1-007-synthetic`。
8. Windows 单击一次“断开”后，E: 与 rclone 均立即消失，Windows 主程序保持运行，无需刷新。

## 6. 未验证和已知限制

- v0.2.6 尚未发布，所以不存在可供已安装 v0.2.6 从 GitHub Latest 下载的更新版；本候选的 Windows/Android 真实新 Release 下载进度视觉验收未运行。
- 全新配对后有一次首次连接失败，立即后续连接与再次断开/连接均通过，原位升级链路首次也成功；未能稳定复现，现有证据不足以确认为缺陷。
- Windows 安装器按已接受的第三方侧载边界仍为 Authenticode `NotSigned`。

## 7. 发布边界

候选本地验收已通过，但未推送、未打标签、未创建 GitHub Release。这三项必须获得用户的独立明确授权。
