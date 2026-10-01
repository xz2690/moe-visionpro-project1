# 跨平台 MR 多人项目计划（Apple Vision Pro + Meta Quest）

[English](Plan.md) | 简体中文

## Context

`xz2690/Lobby-System` 是一个 Unity 6 + Netcode for GameObjects (NGO) + Relay + Unity Auth 的桌面多人大厅原型，用来验证联网方案。现在要以它为联网基础，做一个 **Vision Pro 与 Meta Quest 跨平台同场的混合现实（MR）多人应用**，功能参考 Photon 的 *Fusion Cross-platform Mixed Reality* 示例：

- AVP 端可在 Unbounded（沉浸空间，带头手追踪）和 Bounded（体积窗口，从上方俯瞰全场）之间切换；Quest 端为透视 MR
- 头 + 双手 + 手指骨骼同步（高压缩）
- 共享物体抓取（直接捏合全平台，visionOS 额外支持注视+间接捏合）
- 看手掌弹出手部菜单；积木带磁吸，可吸附本地检测到的真实平面（平面不同步）
- 语音聊天

已确认的决策：
- AVP 用 **PolySpatial（RealityKit MR）**；团队有 Unity Pro、Apple Silicon Mac、AVP、Quest 设备
- 登录用 **Unity Auth 匿名 + 昵称**
- 联网继续用 **Unity 栈（NGO）**，不换 Photon；把 Photon 示例的模块逐一映射到 Unity 对应方案

## 技术栈映射（Photon 示例 → 本项目）

| Photon 示例 | 本项目 |
|---|---|
| Fusion 2 Shared Mode | NGO 2.x **Distributed Authority**，经 `com.unity.services.multiplayer` Sessions 接入（无主机，任何人离开会话都继续；与 Shared Mode 最接近） |
| ConnectionManager addon | `SessionManager`（Create / QuickJoin / JoinByCode，取代原型的 Relay 手写 + 自建房间系统） |
| XRShared（硬件 rig / 网络 rig 分离、抓取） | XR Interaction Toolkit 3.x + 自写 `NetworkRig` / `NetworkGrabbable` |
| XRHands synchronization | XR Hands 1.x + 自写量化压缩的关节同步 |
| ExtendedRigSelection | `RigSelector`：Quest MR rig / AVP Unbounded rig / AVP Bounded rig |
| visionOS helpers | PolySpatial 3.x + `com.unity.xr.visionos` + XRI visionOS 输入（SpatialPointer） + 自写 LineRenderer 替代等 |
| Photon Voice | Vivox（`com.unity.services.vivox`），按 Session Id 建频道 |
| Magnets | 自写 `Magnet` / `MagnetAttractor` |
| 平面检测 | AR Foundation 6.x `ARPlaneManager`（Quest：Meta OpenXR；AVP：visionOS ARKit），仅本地 |

渲染：URP（保留），所有材质只用 URP Lit/Unlit 或 Shader Graph（PolySpatial 不支持手写 HLSL、后处理）。

## 原型代码的处置

