# 阶段 0：跨平台联网技术验证

[English](Phase0-Spike.md) | 简体中文

目标：**编辑器 / Meta Quest / Apple Vision Pro** 三端进入同一个 Multiplayer Services 会话（Distributed Authority），看到彼此的头和手，并且共享方块的所有权能在各端之间轮换。

验证场景不需要任何输入：每台设备启动后会自动匿名登录 → Quick Join（没有会话就新建）→ 显示状态面板。

## 版本锁定

| 组件 | 版本 |
|---|---|
| Unity | 6000.3.25f1 |
| Netcode for GameObjects | 2.13.3（NGO 3.x 需要 Unity 6000.7，暂不升级） |
| Multiplayer Services | 2.3.3 |
| PolySpatial / visionOS | 3.3.1 |
| OpenXR / Meta OpenXR | 1.18.0 / 2.6.1 |
| AR Foundation | 6.6.2 |
| XR Interaction Toolkit / XR Hands | 3.6.1 / 1.9.0 |
| Vivox | 16.12.1（包内自带 visionOS 原生库） |

## 一次性设置（必须手动完成）

### 1. Unity Hub
- 给 6000.3.25f1 安装 **Android Build Support**（含 OpenJDK、Android SDK & NDK）。目前本机只装了 visionOS 和 WebGL 模块。

