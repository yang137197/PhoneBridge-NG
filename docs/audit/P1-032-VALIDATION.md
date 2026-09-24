# P1-032 验收

结果：通过限定验收。当前唯一交付目录包含与产品界面和最终手动挂载要求一致的 `README.txt`，安装器、APK和说明均由清单标识。

## 已确认

- `docs/INSTALL_LOCAL.txt` 使用当前界面的“开始共享”“配对新电脑”“配对并连接”“允许配对”“连接 / 恢复确认”“卸载”“停止共享”和“导出诊断包”等实际名称。
- 说明明确：启用 Windows 自启动只进入托盘；电脑重启或重新登录后仍需用户手动连接。
- 构建输出和 Windows 发布目录中的 `README.txt` 与源码说明 SHA-256 均为 `B303D02700D544925F72EC7A1C340759468A8C3D3762D4F3AF69D31807C9668E`。
- Windows 安装器为 81,063,671 字节，SHA-256 `91E7360C36209886C28D8E35A3AEAE1305DBDCBE01312D138B958B040A4F93F6`。
- Android APK为 3,483,622 字节，SHA-256 `B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC`；APK v2 签名与证书摘要通过。
- `delivery-manifest.json` 和三行 `SHA256SUMS.txt` 与实际文件完全一致。
- Windows 发布 DLL 与 P1-030 已验收 DLL 的 SHA-256 同为 `F0FF0BADC9DAB5F18B8E7128CCC972EDEDBBC6E450E204551EB199D533381C09`。
- 构建后没有 PhoneBridge Desktop 或 rclone 进程，P: 不存在。

机器可读证据：`.audit/runs/P1-032/local-delivery-entry-verification.json`。

## 未验证与边界

本次没有安装新生成的安装器、连接手机或重启电脑。产品 Windows DLL 和 Android APK 均未改变，相关安装版及真机行为已由 P1-030/P1-031 验收；本次只改变随包说明与清单。

Windows 安装器仍未使用商业 Authenticode 签名，APK仍使用本地测试证书，只用于第三方本地预览。
