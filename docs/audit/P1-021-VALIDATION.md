# P1-021 视频播放与随机 Seek 验收

日期：2026-09-22  
结果：通过限定范围。

## 环境与样本

- Windows 11、WinFsp、项目固定 rclone 1.75.1、Release Windows 客户端。
- Samsung SM-S9180，Android 16/API 36，共享根为 `Music`。
- 样本来自 [Blender Foundation Big Buck Bunny 官方下载目录](https://download.blender.org/demo/movies/BBB/)，许可为 CC BY 3.0；ZIP 与 MP4 只用于专用测试目录。
- MP4 为 276,134,947 字节，SHA-256 `ae51005850b0ff757fe60c3dd7a12d754d3cd2397d87d939b55235e457f97658`，1920×1080、30 fps、634.533 秒；Android 独立哈希与本地基线一致。
- 顶层 `moov` 位于 `mdat` 前，适合有界随机读取。Windows H.264/MP4 能力依据 [Microsoft 支持的编解码器](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/supported-codecs)。

## Range 与内容正确性

使用受保护的现有配对记录、固定 CA、严格 TLS 和 HTTP/1.1，直接访问手机端五个固定 1 MiB 区间。五次响应均为 `206`，`Content-Range`、内容长度、完整文件长度和 SHA-256 全部精确匹配；总读取量为 5,242,880 字节。探针没有输出认证材料、设备 ID、证书或响应正文。

通过 P: 对相同五个区间执行随机读取，0、约 25%、50%、75% 和文件尾部的 SHA-256 均与本地基线一致，总读取量同为 5,242,880 字节，用时 0.892 秒。期间 P: 存在，桌面客户端与 rclone 均只有一个。

## 播放与随机 Seek

按 [Microsoft MediaPlayer 指南](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/play-audio-and-video-with-mediaplayer)使用 `Windows.Media.Playback.MediaPlayer` 直接打开 P: 文件，并通过 [MediaPlaybackSession.Position](https://learn.microsoft.com/en-us/uwp/api/windows.media.playback.mediaplaybacksession.position)设置播放位置。

30、180、360、540 秒四次 Seek 均在目标附近进入 `Playing`，各连续推进约 2.0 秒并解码 325–338 帧；引擎回读的文件长度、时长和 1920×1080 分辨率与本地基线一致。完整一轮用时 10.786 秒。

第二轮相同探针前后读取 Android `dumpsys netstats` 的应用 UID 累计值，11.272 秒内手机发送增加 165,704,272 字节、接收增加 1,173,628 字节。该差值能证明实际播放产生了有界网络读取，但同一应用同期的少量控制流量也计入其中，因此不把它描述为纯媒体负载的精确字节数。

## 卸载与清理

用户通过 Windows 客户端正常卸载。回读确认 P: 不存在、rclone 为 0、桌面客户端仍为一个。随后只删除手机 `Music/PhoneBridge-P1-021` 专用目录和电脑上的 ZIP/MP4 大样本，保留小型脱敏结果。

用户在手机端正常停止共享后，`SharingService`、8273 监听和当前 Wake Locks 区段中的 `PhoneBridge-NG:sharing` 均不存在。`dumpsys power` 仍保留既往 ACQ/REL 历史事件，未把历史记录误判为当前持锁。最后关闭空闲 Windows 客户端，P:、rclone、桌面进程均为 0。结构化摘要见[证据文件](p1-021/media-seek-live.json)。

## 自动检查与限制

两个 C# 验收探针的 Release 构建均为 0 warnings/0 errors；PowerShell 随机区间脚本语法检查为 0 errors。产品代码未改变，因此没有重复运行 P1-019 已通过的 279 项 Windows 回归。

DATA-02 的视频直接播放、多个随机位置、Range 响应、内容正确性、实际读取量、单实例和安全卸载获得 Samsung 真机证据。未验证其他编码格式、用户私人视频、第三方播放器、转码、缩略图或多设备同时播放；这些限制不能据此标记为整个 MVP 完成。唯一下一任务为 P1-022 文件名与路径边界完整性。
