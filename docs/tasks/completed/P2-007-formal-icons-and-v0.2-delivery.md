# P2-007 — 正式图标与 v0.2.0 交付收口

状态：已完成。日期：2026-09-26。

## 目标

把已确认的 A“桥接文件”视觉身份和已验收的 v0.2 功能收口为可核验的 Windows 安装器、长期签名 Android APK、对应源码与交付材料，并把 `0.2.0` 建立为后续正式升级的首个基线。

## 范围

- 收口 Windows EXE、窗口、任务栏、安装器和托盘状态图标，以及 Android adaptive/monochrome 启动器图标。
- 把本地交付脚本、安装器元数据、README、manifest、SHA256SUMS 和源码 ZIP 刷新到 `0.2.0`。
- 使用既有长期 Android 签名身份生成正式 APK，不创建或替换签名身份。
- 验证 `0.2.0` 的全新安装、正式身份和基础运行；不把 `0.1.0` 定义为升级来源。
- 记录可复核的版本、签名、哈希、文件清单和安装证据，为未来 `0.2.x` 到 `0.3+` 的升级验证保留基线。

## 不做什么

- 不发布 GitHub Release，不上传或分发制品。
- 不创建、导出、展示或提交签名秘密。
- 不重复大文件、睡眠唤醒或 P2-005 双设备传输测试。
- 不增加 v0.2 范围外功能，不修改配对和传输协议。

## 涉及文件

- `windows/`、`android/app/src/main/res/`：正式图标与版本身份。
- `scripts/Build-LocalDelivery.ps1`、`scripts/New-SourceDelivery.ps1`：正式交付生成与一致性校验。
- `windows/installer/PhoneBridge-NG.iss`：安装器图标和 v0.2 元数据。
- `docs/`：交付说明、验证记录、状态与交接。
- `.audit/delivery/`：Git 忽略的本地正式制品和证据。

## 实现

- Windows EXE、窗口/任务栏、安装器采用已确认的 A“桥接文件”图标；托盘改为离线、发现、连接中、已挂载和错误五种产品单色图标。
- Android 正式 adaptive/monochrome 启动器图标、Windows/Android 版本与本地交付脚本统一为 `0.2.0`。
- 使用既有长期签名身份生成正式 APK；安装器、APK、README、源码 ZIP、许可、SHA256SUMS 和 manifest 组成同一正式交付。
- `0.2.0` 作为首个正式升级基线，不再要求或声称 `0.1.0 → 0.2.0` 升级兼容。

## 测试

- Windows Release 构建 0 warnings / 0 errors，完整测试 298/298；托盘针对性测试 58/58。
- 正式安装器与 APK 版本、散列、Android 证书摘要和 manifest 独立回读一致。
- Windows `0.2.0` 全新安装成功；Samsung 上先清除旧包再安装正式 `0.2.0`，安装后 APK 与交付 APK 字节级散列一致。
- Samsung 与 Windows 正式安装版完成真实配对、Music 共享、`P:` 根目录读取和安全断开；断开后盘符消失、rclone 退出、Windows 客户端保持运行。

## 验收结果

通过。`0.2.0` 已满足正式本地侧载交付条件，并成为未来 `0.2.x` 到 `0.3+` 的升级验收基线。P2-007 未获授权发布；用户随后授权的 P2-008 已发布 GitHub `v0.2.0` Release。Windows 安装器仍未做 Authenticode 产品签名。
