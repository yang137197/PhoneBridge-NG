# 许可与商业化边界

## 当前已确认事实

- 当前 PhoneBridge NG 导入并修改了上游 `ysachin26/PhoneBridge` 的 GPL-3.0-or-later 源码，具体来源见 [NOTICE](../NOTICE.md)。
- 当前仓库、`v0.2.0` 已发布二进制及其对应源码继续受 GPL-3.0-or-later 约束；不得删除既有许可证、版权或作者声明。
- GPL 允许收费销售软件、提供付费下载、支持或订阅服务，但向用户分发包含 GPL 代码的客户端时，仍须履行对应源码和 GPL 授权义务。
- 把 GitHub 仓库设为私有不会把已经按 GPL 分发的代码变成专有软件，也不会撤销接收者已经取得的 GPL 权利。

官方依据：

- [GNU GPL FAQ](https://www.gnu.org/licenses/gpl-faq.html)
- [GNU GPLv3 正文](https://www.gnu.org/licenses/gpl-3.0.html)
- [GitHub：设置仓库可见性](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility)

## 闭源商业版的允许路径

如果后续商业客户端必须闭源，只能在发布前完成以下路径之一：

1. 从所有必要著作权人取得覆盖相关代码的独立商业/专有授权，并保留可审计的授权范围；或
2. 建立独立仓库，由未接触或不复用 GPL 实现代码的团队按公开协议和产品需求进行 clean-room 重写，并重新核对全部第三方依赖许可。

在上述条件未完成前，不得把现有代码、安装包或后续衍生版本标记为 proprietary、closed source 或专有许可。

## 订阅制边界

商业化目标记录为订阅制，但具体功能、价格、账户、支付、授权服务器和隐私条款尚未确定。可选方向包括：

- 保持客户端 GPL 开源，对独立云服务、团队管理、托管能力或商业支持收费；
- 在完成商业授权或 clean-room 重写后，以专有客户端提供订阅。

服务与 GPL 客户端是否构成同一衍生作品取决于实际架构和交互方式，开发前应由专业法律顾问复核；本文件不是法律意见。

## 发布门禁

- 在闭源路径确定并完成许可复核前，不修改或删除 `LICENSE`、`NOTICE.md` 和既有 Release 的对应源码材料。
- 不把“仓库私有”当作“许可证已转换”或“闭源合规完成”的证据。
- 商业版必须使用独立产品范围、版本、交付清单和许可审计，不得直接把当前 GPL 版本改名后作为闭源版本分发。

