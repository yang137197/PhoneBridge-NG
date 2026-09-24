# P1-021 视频播放与随机 Seek

状态：已完成。验收日期：2026-09-22。

## 目标

在 Samsung 真机共享目录和 Windows P: 挂载路径上验证可播放视频的直接播放与多个随机位置 Seek，确认每次读取的字节区间、响应状态和媒体内容正确，覆盖 DATA-02。

## 范围

使用专用合成或可公开复现的视频样本，先记录本地基线时长、关键帧和散列，再经 P: 使用 Windows 媒体播放器播放并跳转多个预定位置。同步采集有界的服务端 Range/响应与读取量证据，核对播放期间 P:、桌面客户端和单一 rclone 状态，最后安全卸载并清理测试数据。

## 不做什么

不优化缩略图或转码，不改变 Range 协议来迁就播放器，不测试 20 GB 传输、磁盘满、重启、睡眠或多设备并发，不使用用户私人视频。

## 涉及文件

- `tests/integration/media_seek/Compare-RandomRanges.ps1`：P: 固定区间随机读取与基线散列比较。
- `tests/integration/media_seek/PhoneBridge.MediaSeekProbe`：现代 Windows Media Player 引擎的多位置播放推进与解码帧探针。
- `tests/integration/media_seek/PhoneBridge.RangeProbe`：使用受保护配对凭据、固定 CA 和严格 TLS 的直接 HTTP Range 验收探针。
- `.audit/p1-021-data`：忽略的公开样本、基线和原始结果。

只有复现出产品缺陷时才修改运行代码。

## 实现

采用 Blender Foundation 官方 Big Buck Bunny 1080p H.264/MP4 公开样本；Windows 对 H.264/MP4 的原生支持以 Microsoft 官方文档为依据。本地基线已确认文件为 276,134,947 字节、1920×1080、30 fps、约 634.53 秒，SHA-256 为 `ae51005850b0ff757fe60c3dd7a12d754d3cd2397d87d939b55235e457f97658`。MP4 的 `moov` 位于 `mdat` 前；五个固定 1 MiB 区间覆盖文件头、媒体数据约 25%/50%/75% 和尾部。样本已推送到 Samsung 专用目录并由 Android 独立哈希确认一致。产品代码尚未修改。

新增三个有界验收工具：严格验证五个 HTTP `206`/`Content-Range`/长度/散列的直接 HTTPS Range 探针、通过 P: 读取相同五个区间的散列比较脚本，以及使用 `Windows.Media.Playback.MediaPlayer` 在 30/180/360/540 秒播放并统计解码帧的媒体探针。配对凭据只从当前用户受保护存储读入内存，探针不输出 Token、设备 ID、证书或任意响应正文。

## 测试

Samsung SM-S9180/API 36 共享 `Music` 后，直接 HTTPS 的五个 1 MiB Range 均返回精确 `206`、正确 `Content-Range` 和匹配散列；P: 上相同五个区间共 5,242,880 字节也全部匹配。Windows 媒体引擎从 P: 打开 634.533 秒、1920×1080 的文件，在四个目标位置均于目标附近开始、连续推进约 2 秒且解码 325–338 帧。第二轮相同播放测量期间，Android 应用 UID 发送字节增加 165,704,272；该数值可能包含同一应用同期的少量控制流量。

播放期间 P: 始终存在，桌面客户端和 rclone 均只有一个。用户正常卸载后，P: 不存在、rclone 为 0、桌面客户端仍为一个；手机专用目录和本地两个大型公开样本已按精确路径清理。两个 C# 探针的 Release 构建均为 0 warnings/0 errors，PowerShell 脚本语法检查为 0 errors。

## 验收结果

通过限定范围。DATA-02 的真机 Range、内容、实际播放、随机 Seek、读取量和安全卸载均已验证。手机停止共享后 `SharingService`、8273 监听与当前 WakeLock 均不存在，最终 P:、rclone 和桌面进程均为 0。未验证其他编码格式、第三方播放器、转码、缩略图或多设备同时播放。证据见 [P1-021 验收](../../audit/P1-021-VALIDATION.md)；唯一下一任务为 P1-022 文件名与路径边界完整性。
