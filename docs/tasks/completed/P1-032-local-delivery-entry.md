# P1-032 — 本地交付入口与最短使用说明

状态：已完成。日期：2026-09-23。

## 目标

让用户拿到本地交付目录后可以直接识别正确安装器、APK和实际操作顺序，明确电脑重启后必须手动连接，避免误用历史自动挂载行为。

## 范围

新增与当前界面文字一致的本地安装、首次配对、以后使用、访问模式、正确结束、故障入口和散列核对说明；构建时将说明复制到交付目录及 Windows 安装目录，并纳入交付 manifest 和 SHA-256 清单。

## 不做什么

不增加向导、自动安装 APK、自动连接或自动挂载功能；不安装产物、不连接手机、不重启电脑、不发布到外部平台。

## 涉及文件

- `docs/INSTALL_LOCAL.txt`：交付给最终用户的纯文本说明。
- `scripts/Build-LocalDelivery.ps1`：复制说明并记录散列。
- `docs/LOCAL_DELIVERY.md`、`README.md`：开发者入口和当前状态。

## 实现

新增纯文本 `docs/INSTALL_LOCAL.txt`，内容逐项采用当前 Android 和 Windows 界面中的实际按钮名称。说明覆盖第三方侧载、首次配对、电脑重启后的手动连接、手机端访问模式、安全卸载、诊断导出和散列核对。

`Build-LocalDelivery.ps1` 现在把说明复制为交付目录与 Windows 发布目录中的 `README.txt`。说明文件作为 manifest 的 `instructions` 项，并与安装器和 APK 一起写入 `SHA256SUMS.txt`。README 和本地交付说明明确 `.audit/delivery/output` 是当前唯一交付目录，历史 `.audit/runs` 不作为安装入口。

## 测试

- 本地交付完整重建成功：.NET locked restore/publish、Android Release assemble、APK v2 签名和 Inno Setup 编译均完成。
- 安装器 SHA-256：`91E7360C36209886C28D8E35A3AEAE1305DBDCBE01312D138B958B040A4F93F6`，81,063,671 字节。
- APK SHA-256：`B6831F1C580C1337E9A0FDC4DB8BB21FD328AE90D9E0C2625DB61D170C6D33BC`，3,483,622 字节。
- README SHA-256：`B303D02700D544925F72EC7A1C340759468A8C3D3762D4F3AF69D31807C9668E`，2,974 字节；源码、交付目录和 Windows 发布目录三份一致。
- manifest 和 `SHA256SUMS.txt` 与三份实际文件一致；APK v2 签名和证书摘要通过；Windows DLL 与 P1-030 已验收版本一致。
- 构建后客户端进程 0、rclone 进程 0、P: 不存在。证据为 `.audit/runs/P1-032/local-delivery-entry-verification.json`。

## 验收结果

已完成并通过限定验收。交付目录现在包含用户可以直接阅读的操作说明，且说明和二进制由同一次构建生成、共同纳入机器可核对的散列清单。

未安装新包或重复真机测试，因为产品二进制未改变，Windows DLL 与 P1-030 已验收版本一致，APK散列也未变化。唯一下一任务：核对并修正 `PROTOCOL.md`、`SECURITY.md`、`PAIRING.md` 中仍把已实现配对链路写成“待实现”的过期状态，避免开发依据互相矛盾。