> 下面提到的原型文件不在本仓库中，可在原型仓库 [xz2690/Lobby-System](https://github.com/xz2690/Lobby-System) 中查看。

- **复用模式**：`IAuthProvider` 接口（`AuthModels.cs`）、单例 + `DontDestroyOnLoad` 启动方式（`LobbyBootstrap.cs`）、编辑器一键生成（`LobbySetup.cs`）、`UnityServicesAuthProvider` 的初始化/错误映射逻辑。
- **替换**：`NetworkLobbyManager` 的 Relay 手动分配 → Sessions API；`LobbyManager` 的自建房间/准备/聊天 → Session 即房间，聊天由语音取代；`ClientNetworkTransform` → `NetworkTransform` 的 Owner 授权模式；连接载荷里的自生成 Token 删除，身份直接用 `AuthenticationService.PlayerId` + 会话玩家属性中的昵称。
- **删除**：用户名密码 UI、`LocalAuthProvider`、屏幕空间 Canvas、WASD `PlayerController`、`.plastic/`、`ignore.conf`、`TutorialInfo/`。
- 上次审查出的 5 个 Bug（幽灵玩家、全会话切场景、重复入房、跨房间踢人、聊天伪造）都在被替换的代码里，随重构一起消失，不单独修。

## 项目结构

在新分支（或新仓库，从本仓库拷贝 `ProjectSettings` / `Packages` 起步）中：

```
Assets/_Project/
  Core/          Bootstrap, 平台检测, 服务初始化               (asmdef: Core)
  Auth/          AnonymousAuthProvider, 昵称存储                (asmdef: Auth)
  Networking/    SessionManager, NetworkRig, HandSync, NetworkGrabbable (asmdef: Networking)
  XR/            RigSelector, HardwareRig 各变体, 手势检测, HandMenu  (asmdef: XR)
  Interaction/   Magnet, 积木, 平面吸附
  Voice/         VoiceManager (Vivox)
  UI/            世界空间连接面板, 手部菜单 UI
  Platform/visionOS/  VolumeCamera 切换, LineRenderer 替代, 间接捏合适配 (#if UNITY_VISIONOS)
  Platform/Quest/     Passthrough / 房间设置提示 (#if UNITY_ANDROID)
  Scenes/        Bootstrap.unity, Main.unity
```

XR Plug-in Management：Android → OpenXR（Meta Quest 特性组 + Meta OpenXR AR 特性）；visionOS → Apple visionOS，App Mode = RealityKit with PolySpatial。

## 分阶段计划

### 阶段 0：技术验证（最高风险，先做）
目标：在三端（编辑器 / Quest 3 / AVP）都跑通"进同一个会话并同步一个方块"。
1. 确认 Unity 6.3 与 PolySpatial 3.x、visionOS 插件、Meta OpenXR、NGO、Multiplayer Services 的兼容组合，锁定版本。
2. 最小场景：`SessionManager` 用 `CreateOrJoinSessionAsync` + `WithDistributedAuthorityNetwork()`，生成一个 Owner 授权的 NetworkObject 方块。
3. 分别打 Quest（Android/APK）和 AVP（visionOS/Xcode）包，三端互连。
4. 验证 Vivox 能否在 visionOS 上编译和通话。
5. **出口条件**：DA 在三端稳定；如果 DA 在 visionOS 上有问题，退回 Relay 客户端-主机模式（复用原型的 `StartHostRelay`/`JoinRelay` 思路，但通过 Sessions 的 `WithRelayNetwork()`）。如果 Vivox 不支持 visionOS，语音降级为可选或改用其他方案。

### 阶段 1：项目骨架与会话流程
- 按上面的结构重组仓库，加 asmdef，清理原型和模板残留。
- `AnonymousAuthProvider`：`SignInAnonymouslyAsync` + `UpdatePlayerNameAsync`，昵称存 PlayerPrefs。
- `SessionManager`：Create（返回短码）/ QuickJoin / JoinByCode / Leave，玩家属性带昵称和平台/模式。
- 世界空间连接面板：昵称输入（Quest 用系统键盘，AVP 用 visionOS 系统键盘）、创建、加入码、快速加入。
- 单一 Main 场景（MR 下不需要大厅场景切换），连接后在本地生成网络 rig。

### 阶段 2：Rig 选择与 Avatar 同步
- 硬件 rig 和网络 rig 分离：本地 `HardwareRig` 采集 头 / 左手 / 右手 / 手指；`NetworkRig`（玩家 Prefab）每帧从本地硬件 rig 拷贝，用 Owner 授权的 `NetworkTransform` 同步头和手腕。
- `RigSelector` 三种配置：
  - Quest MR：XR Origin + AR Camera（透视）+ 手柄和手势追踪
  - AVP Unbounded：VolumeCamera Unbounded + ARKit 头/手追踪（XR Hands）+ SpatialPointer
  - AVP Bounded：VolumeCamera Bounded，没有头手追踪；把共享场景缩放到体积窗口内俯瞰；在网络上广播模式，其他人看到时隐藏该玩家的身体
- `HandSync`：26 关节旋转用 smallest-three 量化（每个约 4 字节）+ 手腕位置，20–30 Hz，用自定义 `INetworkSerializable` + 非可靠 RPC（或 NetworkVariable）发送；接收端做插值。Quest 用手柄时发送预设手势姿态。
- 头像外观：简单的头部 + 手部模型，昵称标签，按 PlayerId 哈希分配颜色（沿用原型 `PlayerController` 的调色板思路）。

### 阶段 3：交互
- `NetworkGrabbable`：XRI `XRGrabInteractable` 的 select 事件 → 请求所有权（DA 的 `NetworkObject` 所有权标志设为可转移 / 可请求），非 Owner 端设为 kinematic；松手后由 Owner 做物理模拟。
- 输入：全平台直接捏合（XR Hands + XRI Poke/Direct interactor）；visionOS 用 SpatialPointer 做注视 + 间接捏合；Quest 手柄用 Ray / Direct interactor。
- 手部菜单：手掌朝向头部时显示积木菜单（XR Hands 手势检测），从菜单拿起时用 `NetworkObject.Spawn` 生成积木。
- 磁吸：积木顶部和侧面有吸附点，松手时吸附到最近的点；本地 AR 平面加碰撞体，也作为吸附目标（平面本身不同步，只同步积木的最终位置）。

### 阶段 4：语音
- `VoiceManager`：登录 Vivox → 加入以 Session Id 命名的频道（Unbounded / Quest 用户用位置频道，Bounded 用户用普通频道）；处理麦克风权限（Android 清单、visionOS Info.plist）；静音按钮放在手部菜单里。

### 阶段 5：平台打磨与性能
- visionOS：所有材质改用 Shader Graph / URP 标准着色器，用 PolySpatial 支持的方案替代 LineRenderer，配置 Info.plist 的手部追踪/世界感知权限说明，Bounded↔Unbounded 运行时切换。
- Quest：URP 移动端设置、注视点渲染、MSAA 4x、Single Pass Instanced；未完成房间设置时显示提示。
- 网络：调同步频率和插值；断线重连（Sessions 重新加入）。

## 关键文件（新）

- `Networking/SessionManager.cs`：替代 `NetworkLobbyManager.cs`
- `Auth/AnonymousAuthProvider.cs`：实现现有 `IAuthProvider`
- `Networking/NetworkRig.cs`、`Networking/HandSync.cs`、`Networking/HandPoseSerializer.cs`
- `XR/RigSelector.cs`、`XR/HardwareRig*.cs`、`XR/HandMenu.cs`
- `Networking/NetworkGrabbable.cs`、`Interaction/Magnet*.cs`
- `Voice/VoiceManager.cs`
- `Platform/visionOS/VolumeModeSwitcher.cs`
- `Editor/ProjectSetup.cs`：沿用 `LobbySetup` 的一键生成思路，生成 Prefab、注册 NetworkPrefabs

## 验证方式

1. **编辑器**：Multiplayer Play Mode 开 2–4 个虚拟玩家 + XR Interaction Simulator，测会话、头手同步、抓取所有权转移。
2. **AVP**：PolySpatial Play to Device 快速迭代；在 Xcode visionOS 模拟器测 Bounded 模式；真机测 Unbounded 手部追踪和间接捏合。
3. **Quest**：Quest Link 调试 + APK 真机测透视、平面检测、手柄和手势切换。
4. **跨平台验收清单**（每个里程碑都跑一遍）：
   - Quest + AVP Unbounded + 编辑器进同一个会话，能看到彼此的头、手、手指
   - 任一方（包括会话创建者）退出后，会话继续，其他人看不到残留 Avatar
   - A 抓起积木交给 B，没有抖动或回弹；磁吸结果三端一致
   - AVP 切到 Bounded 后，其他人看不到它的身体；它能俯瞰全场
   - 语音双向互通，位置衰减正确
5. **性能**：Quest 稳定 72/90 fps；AVP 用 RealityKit Trace 检查；记录每个玩家的上行带宽（目标 < 30 KB/s）。

## 风险

- NGO Distributed Authority 和 Vivox 在 visionOS 上的支持程度 → 阶段 0 验证，有退路方案。
- PolySpatial 的渲染和组件限制（着色器、粒子、LineRenderer、后处理）→ 从第一天起只用兼容的资源。
- 两台设备在物理上**不共享空间**（和参考示例一样，不做同空间定位对齐），每个人在自己房间里看到其他人的 Avatar；如果之后需要同空间协作，再单独规划空间锚点对齐。
