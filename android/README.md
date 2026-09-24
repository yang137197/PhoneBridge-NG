# Android NG 开发工程

`app` 是新 NG 应用，应用 ID 为 `org.phonebridge.ng`，与实验 `com.phonebridge` 隔离。它整合限时短码配对、手机批准/撤销、HTTPS 认证、三种 WebDAV 访问模式、mDNS 发布和前台共享服务。默认安全模式，已配对电脑可分别设置只读、安全或完全读写。当前默认交付已使用项目外长期身份签名的第三方侧载 APK，并通过 Samsung 迁移与同签名覆盖升级验收。

[Kotlin 配对核心](pairing-core/README.md)与 [Keystore 配对存储](credentials-store/README.md)保留独立构建，App 直接引用相同源文件并由 AGP 内置 Kotlin 2.2.10 编译，避免源码分叉。应用构建用固定 AGP 9.1.1、Gradle 9.3.1、JDK 21、BC 1.86；严格依赖锁和 SHA-256 校验。入口为 [Verify-AndroidApp](../scripts/Verify-AndroidApp.ps1)，原生和 C# 联动见[复验说明](../tests/integration/android_pairing/README.md)。Debug 使用开发签名，Release 产物未签名、不用于分发。

固定方向为原生 Kotlin、系统允许的共享存储访问、HTTPS/WebDAV、mDNS、配对和前台服务；细节见 [ARCHITECTURE](../ARCHITECTURE.md) 和 [SECURITY](../docs/SECURITY.md)。P0-007 确定 minSdk 26、本阶段 compile/target 36 与 connectedDevice 服务，兼容承诺限于已记录的实测范围。正式 APK和 Windows 安装器的唯一入口见 `.audit/delivery/output` 及 [本地交付说明](../docs/LOCAL_DELIVERY.md)。

`.audit/upstream/android` 是未修改基线，当前隔离修复副本为 `.audit/p0-009-worktree/android`，此前实验副本保留；源码改动以补丁保存，来源与 GPL 文本见 [NOTICE](../NOTICE.md)。P0-009 限定 TLS 验证及真机 LAN 通过，P0-010 已验证真机最小文件链路，见[记录](../docs/audit/P0-009-VALIDATION.md)。Phase 0 验收前不在此大规模重写 Android 核心。
