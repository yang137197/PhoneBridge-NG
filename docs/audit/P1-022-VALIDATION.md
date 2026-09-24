# P1-022 文件名与路径边界完整性验收

日期：2026-09-22  
结果：通过限定范围，修复一个安全模式 VFS 缓存缺陷。

## 环境与最小样本

- Windows 11、WinFsp、项目固定 rclone 1.75.1、Samsung SM-S9180/API 36，共享根为 `Music`。
- 手机实际 `NAME_MAX` 为 255 字节。
- 只使用三个代表性样本：同时包含中文、空格、`+`、`%` 和 emoji 的 38 字节名称；244 字节长名称；一个既有同名目标。不做字符排列组合。
- 所有内容均为 25–33 字节合成文本，手机和 Windows 使用独立 SHA-256。

## 读取、上传与重命名

P: 能列出并读取组合名称与 244 字节长名称，长度和 SHA-256 均与 Android 基线一致。Windows 新建 `电脑上传 中文+%😀.txt` 后，Android 独立哈希匹配；再通过 P: 重命名为 `已重命名 空格+%😀.txt`，Android 端新名称内容保持一致且旧名称消失。期间始终只有一个桌面客户端和一个 rclone。

## 同名覆盖缺陷与修复

首次在默认安全模式通过 P: 覆盖既有 `同名覆盖.txt` 时，Windows 复制命令返回成功，手机服务正确拒绝覆盖并保留原文件，但 rclone `writes` 缓存让 P: 暂时显示未提交的新内容，并留下一个 `Dirty: true` 项。该状态会误导用户且阻止安全卸载。

修复后，安全模式使用 `--vfs-cache-mode off`，完全读写模式继续使用可恢复的 `writes` 缓存。依据 [rclone 官方 VFS 文档](https://rclone.org/commands/rclone_mount/)，`writes` 会先缓冲写入并在失败后重试；直接模式把写入交给远端。没有使用 `--immutable`，因为 [官方说明](https://rclone.org/docs/#immutable)只保证 copy/sync/move 等传输命令的不可变语义，不能作为 mount 行为的可靠依据。

相同真机覆盖复验中，Android 和重新打开的 P: 都保持原内容，没有替换文件落盘，也没有脏缓存。另一个新的组合名称文件在直写模式成功落到 Android，独立 SHA-256 匹配。PowerShell `Copy-Item` 仍未报告关闭时的远端冲突，这是 WinFsp/rclone 当前可见限制；安全属性和最终可见内容正确，但后续模式界面必须明确说明安全模式不会替换既有文件。

## 自动检查与清理

- `PhoneBridge.Mounting.Tests`：44/44 通过。
- `scripts/Verify-Windows.ps1`：Release 构建 0 warnings/0 errors，Windows 279/279 通过。
- P:、rclone、桌面进程、手机专用目录、本地二进制样本、`SharingService`、8273 监听和当前共享 WakeLock 均已清理。
- 手机开始/停止共享由已授权 ADB 自动操作完成；没有要求用户重复点击。

结构化摘要见[证据文件](p1-022/path-names-live.json)。DATA-04 的代表性名称、内容、重命名和同名保护已取得 Samsung 真机证据；未验证全部 Unicode 字符、Windows 保留名、大小写碰撞或大目录，这些不是本任务实际使用场景。唯一下一任务为 P1-023 三种访问模式与用户设置收口。
