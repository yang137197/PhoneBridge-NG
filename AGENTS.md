# PhoneBridge NG — 项目开发交接

## 0. 项目定位

项目名称暂定：

**PhoneBridge NG**

上游参考项目：

`ysachin26/PhoneBridge`

上游已确认采用 GPLv3。开始开发前再次核对仓库 `LICENSE`。

本项目目标不是简单 Fork 后修补 UI，而是：

> 基于 PhoneBridge 已验证的 Android → HTTPS/WebDAV → rclone → WinFsp → Windows Explorer 技术路线，重新开发一个成熟、稳定、中文优先的 Android ↔ Windows 无线文件系统工具。

最终用户体验：

```text
Windows 文件资源管理器

此电脑
├─ C:
├─ D:
└─ Galaxy S23 Ultra (P:)
   ├─ DCIM
   ├─ Pictures
   ├─ Download
   ├─ Movies
   ├─ Music
   └─ Documents
```

用户无需：

* 注册账号
* 登录云服务
* 手动输入 IP
* 每次重新配对
* 使用数据线
* 打开浏览器
* 安装 FTP/SFTP 管理器

目标体验：

```text
手机和电脑进入同一 Wi-Fi
        ↓
自动发现
        ↓
用户点击连接
        ↓
Windows 自动出现手机盘符
        ↓
直接通过 Explorer 管理文件
```

---

# 1. 核心原则

必须遵守。

## 1.1 产品原则

优先级：

1. 稳定
2. 数据安全
3. 文件系统兼容性
4. 使用简单
5. 性能
6. UI 美观
7. 附加功能

任何“漂亮但不稳定”的功能不得优先于基础文件操作。

---

## 1.2 非目标

第一阶段禁止加入：

* 云盘
* 用户账号系统
* 广告
* 遥测
* 数据分析 SDK
* 商业会员体系
* 聊天
* 手机通知同步
* 手机投屏
* 剪贴板同步
* AI
* NAS 功能
* 文件在线编辑
* 自建文件系统驱动
* Root 功能

不要把项目开发成 KDE Connect、AirDroid 或 Phone Link 的综合替代品。

核心只做：

> **无线访问 Android 文件。**

---

# 2. 当前已经确认的技术路线

上游 PhoneBridge 已实现或已经验证的基本链路：

```text
Android
│
├─ Kotlin
├─ 文件访问
├─ WebDAV
├─ HTTPS
└─ mDNS
       │
       │ Wi-Fi
       ▼
Windows
│
├─ rclone
├─ VFS
├─ WinFsp
└─ Windows Explorer
       │
       ▼
手机作为盘符
```

这个方向保留。

不要因为“可以重新设计”而自行更换：

* SMB
* FTP
* SFTP
* MTP
* WebSocket 文件协议
* 自研文件系统驱动

除非后续真实测试证明当前路线存在无法解决的根本问题。

---

# 3. 技术栈

## Android

继续：

**Kotlin 原生 Android**

核心模块：

```text
Android App
├─ 文件访问
├─ MediaStore
├─ MANAGE_EXTERNAL_STORAGE
├─ WebDAV Server
├─ HTTPS
├─ mDNS
├─ 配对
├─ Foreground Service
└─ 设备状态
```

必须遵守 Android 官方文件访问权限模型。

不 Root。

正常情况下目标访问：

```text
DCIM
Pictures
Download
Documents
Movies
Music
Android/media
```

不要承诺访问：

```text
/data/data
其他 App 私有目录
Android/data 中受系统限制的目录
```

---

# 4. Windows 客户端

不要继续把现有 Python GUI 当正式架构。

原 Python 客户端仅作为：

> 功能参考代码。

新版 Windows 客户端采用：

**C# + 当前稳定 LTS .NET + WPF**

Codex 开始开发前核对当前 .NET LTS 版本。

架构：

```text
PhoneBridge.Windows

├─ UI
├─ DeviceDiscovery
├─ Pairing
├─ MountManager
├─ RcloneManager
├─ WinFspManager
├─ CredentialManager
├─ CertificateManager
├─ CacheManager
├─ AutoReconnect
├─ Logging
├─ Settings
├─ Localization
└─ Update
```

---

# 5. rclone 与 WinFsp

第一阶段继续使用：

**rclone + WinFsp**

不要自行实现 Windows 文件系统驱动。

rclone 负责：

* WebDAV backend
* VFS
* 文件缓存
* 目录缓存
* read ahead
* write back
* reconnect

WinFsp 负责：

> 将 rclone 文件系统映射为 Windows 盘符。

---

# 6. 最终 Windows 体验

首次：

```text
安装 PhoneBridge
        ↓
检测/安装 WinFsp
        ↓
内置或部署 rclone
        ↓
打开 PhoneBridge
        ↓
发现 Android 手机
        ↓
配对
        ↓
用户点击连接并挂载
```

