# P2-004 — 两端本地移除与截屏策略

## 目标

按用户在 P2-003 复验通过后新增的要求，统一两端的本地移除语义，并允许 Windows、Android 截屏。

## 范围

- Windows“移除此手机”停止该手机的活动挂载，删除本机受保护配对记录、临时地址和进程内自动重连状态，不请求或等待手机确认。
- Android“移除此电脑”从本机加密配对库删除该电脑的名称、访问模式和凭据验证值，并中止其活动连接，不请求远端确认。
- 删除后不复用旧凭据；再次连接必须经过新的配对流程。
- Android 所有 build type 不再设置 `FLAG_SECURE`，Windows 保持可截屏。
- 刷新本地 UI 验收制品，不发布正式 Release。

## 非目标

- 不开始按 `device_id` 隔离的多会话重构。
- 不承诺单端离线移除可以删除另一端的本地记录；不发送远端撤销请求就无法保证远端也已清除。
- 不删除用户已经复制的文件、诊断日志或与设备移除无直接关系的全局设置。
- 不覆盖或发布 v0.1.0 正式交付。

## 实现

- Windows 设备设置只保留一个“移除此手机”；确认后调用本地删除路径，不再调用远端 self-revocation。成功后清除临时地址并返回设备页。
- Android 加密配对库增加带 revision 校验的精确删除；本地 UI 通过同一授权屏障先阻断该 client、持久删除记录，再关闭其 socket 并刷新列表。远端 `DELETE /phonebridge/v1/pairings/self` 仍保留原撤销墓碑语义，兼容旧客户端及配对取消流程。
- Android `MainActivity` 不再设置 `FLAG_SECURE`，并删除 build type 截图开关资源。

## 已验证

- Windows `scripts/Verify-Windows.ps1`：Release 0 警告/0 错误，281/281 测试通过；新增测试确认本地删除不产生网络请求、记录消失且相同手机可创建全新 Pending 配对。
- Android `scripts/Verify-AndroidApp.ps1`：123 个任务成功，覆盖 Debug/Release、测试 APK、单元测试与 Debug/Release lint；`assembleUiPreview lintUiPreview` 39 个任务成功。
- Samsung `R5CW429DKDN` 运行两条隔离 instrumentation 测试：Android 本地删除后记录为空、旧 token 为 401，并允许重新批准为新配对；2/2 通过。临时 Debug/测试包随后卸载。
- 更新后的 `org.phonebridge.ng.uipreview` 已覆盖安装；实际 `screencap` 获得正常像素图，不再被安全策略置黑。
- 本地制品及散列见 `docs/audit/P2-004-VALIDATION.md`。

## 未验证

- 未在真实已配对 Windows/Android 用户记录上执行删除，避免未经用户单独确认而破坏现有配对。
- Windows 本地移除不会联系手机，故手机可能保留旧电脑授权；Android 本地移除也不会删除 Windows 保存的旧记录。两端各自删除或重新配对前的旧记录提示仍需真实流程验收。
- 正式签名 Android APK 与正式 Windows 安装器尚未刷新；当前只提供本地验收包。

## 唯一下一任务

使用一组可重建的真实配对，分别验证 Windows 本地移除和 Android 本地移除：确认本端记录与活动连接消失、另一端无需确认、旧凭据不能连接，并按全新设备重新配对。通过前不开始 P2-005 多设备核心。