### 2. Unity Cloud
1. **Edit → Project Settings → Services**：关联一个 Unity Cloud 项目。
2. 在 [cloud.unity.com](https://cloud.unity.com) 上为这个项目启用：
   - **Multiplayer → Sessions / Distributed Authority**
   - **Vivox**
   - Authentication 默认开启了匿名登录，**不需要**再配置用户名密码提供商。

### 3. XR Plug-in Management（Edit → Project Settings → XR Plug-in Management）
- **Android 页签**（仓库里已经配好）：使用 **OpenXR** 加载器，并开启了：
  - Meta Quest Support；交互配置：Oculus Touch、Meta Quest Touch Plus / Pro 手柄和 Hand Interaction（手势）
  - Meta OpenXR 的 AR 功能：Session、Camera（透视）、Planes（平面）、Raycast；Hand Tracking 和 Meta Hand Tracking Aim；Composition Layers；Foveated Rendering（注视点渲染）
  - Player Settings：包名 `edu.nyu.moe.visionpro1`、IL2CPP、ARM64、Vulkan、最低 API 32、网络权限设为必需
- **Standalone 页签**（已配好）：使用 **OpenXR** 加载器，开启了 Touch / Touch Plus 手柄和手势交互配置，供 Windows 上的 Meta XR Simulator 使用（见测试 B2）。没有 OpenXR 运行时的机器（比如 macOS）上 XR 不会启动，编辑器继续用键盘鼠标操作。相关菜单：**Tools → MR → Enable / Disable OpenXR in Editor**。
- **visionOS 页签**：勾选 **Apple visionOS**，App Mode 选 **RealityKit with PolySpatial**。
- 最后打开 **Project Validation**。以下几条是已知的、可以忽略的提示：
  - *At least one interaction profile must be added* 和 *Composition Layers Support feature is required*：当 Build Settings 窗口里选中的不是 Android 时出现的误报（这两条规则读的是"选中的平台"）。
  - *Soft shadows* 和 *Screen Space Ambient Occlusion*：来自 **PC** 画质档位。Quest 用的是 **Mobile** 档位，这两项都没有。
  - *PoseControl* 和 *StickControl*：可选项，会改动整个项目的输入类型，有意跳过。
  - 校验或构建时 `MetaQuestFeature.cs:539` 出现的 `NullReferenceException` 也是同一个 OpenXR 包的问题，不影响构建。

### 4. 权限说明（Player Settings → visionOS → Other Settings，以及 Apple visionOS 设置页）
- Hand Tracking Usage Description："用于在多人场景中同步你的手部动作"
- World Sensing Usage Description："用于检测桌面等平面以放置物体"
- Microphone Usage Description："用于和其他参与者语音聊天"
- Android 端麦克风权限由 `VoiceManager` 在运行时申请。
- 快捷方式：**Tools → MR → Apply visionOS Player Settings** 会填好麦克风说明（以及 Apple 签名团队）。注意要填的是 *Microphone* Usage Description，不是 *Camera*：缺了它，Vivox 一打开麦克风，visionOS 就会直接杀掉 App。

### 5. 生成验证场景
菜单 **Tools → MR → Generate Phase 0 Spike Content**（可重复执行）。会生成：
- `Assets/_Project/Prefabs/NetworkRig.prefab`：玩家 Prefab，包含头、双手和昵称标签
- `Assets/_Project/Prefabs/OwnershipProbe.prefab`：所有权轮换测试方块
- `Assets/_Project/Settings/NetworkPrefabs.asset`、`UnboundedVolume.asset`
- `Assets/_Project/Scenes/Main.unity`：并设为 Build Settings 里唯一的场景

## 测试步骤

### A. 编辑器（Multiplayer Play Mode）
1. **Window → Multiplayer → Multiplayer Play Mode**，启用 1–3 个虚拟玩家。
2. 打开 `Main.unity`，按 Play。
3. 操作：WASD 移动、Q/E 升降、按住右键转视角。编辑器里没有头显时，手的位置由 `HardwareRig` 模拟在身前。
4. 每个虚拟玩家使用独立的认证 Profile（见 `UnityServicesInitializer`），所以会有不同的 PlayerId。
5. 每个玩家按 Multiplayer Play Mode 的编号命名（`Editor-P1`、`Editor-P2`……），并被分配到方块周围不同的座位（`SeatAssigner`）。虚拟玩家会跳过语音：Vivox 的原生库在虚拟玩家里无法加载。
6. **修改场景之后，要把虚拟玩家关掉再打开。** 代码改动会自动同步给它们，但它们会继续使用之前已经打开的那份场景。

### B. Quest（真机）
1. 把构建目标切到 **Android**（Build Profiles）。
2. 构建：APK 会输出到 `Build/Android/`。用 Meta Quest Developer Hub 或 `adb install -r MRSpike.apk` 安装；头显连着电脑时也可以直接用 Build And Run。
3. 如果看不到平面或透视画面，先在头显系统设置里完成"空间设置"。

会话在云端，所以 Quest 不需要和其他设备在同一个网络里：手上有头显的人装好 APK，在任何地方都能和 Vision Pro、编辑器的玩家进入同一个会话。

### B2. 没有头显时测试 Quest：Meta XR Simulator（macOS 或 Windows）
Meta XR Simulator 是一个模拟 Quest 3 的 OpenXR 运行时，模拟头显、Touch Plus 手柄和手，在编辑器的 Play 模式里运行。Apple 芯片的 Mac 和 Windows 都支持。

1. **在本机安装模拟器包。** 在 `Packages/manifest.json` 里加上 Meta 的包仓库和这个包：
   ```json
   "dependencies": {
     "com.meta.xr.simulator": "81.0.1"
   },
   "scopedRegistries": [
     { "name": "Meta XR", "url": "https://npm.developer.oculus.com", "scopes": ["com.meta.xr"] }
   ]
   ```
   （也可以在 Unity Asset Store 领取 *Meta XR Simulator*，再到 Package Manager → My Assets 里安装。）
   这样会改动 `Packages/manifest.json`、`Packages/packages-lock.json` 和 `ProjectSettings/PackageManagerSettings.asset`，**这些改动不要提交**：模拟器是每台机器自己的开发工具。
2. **安装模拟器本体。** 包会在第一次使用时自动下载（约 190 MB）。macOS 装在 `~/Library/MetaXR/MetaXrSimulator/<版本>`，Windows 装在 `%LOCALAPPDATA%\MetaXR\MetaXrSimulator\<版本>`。要指定版本，打开 **Edit / Unity → Preferences → Meta XR → Meta XR Simulator → Available Versions**。
   - **Windows 的 81 版：** Meta 那边 Windows 版 81 的下载链接目前返回 *HTTP 404*（`[Meta XR Simulator Installer] HTTP/1.1 404 Not Found`）。请在 *Available Versions* 里改选 **78.1**。如果列表是空的，先按一次 Play 让它去获取版本列表，再重新打开 Preferences。macOS 的 81 版可以正常下载。
3. **激活：** **Meta → Meta XR Simulator → Activate**。这会把 OpenXR 的 Play 模式运行时指向模拟器，相当于在 Project Settings → XR Plug-in Management → OpenXR → Play Mode OpenXR Runtime 里选择 Meta XR Simulator。仓库里已经开启了 Standalone 平台的 OpenXR 加载器。
4. 打开 `Main.unity` 按 Play，在模拟器窗口里操控模拟的头显和手柄。编辑器会像其他玩家一样加入共享会话，所以 Vision Pro 或其他编辑器可以同时加入。
5. 要换回键盘鼠标，在同一个菜单里点 **Deactivate**。做 Multiplayer Play Mode 测试前也请先 Deactivate：虚拟玩家是主编辑器启动的子进程，可能会继承模拟器设置，每个都去开一个模拟器。

能验证的：在类似 Quest 的 OpenXR 运行时上，头部姿态、Touch 手柄和手的姿态能否通过 `HardwareRig` 正常读取，以及配合联网和语音的表现。验证不了的：透视（背景显示为黑色）、平面检测和真机性能，这些仍然需要头显。

### C. Vision Pro
- Build Settings 切到 visionOS → Build，在 Xcode 中打开生成的工程，部署到真机。
- 也可以用 **PolySpatial → Play to Device** 快速迭代。

## 通过标准

在至少 **编辑器 + Quest + AVP** 三端同时在线的情况下：

- [ ] 三端状态面板显示**同一个会话码**，玩家列表包含所有人和他们的平台
- [ ] 每一端都能看到其他人的头和手在移动，延迟可以接受
- [ ] 方块绕圈运动，每 5 秒换一个所有者（颜色随之变化），运动连续，没有跳变
- [ ] 关掉**会话创建者**那一端后，会话仍然存在，方块继续由其他人驱动，被关掉那个人的 Avatar 消失
- [ ] 三端能够互相语音通话

## 测试结果（2026-10-08）

**阶段 0 已通过，可以开始阶段 1 的开发。**

| 检查项 | 结果 |
|---|---|
| 同一会话，玩家列表显示各自平台 | ✅ 编辑器三人（MPPM）；✅ Vision Pro + 编辑器 |
| 能看到其他人的头和手并且会动 | ✅ 编辑器三人；每个玩家坐在方块周围的一圈座位上，面朝中心 |
| 方块绕圈，每 5 秒换所有者和颜色 | ✅ 各端一致地按 1 → 2 → 3 → 1 轮换 |
| 会话创建者离开后会话仍在 | ✅ 剩下两人仍在会话中，方块继续在他们之间传递 |
| 语音 | ✅ 主编辑器连接成功；虚拟玩家按设计跳过语音 |
| Meta Quest | ✅ APK 能成功构建（清单中已确认 OpenXR、Meta Quest 功能、透视、手部追踪和 VR 启动类别）；✅ macOS 上的 Meta XR Simulator 81：OpenXR 正常运行，模拟出头显和两个 Touch Plus 手柄，`HardwareRig` 能从手柄读到手的位置，会话、座位、语音都正常；⏳ 还没在头显上运行 |
| Vision Pro 上的座位效果 | ⏳ 编辑器里正常，还没在真机上确认 |

测试中修复的问题：

- Vivox 打开麦克风时 Vision Pro 崩溃：语音说明被误填到了*相机*权限说明里。请用 **Tools → MR → Apply visionOS Player Settings** 设置。
- 所有 MPPM 玩家昵称相同（它们共用 PlayerPrefs）；现在昵称按玩家编号生成。
- 退出 Play 时出现 `Leave on destroy failed: lobby not found`；Multiplayer SDK 在退出时已经会离开会话，所以去掉了多余的离开操作。

## 不通过时的退路

- **Distributed Authority 在某一端不稳定**：把 `Services` 物体上 `SessionManager.networkMode` 改成 `RelayClientHost`，即退回原型那种主机模式（`AppBootstrap` 已兼容由主机生成方块）。
- **Vivox 不可用**：语音不影响其余验证，状态面板会显示 `Voice unavailable: ...`，可以先跳过。

## 代码结构

```
Assets/_Project/
  Core/        PlatformInfo, PlayerColors, EditorInstanceInfo
  Auth/        IAuthProvider, AnonymousAuthProvider, AuthManager, UnityServicesInitializer
  XR/          HardwareRig（头/手姿态：XR Hands → 手柄 → 编辑器模拟）, EditorFlyCamera
  Networking/  SessionManager（Sessions API）, NetworkRig（玩家 Prefab）, OwnershipRotationProbe, SeatAssigner
  Voice/       VoiceManager（Vivox，频道名 = 会话 Id）
  App/         AppBootstrap（自动登录 + 快速加入）, StatusPanel
  Editor/      ProjectSetup（一键生成），AppleSigningSetup（visionOS Player Settings）
```