以后：

```text
开机
↓
PhoneBridge 后台启动
↓
进入托盘，等待用户操作
↓
用户打开窗口并点击连接
↓
验证设备并挂载 P:
```

Explorer：

```text
Galaxy S23 Ultra (P:)
```

手机离线：

```text
P: 自动卸载
```

手机重新出现：

```text
自动重新挂载
```

以上自动重新挂载只适用于用户已经手动建立的活动连接遇到短暂断网；Windows 重新登录、客户端重新启动或电脑重启后必须由用户再次点击连接，不得自动连接、启动 rclone 或创建盘符。

---

# 7. 配对设计

现有简单用户名/密码机制不能作为最终方案。

目标：

### 第一次

```text
Android
↓
生成设备身份
↓
显示二维码 / 一次性配对码
```

Windows：

```text
扫描二维码
或
输入一次性配对码
```

完成设备身份交换。

之后保存：

```text
Device ID
Public Key
Certificate Fingerprint
Device Name
```

以后：

```text
发现设备
↓
校验证书
↓
认证设备
↓
用户点击连接并挂载
```

不能继续长期依赖用户手动输入密码。

---

# 8. 安全要求

这是强制要求。

## 禁止

禁止：

```text
password → config.json
```

禁止明文保存：

* 密码
* Token
* 私钥
* 长期认证凭证

Windows 使用：

**Windows Credential Manager / DPAPI**

Android 使用：

**Android Keystore**

---

## HTTPS

现有：

```text
自签 TLS
+
--no-check-certificate
```

只能作为开发阶段。

正式版本需要：

```text
首次配对
↓
保存证书指纹
↓
以后执行 certificate pinning
```

如果证书发生变化：

```text
禁止静默接受
```

必须提示：

> 设备身份发生变化，需要重新配对。

---

# 9. 删除保护

文件删除属于高风险操作。

默认提供三种模式：

```text
只读模式
安全模式
完全读写模式
```

默认：

**安全模式**

安全模式：

允许：

* 打开
* 下载
* 上传
* 新建目录
* 重命名

删除：

必须明确确认。

后续考虑：

```text
PhoneBridge Trash
```

即：

```text
删除
↓
移动到手机 .PhoneBridgeTrash
↓
保留一定时间
↓
用户确认后彻底删除
```

第一版可以不实现自动清理，但架构不得阻止以后加入。

---

# 10. 中文

必须从第一版开始设计 i18n。

默认语言：

**简体中文**

同时准备：

```text
zh-CN
en-US
```

禁止：

> 先把所有中文/英文写死，以后再国际化。

所有 UI 文本必须来自资源文件。

---

# 11. 性能目标

重点处理：

```text
DCIM/Camera
```

可能存在：

```text
5000+
10000+
照片/视频
```

不能每次打开文件夹都全量重新读取整个文件内容。

需要：

* 目录元数据缓存
* rclone VFS cache
* directory cache
* read ahead
* thumbnail strategy
* 分页/异步加载
* 避免 UI 阻塞

---

# 12. 缩略图

第一阶段可以使用 Windows Explorer 原有缩略图能力。

第二阶段评估：

Android：

```text
MediaStore
↓
Thumbnail API
```

Windows：

```text
Thumbnail cache
```

目标：

打开包含几千张照片的 DCIM 时，不能为了生成缩略图而读取几千张完整原图。

---

# 13. 大文件

必须专门测试：

```text
1 GB
5 GB
10 GB
20 GB
```

包括：

```text
手机 → PC
PC → 手机
Explorer复制
拖放
视频直接播放
随机Seek
传输中Wi-Fi断开
重新连接
```

不能只用几个 KB 的测试文件证明“功能完成”。

---

# 14. 自动发现

继续使用：

**mDNS**

目标：

```text
同LAN
↓
自动发现
```

用户无需：

```text
输入 IP
输入端口
```

同时必须提供：

```text
手动添加 IP
```

作为故障排查备用方案。

---

# 15. 网络异常

必须处理以下情况：

```text
Wi-Fi切换
Wi-Fi断开
手机锁屏
手机息屏
手机重启
PC睡眠
PC唤醒
路由器重连
IP变化
Android进程被杀
```

正确目标：

```text
连接中断
↓
卸载/标记离线
↓
后台重新发现
↓
设备恢复
↓
重新挂载
```

禁止：

* 无限弹窗
* Explorer 长期卡死
* rclone 僵尸进程
* 多次重复挂载
* 同一个手机出现多个盘符

---

# 16. Android 后台

必须实现可靠的：

**Foreground Service**

显示低打扰通知：

```text
PhoneBridge
正在共享手机存储
```

