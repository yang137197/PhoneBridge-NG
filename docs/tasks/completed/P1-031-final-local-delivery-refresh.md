# P1-031 — 最终本地交付包刷新

状态：已完成。日期：2026-09-23。

## 目标

用 P1-030 已验收的“Windows 自启动仅进入托盘、用户手动连接挂载”源码刷新标准本地交付包，确保提供给用户的安装器不再包含旧的登录自动挂载实现。

## 范围

同步根项目约束中的最终自启动行为；使用固定依赖和现有本地测试签名生成 Windows x64 安装器、Android 本地测试 APK、SHA-256 清单和交付 manifest；核对产物来源、散列、签名状态及关键 Windows 程序集与已验收构建一致。

## 不做什么

不发布 GitHub Release，不上传应用商店，不生成或更换签名密钥，不安装产物，不连接手机，不重启电脑，不重复真机或文件传输测试。

## 涉及文件

- `AGENTS.md`：记录最终自启动和手动挂载约束。
- `.audit/delivery/`：本地构建输出与机器可读清单，不纳入源码。
- `README.md`、`docs/LOCAL_DELIVERY.md`：交付状态与使用边界。
- 本任务记录及验收记录。

## 实现

根 `AGENTS.md` 的首次使用、开机启动和 MVP 描述已统一为用户最终要求：自启动只进入托盘，重启或重新登录后由用户手动连接；自动恢复仅属于已经手动建立的活动连接。

固定构建脚本成功刷新 `.audit/delivery/output`。Windows 安装器内的 `PhoneBridge.Desktop.dll` SHA-256 为 `F0FF0BADC9DAB5F18B8E7128CCC972EDEDBBC6E450E204551EB199D533381C09`，与 P1-030 已验收发布 DLL 完全一致。Android 源码本次未改，APK保持相同散列和测试签名证书。

## 测试

- `scripts/Build-LocalDelivery.ps1`：成功；.NET locked restore/publish、Android Release assemble、zipalign、APK v2 签名和 Inno Setup 7.1.0 编译完成。
- Windows 安装器 SHA-256：`7D6951115352384BA9864657D6C92F99BEE09EC09C4B8FC3FC6946FB58B1496D`，大小 81,066,194 字节；与 manifest 和 `SHA256SUMS.txt` 一致。
- Android APK SHA-256：`B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC`，大小 3,483,622 字节；v2 签名通过，证书 SHA-256 与 manifest 一致。
- 构建后客户端进程 0、rclone 进程 0、P: 不存在。证据为 `.audit/runs/P1-031/final-local-delivery-verification.json`。

## 验收结果

已完成并通过限定验收。标准本地交付包现在对应最终手动挂载源码。未安装或重复运行真机测试，因为相同 Windows DLL 已在 P1-030 安装版验收，Android 二进制未变化。

未验证商业代码签名、公开分发、Windows ARM64 或未安装 WinFsp 的全新电脑；这些均不属于当前第三方本地使用范围。唯一下一任务：整理实际交付入口与最短安装使用说明，避免用户误用历史构建产物。
