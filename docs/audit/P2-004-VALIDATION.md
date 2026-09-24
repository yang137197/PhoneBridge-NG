# P2-004 两端本地移除与截屏策略验证

日期：2026-09-24。状态：实现与隔离验证完成，等待真实配对验收。

## 制品

| 平台 | 本地文件 | SHA-256 | 大小 |
| --- | --- | --- | ---: |
| Windows | `.audit/ui-acceptance/PhoneBridge-NG-Windows-v0.2-ui-preview-r7.zip` | `C9D375AC16AEC445AD2E35650CE10B06C4013E3CFBE42243F07BDA73138CD504` | 39,560,586 bytes |
| Android | `.audit/ui-acceptance/android/PhoneBridge-NG-v0.2-ui-preview.apk` | `6A99753BA44BFCF1C06F3E7055F0F4BCDFE9B65AB94958C798990A250AFF2A35` | 4,149,724 bytes |

两者均为本地验收候选，不是正式发布包。Android 包名为 `org.phonebridge.ng.uipreview`，使用调试签名和独立数据。

## 已验证事实

- Windows 完整 Release 构建 0 警告/0 错误，281/281 测试通过。新增隔离测试确认本地删除不访问网络、受保护记录消失，并可为同一手机创建全新 Pending 配对。
- Android Debug/Release、测试 APK、单元测试和 lint 共 123 个任务成功；`uiPreview` assemble/lint 39 个任务成功。
- Samsung 上两条限定 instrumentation 测试 2/2 通过：Android 本地删除会清空该 client 记录、使旧 token 返回 401，并允许重新批准新记录；测试使用随机隔离存储，结束后 Debug 与测试包已卸载。
- 更新后的 `uiPreview` 已覆盖安装。`adb screencap` 成功取得 131,977-byte 正常界面 PNG，证明运行中的 Android 验收包不再阻止截屏。
- Windows 验收窗口已从 r7 内容启动；Windows 本身没有新增截屏阻挡。

## 边界与未验证

- 未删除任何真实配对。真实活动挂载安全停止、本端记录消失、另一端无需确认和重新配对的端到端流程仍未验证。
- 本地移除不联系远端，因此不能同时删除远端设备自己的旧记录。Windows 移除后手机可能保留旧授权；Android 移除后 Windows 可能保留旧配对记录，直至用户在该端也移除或重新进入新配对流程。
- 不删除已复制文件、诊断日志或全局设置；“全部相关数据”在本任务中限定为本端设备配对记录、凭据、访问模式、临时端点、活动连接和进程内恢复状态。
- 正式签名 APK、Windows 安装器、升级路径和真实双设备链路未验证。

## 唯一下一任务

使用可重建的真实配对完成两端各一次本地移除与全新重配验收；通过前不开始 P2-005 多设备核心。