允许：

```text
开始共享
停止共享
```

三星等厂商可能存在后台限制。

代码结构中必须考虑：

* Battery Optimization
* Doze
* Foreground Service
* 系统回收

但不要使用危险的厂商私有 Hack。

---

# 17. Windows 托盘

Windows 客户端支持：

```text
系统托盘
```

菜单：

```text
Galaxy S23 Ultra
  ├─ 打开
  ├─ 重新连接
  ├─ 卸载
  └─ 设置

设置
退出
```

默认关闭窗口：

> 最小化到托盘。

---

# 18. Windows 开机启动

用户可以选择：

```text
☑ Windows启动时自动运行 PhoneBridge
```

不要偷偷开启。

第一次安装默认可以推荐，但必须用户决定。

启用后只启动唯一客户端并进入托盘，不自动连接手机、不启动 rclone、不创建盘符；盘符必须由用户手动点击连接后出现。

---

# 19. 日志

必须设计统一日志系统。

日志必须记录：

```text
设备发现
认证
挂载
卸载
rclone启动
rclone退出
WinFsp状态
网络变化
错误
重连
```

禁止记录：

```text
密码
Token
完整认证Header
私钥
用户文件内容
```

提供：

```text
导出诊断包
```

方便以后排查。

---

# 20. 安装程序

正式目标：

用户只下载：

```text
PhoneBridgeSetup.exe
```

安装程序负责：

```text
Windows客户端
rclone
WinFsp
必要运行库
```

不要要求普通用户分别下载四五个依赖。

如果 WinFsp 因许可证/安装机制不能静默打包：

必须实现：

```text
自动检测
↓
引导官方下载/安装
```

不要偷偷下载未知二进制文件。

---

# 21. Android APK

Android 最终输出：

```text
PhoneBridge.apk
```

第一阶段：

GitHub Release APK。

后续再考虑：

Google Play / 其他分发。

---

# 22. 自动更新

MVP 后实现。

Windows：

```text
检查 GitHub Release
↓
发现新版
↓
提示用户
↓
用户确认更新
```

Android：

同样。

第一版不得为了自动更新引入复杂后台服务器。

---

# 23. GPL要求

上游当前为 GPLv3。

因此：

如果直接修改并对外分发基于该代码的版本：

必须遵守 GPLv3。

如果未来需要：

> 闭源商业化

在做任何商业闭源决定之前：

必须重新进行许可证评估。

不要自行删除：

* LICENSE
* Copyright
* 原作者声明

---

# 24. 项目目录

Codex 首先创建：

```text
PhoneBridge-NG/
│
├─ AGENTS.md
├─ README.md
├─ ARCHITECTURE.md
├─ DEVELOPMENT_RULES.md
├─ DECISIONS.md
│
├─ docs/
│   ├─ PRODUCT.md
│   ├─ SECURITY.md
│   ├─ PROTOCOL.md
│   ├─ TESTING.md
│   └─ tasks/
│       ├─ active/
│       └─ completed/
│
├─ android/
│
├─ windows/
│
├─ scripts/
│
└─ tests/
```

---

# 25. 文件职责

## AGENTS.md

本文件。

Codex 每次工作前必须阅读。

---

## PRODUCT.md

记录：

```text
用户是谁
解决什么问题
产品目标
非目标
MVP
验收标准
```

---

## ARCHITECTURE.md

记录：

```text
Android架构
Windows架构
WebDAV
TLS
mDNS
rclone
WinFsp
配对
缓存
```

任何重大架构调整必须先更新：

`ARCHITECTURE.md`

再修改代码。

---

## DECISIONS.md

记录重大技术决策，例如：

```text
ADR-001 保留WebDAV
ADR-002 使用rclone
ADR-003 使用WinFsp
ADR-004 Windows改用C#
```

以后不得反复重新讨论已经决定的问题，除非出现新的事实证据。

---

# 26. Codex 工作规则

非常重要。

## 禁止一次完成整个项目

每次只做：

> 一个可验证任务。

---

## 每个任务格式

```text
目标

范围

不做什么

涉及文件

实现

测试

验收结果
```

---

## 开发完成后

必须：

1. 运行相关测试
2. 更新任务文档
3. 更新 DECISIONS（如果产生架构决定）
4. 给出改动摘要
5. 给出已验证内容
6. 明确未验证内容

---

# 27. 禁止 Codex 的行为

禁止：

### 1

未经批准大规模重构已经正常工作的模块。

### 2

因为一个错误反复：

```text
换库
换协议
换框架
```

### 3

在没有真实证据时声称：

```text
已解决
稳定
生产可用
性能很好
```

### 4

连续修改多个可能根因。

出现问题：

必须先：

```text
复现
↓
日志
↓
确认根因
↓
修改
↓
验证
```

