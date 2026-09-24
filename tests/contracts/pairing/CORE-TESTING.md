# P1-004 核心协议复验

测试使用合成码、合成公有 CA 和两个短命管道进程，不连接手机/网络、不开盘符、不安装应用。测试宿主仅接受三个固定合成码，严禁用于实际配对。

从项目根，使用已校验的 `.audit/tools/dotnet-10.0.401/dotnet.exe`：

```text
dotnet restore windows/PhoneBridge.Windows.slnx --locked-mode
dotnet build windows/PhoneBridge.Windows.slnx -c Release --no-restore
dotnet test windows/PhoneBridge.Windows.slnx -c Release --no-build --no-restore --logger trx
```

沿用项目 `.audit/dotnet-home` / `.audit/nuget-packages`，设置 DOTNET_CLI_TELEMETRY_OPTOUT=1、TESTINGPLATFORM_TELEMETRY_OPTOUT=1。上面的 dotnet 指代固定绝对工具路径，不依赖 PATH 中可能不同的 SDK。

设置 JAVA_HOME 为已校验的 JDK 21 目录、GRADLE_USER_HOME 为项目 `.audit/gradle-user`，使用 `.audit/tools/gradle-9.3.1/bin/gradle.bat`：

```text
gradle -p android/pairing-core test jar writeTestClasspath --console=plain
```

最后用 `.audit/venv/Scripts/python.exe` 执行 `tests/contracts/pairing/run_core_interop.py`。脚本读取构建生成的 test-classpath.txt，依次传递完整 8 帧、在接收端逐字节分片，检查最终 grant/CA/标识/端口一致、退出码与无强杀。结果写 `.audit/p1-004/core-interop-results.json`；公开合成 transcript 单独保存，无原始 grant/PAKE 私钥。

59 项管道场景含 4 次正确/前导零、错误码、每帧 magic/type/长度/尾随/截断、Hello 身份绑定、反射、跨会话重放、元素/标量范围与证明/MAC 改动。MSTest 与 JUnit 补充状态顺序、重复取结果、取消/关闭和 HKDF 已知答案。对应 [产品验收清单](contract.json)仍不能标为真机/网络授权通过。

校验文件、锁文件和构建脚本的修改需核对官方来源；Gradle 首次生成 checksum 不是独立签名验证。完整范围和失败记录见 [P1-004 验收](../../../docs/audit/P1-004-VALIDATION.md)。
