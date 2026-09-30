# P2-027 — Windows 更新检查可靠性

状态：已完成。日期：2026-09-30。

## 目标

核对 Windows v0.2.5 在有网络且官方 Latest 同为 v0.2.5 时曾显示“检查更新失败”的证据；修复已确认的错误分类，使 GitHub 限流不再误报成网络故障，并阻止重置时间前的重复请求。成功取得同版正式 Release 时继续显示“当前已是最新版”。

## 范围

- 核对已安装 v0.2.5 的实际更新记录、官方 GitHub Latest 响应和诊断日志。
- 识别 GitHub 主限流响应，显示独立提示并在当前进程内遵守服务端重置时间。
- 为限流、恢复和中英文资源补充定向自动测试，运行完整 Windows 门禁。

## 不做什么

- 不把无法访问 GitHub、超时、限流或无效响应误报成“当前已是最新版”。
- 不在本任务实现 Windows 下载进度、Windows/Android 首次使用引导或其他功能。
- 不安装候选、不推送、不打标签、不创建 GitHub Release。

## 涉及文件

- `windows/src/PhoneBridge.Desktop/UpdateService.cs`
- `windows/src/PhoneBridge.Desktop/DiagnosticLog.cs`
- `windows/src/PhoneBridge.Desktop/MainWindow.xaml.cs`
- Windows 中英文资源
- `windows/tests/PhoneBridge.Desktop.Tests/UpdateServiceTests.cs`
- `windows/tests/PhoneBridge.Desktop.Tests/LocalizationTests.cs`

## 实现

- 用户导出的 v0.2.5 诊断包确认：2026-09-30 09:15–09:21 的五次检查均记录 `Failure`，10:27 同一客户端记录 `Success/Healthy` 并显示“当前已是最新版本”；日志器状态为 `available`。
- GitHub Release 的创建、发布和更新时间均为 2026-09-29，失败期间没有 Release 变更；实时请求返回正式 `v0.2.5` 和 6 项资产。
- GitHub 官方规则是：未认证请求按来源 IP 每小时 60 次；主限流返回 403 或 429，`X-RateLimit-Remaining` 为 0，并要求等到 `X-RateLimit-Reset` 后再请求。失败时间线与限流窗口吻合，但旧版日志没有保存响应状态或限流头，因此“旧故障就是限流”仍是有较强证据的推断，不写成已直接确认。
- 已确认的产品缺陷是旧版把限流产生的 HTTP 异常与普通网络失败统一映射为 `UpdateCheckFailed`，会错误提示用户检查网络。
- `UpdateService` 现在识别 429，或带 `X-RateLimit-Remaining: 0` 的 403；优先遵守 `Retry-After`，否则使用 `X-RateLimit-Reset`，缺失或过期时最少等待一分钟。在重置前重复点击只返回 `UpdateRateLimited`，不再发起 GitHub 请求；到期后自动恢复正常检查。
- Windows 界面新增中英文限流提示，诊断日志记录独立的 `RateLimited` 结果。正常取得 Latest 后，现有 `Current` / `Available` 判断和下载完整性校验均未改变。

## 测试

- 通过：实时 GitHub Latest 请求，HTTP 200，正式 `v0.2.5`，6 项资产；同一台电脑以与客户端一致的 .NET `HttpClient` 请求成功。
- 通过：Desktop 定向 8/8，覆盖主限流、重置前不重复请求、重置后恢复为 `Current` 及中英文资源。
- 通过：`scripts/Verify-Windows.ps1`；Release 构建 0 警告、0 错误，Windows 335/335 测试通过。TRX 位于 `.audit/windows-verification/20260930-103331/`。

## 验收结果

通过。Windows 能区分 GitHub 限流与普通网络失败，遵守服务端重置时间，并在成功取得同版正式 Release 时继续显示“当前已是最新版本”。旧版五次失败的底层 HTTP 状态因旧日志信息不足无法事后直接确认；若未来非限流失败再次出现，必须依据新证据另立单一根因任务，不能把它推定为限流。