### 5

为了“以后扩展”提前实现大量复杂抽象。

### 6

加入用户没有要求的功能。

---

# 28. 测试设备

主要目标设备：

```text
Windows 11 PC
+
Samsung Galaxy S23 Ultra
```

但代码不能写死三星型号。

Android最低版本在 Phase 0 调研后确定。

优先支持现代 Android。

---

# 29. MVP

第一版只有这些功能：

### Android

* 启动文件共享
* 停止文件共享
* 文件访问权限
* HTTPS
* WebDAV
* mDNS
* 设备名称
* 配对
* 前台服务
* 中文

### Windows

* 自动发现手机
* 配对
* 保存安全凭据
* 调用 rclone
* 调用 WinFsp
* 用户手动连接后挂载盘符
* 自动卸载
* 自动重连
* 打开 Explorer
* 中文
* 托盘
* 日志

### Explorer

必须完成：

```text
浏览目录
打开文件
手机→PC复制
PC→手机复制
新建文件夹
重命名
删除
```

---

# 30. MVP 验收标准

只有全部通过，才允许称：

> MVP 完成。

### Case 1

Windows 和手机同 Wi-Fi：

30 秒以内发现设备。

### Case 2

第一次完成配对。

### Case 3

重新启动 Windows：

如果用户已启用 Windows 自启动，只启动唯一客户端并进入托盘；不自动连接、不启动 rclone、不创建盘符。用户打开客户端并手动点击连接后，盘符正常出现。

### Case 4

重新启动 Android：

自动恢复。

### Case 5

打开：

```text
DCIM
Download
Pictures
```

成功。

### Case 6

复制：

```text
100 MB
1 GB
5 GB
```

成功。

### Case 7

PC 上传文件到手机成功。

### Case 8

重命名成功。

### Case 9

创建目录成功。

### Case 10

删除成功。

### Case 11

复制过程中 Wi-Fi 中断：

不能造成 Windows 客户端崩溃。

### Case 12

恢复网络：

能够重新连接。

### Case 13

手机 IP 改变：

仍可通过 mDNS 找回。

### Case 14

Windows睡眠 → 唤醒：

连接能够恢复。

### Case 15

打开含大量照片目录：

UI 不得长时间假死。

---

# 31. 第一阶段不要做缩略图优化

先完成：

> 文件系统稳定性。

顺序：

```text
连接
↓
认证
↓
挂载
↓
读取
↓
写入
↓
重连
↓
安全
↓
性能
↓
缩略图
```

不能反过来。

---

# 32. Codex 第一个任务

不要立即开发新 UI。

先执行：

## Phase 0 — Upstream Audit

### 任务

完整分析上游：

`ysachin26/PhoneBridge`

确认：

1. LICENSE
2. Android模块
3. Windows模块
4. WebDAV实现
5. TLS实现
6. mDNS实现
7. rclone调用方式
8. WinFsp调用方式
9. 配置保存方式
10. 已完成能力
11. 未完成能力
12. 明显Bug
13. 安全风险
14. 可以复用的代码
15. 建议重写的代码

然后生成：

```text
docs/UPSTREAM_AUDIT.md
```

---

# 33. Phase 0 第二个任务

建立项目结构：

```text
AGENTS.md
README.md
ARCHITECTURE.md
DEVELOPMENT_RULES.md
DECISIONS.md
docs/
```

这一阶段：

**不要修改核心功能。**

---

# 34. Phase 0 第三个任务

在开发机上验证原始技术链路：

```text
Android
↓
WebDAV
↓
rclone
↓
WinFsp
↓
Explorer
```

至少证明：

```text
能挂载
能读
能写
能复制
```

没有验证以前：

禁止开始大规模重构。

---

# 35. Phase 1

只有 Phase 0 验证通过以后：

开始开发新版 Windows C# 客户端。

优先顺序：

```text
Device Discovery
↓
Device Model
↓
Mount Manager
↓
rclone integration
↓
WinFsp integration
↓
Basic UI
↓
Tray
↓
Logging
```

---

# 36. 核心开发原则

这个项目不是追求代码最先进。

目标只有一个：

> Windows 用户打开“此电脑”，手机就在那里，并且稳定地像一个网络硬盘一样使用。

如果一个技术方案：

```text
更先进
但
更复杂 / 更不稳定
```

不要采用。

优先：

```text
成熟
可验证
可维护
低依赖
```

---

# 37. Codex 每次回复格式

每个任务结束只报告：

## 已完成

具体改了什么。

## 已验证

真实运行过什么测试。

## 未验证

哪些只是代码完成但没有实机测试。

## 已知问题

当前存在什么问题。

## 下一步

只给一个最合理的下一任务。

不要一次给十几个后续方向。
