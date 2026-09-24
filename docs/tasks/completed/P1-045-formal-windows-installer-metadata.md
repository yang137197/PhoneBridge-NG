# P1-045 — Windows 正式安装器元数据

状态：已完成。日期：2026-09-23。

## 目标

移除 Windows 安装器内部残留的 `local preview/local installer` 开发标签，生成正式第三方侧载安装器，并在当前已安装 WinFsp 的 Windows 11 上验证同版本覆盖安装。

## 范围

- 只修改 Inno Setup 的显示版本名和文件描述元数据。
- 复用 P1-041 已验证的 Windows publish 目录、固定 WinFsp MSI 和 Inno Setup 编译器，重编 Windows 安装器。
- 核对候选安装器文件版本元数据、内容、安装退出码、安装后文件和无后台进程。
- 验证通过后更新默认交付 installer、manifest、SHA256SUMS 和对应源码包。

## 不做什么

不修改运行代码；不重签或重建 Android APK；不卸载 WinFsp；不连接手机；不增加发布渠道、自动更新或商业代码签名。

## 涉及文件

- `windows/installer/PhoneBridge-NG.iss`：正式显示元数据。
- `.audit/runs/P1-045`：候选安装器和机器证据。
- `.audit/delivery/output`：验证后更新的默认正式交付。
- 本任务记录和验收报告。

## 实现

- 把 Inno Setup 的 `AppVerName` 改为 `PhoneBridge NG 0.1.0`，文件描述改为 `PhoneBridge NG installer`；其他安装逻辑未改。
- 复用已验证 Windows publish、固定 WinFsp MSI 和 Inno Setup 编译候选安装器。
- 当前机覆盖安装通过后，以同卷暂存方式替换默认 installer，并更新 manifest 与 SHA256SUMS。
- 重新运行对应源码生成器，使源码 ZIP 包含新安装器配置和最新权威文档。

## 测试

- 候选大小 81,083,725 字节，SHA-256 为 `02B63BE25590FD1F86BC661CF15B7A3941E2FE13440920AAA0F850B4E7961262`；文件描述、产品名和版本符合正式元数据。
- 当前 Windows 11 静默同版本覆盖安装返回 0，三份配对记录集合散列不变。
- 安装后客户端、rclone 为 0，P: 不存在。
- 默认 manifest 与 SHA256SUMS 已绑定新 installer；正式 APK SHA-256 未变化。
- 对应源码 ZIP 已重新生成和逐项回读。

## 验收结果

Windows 正式安装器不再显示本地预览标签，并已通过当前机覆盖安装及配对保留验收。尚未验证的产品安装分支只剩无 WinFsp 干净 Windows 的内置 MSI/UAC 路径。
