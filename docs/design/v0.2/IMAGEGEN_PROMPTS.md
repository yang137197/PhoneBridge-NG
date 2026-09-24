# P2-002 视觉方向稿生成记录

两个 PNG 方向稿均使用 Codex 内置图像生成工具生成，随后复制到本目录并以原始分辨率人工复核。它们只用于视觉气质，不覆盖 SVG 线框、控件状态、视觉变量和文案表。

## Windows

输出：`windows-visual-concept-r1.png`

```text
Use case: ui-mockup
Asset type: PhoneBridge NG v0.2 Windows desktop app visual concept board
Primary request: create a polished, implementation-realistic WPF desktop app concept for a Chinese-first Android-to-Windows wireless file access utility. Show the complete main Devices screen at 1440x900-style desktop proportions, plus a narrow secondary strip with the five device-card states.
Scene/backdrop: flat app screenshot only, no device mockup, no surrounding desk.
Information architecture: a compact left sidebar with exactly three entries: 设备, 设置, 关于. Main header: 我的手机. One primary action: 添加手机. The main canvas contains two spacious white device cards, one connected Samsung Galaxy S23 Ultra on P: in 安全模式 with primary button 打开文件 and secondary action 断开, and one paired-but-disconnected Redmi K40 with primary button 连接. Include a subtle top-level status summary, but no fake metrics.
Five state strip: 未配对 with 配对; 未连接 with 连接; 处理中 with inline progress and 取消; 已连接 with 打开文件 plus 断开; 需要处理 with 继续连接 plus 查看原因.
Style/medium: refined modern Windows 11 app UI, crisp code-native controls, clean linear icons, flat vector-like interface, airy but not sparse, restrained technology feel.
Color palette: very light cool gray #F4F7FB background, pure white surfaces, blue #1769E0 primary, cyan #16B8C4 accent only for connectivity, ink #172033, muted #5E6B7A, green/amber/red semantic states.
Typography: Segoe UI / Microsoft YaHei UI character, clear hierarchy, no oversized marketing headline.
Geometry: 12 px card radius, 8 px control radius, subtle 1 px borders, soft restrained shadow, 8-point spacing rhythm.
Constraints: do not invent cloud accounts, sync, notifications, charts, statistics, theme selector, search box, hero marketing copy, or extra navigation. No gradients, no dark theme, no glassmorphism, no nested card grids, no logos copied from Microsoft or Samsung. Keep all controls plausible for WPF. Chinese text must be legible and use the exact labels where possible. This is a design-direction reference; prioritize hierarchy and component anatomy over decorative illustration.
Avoid: blurry text, repeated cards, excessive pills, fake telemetry, 3D devices, photographs, watermarks.
```

## Android

输出：`android-visual-concept-r1.png`

```text
Use case: ui-mockup
Asset type: PhoneBridge NG v0.2 Android app visual concept board
Primary request: create a polished, implementation-realistic native Android concept for a Chinese-first local wireless file sharing app. Show one complete phone home screen plus three adjacent compact state/detail screens: pairing session, paired computer details, and settings.
Scene/backdrop: flat app screens only on a neutral presentation board, no hand, no phone photography.
Home screen information: top app title PhoneBridge NG and a small settings icon; a calm sharing status area showing “正在共享”; current shared folder card showing “DCIM”; one prominent primary button “停止共享”; a secondary action “配对新电脑”; paired computers section with one card “DESKTOP-YANG” and “安全模式”. If sharing is stopped, the corresponding primary label is “开始共享”. Permission card appears only as a conditional example, not permanently.
Pairing session: exact 8-digit code “4827 1953”, countdown text, waiting status, and explicit note that approval happens on the phone.
Computer details: device name, access mode choices “只读 / 安全模式 / 完全读写”, explanation, and destructive “撤销访问” separated at the bottom.
Settings: language options “简体中文 / English” only; no theme selector.
Style/medium: native Android interface with Material-inspired restraint but original visual language, crisp code-native controls, flat vector-like UI, clean linear icons, clear one-handed hierarchy.
Color palette: very light cool gray #F4F7FB background, pure white cards, blue #1769E0 primary, cyan #16B8C4 connectivity accent, ink #172033, muted #5E6B7A, green/amber/red semantic states.
Typography: modern Android sans/Chinese system font, clear readable hierarchy, no marketing headline.
Geometry: 16 px card radius, 12 px button radius, 8-point spacing rhythm, subtle 1 px borders, very restrained shadows.
Constraints: do not invent cloud accounts, remote access, sync, notifications history, analytics, QR scanning, theme selector, file browser, charts, or extra tabs. No gradients, no dark theme, no glassmorphism, no copied brand assets. Chinese text must be legible and use the exact labels where possible. Keep all controls feasible in Kotlin native Android.
Avoid: blurry text, overly dense forms, bottom navigation, floating action button, excessive pills, fake telemetry, 3D illustrations, photographs, watermarks.
```

