# P1-031 验收

结果：通过限定验收。标准本地交付目录已由 P1-030 最终源码重建，Windows 自启动只进入托盘，电脑重启后由用户手动点击连接并挂载。

## 已确认

- `scripts/Build-LocalDelivery.ps1` 成功完成固定 .NET 10.0.401、Gradle 9.3.1、Android build-tools 36.0.0、rclone 1.75.1、WinFsp 2.1.25156 和 Inno Setup 7.1.0 的核对与构建。
- Windows 安装器：`.audit/delivery/output/PhoneBridge-NG-Setup-0.1.0.exe`，81,066,194 字节，SHA-256 `7D6951115352384BA9864657D6C92F99BEE09EC09C4B8FC3FC6946FB58B1496D`。
- Android APK：`.audit/delivery/output/PhoneBridge-NG-0.1.0-local-test.apk`，3,483,622 字节，SHA-256 `B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC`。
- APK v2 签名验证通过；签名证书 SHA-256 为 `7D5EC604BC5C40BC294617BD62BD00E2C4E4063F8326783E34D26CEE0CA7CBFF`，与 manifest 一致。
- 交付 Windows DLL 与 P1-030 已验收 DLL 的 SHA-256 同为 `F0FF0BADC9DAB5F18B8E7128CCC972EDEDBBC6E450E204551EB199D533381C09`，证明安装器包含最终手动挂载实现。
- `delivery-manifest.json`、`SHA256SUMS.txt` 与实际文件散列全部一致。构建后没有 PhoneBridge Desktop 或 rclone 进程，P: 不存在。

机器可读证据：`.audit/runs/P1-031/final-local-delivery-verification.json`。

## 未验证与边界

本次未安装新包、未重启电脑、未连接手机、未重复传输测试。P1-030 已对相同 Windows DLL 完成安装版和 `--startup` 验收；Android APK散列未变化，因此重复真机测试没有新增验证价值。

Windows 安装器 Authenticode 状态为 `NotSigned`，Android 使用本地 Debug 测试证书，均只作为第三方本地预览制品。没有验证商业签名、公开发布、Windows ARM64 或全新无 WinFsp 电脑。
