# Cross-Platform MR Multiplayer (Apple Vision Pro + Meta Quest)

[English](README.md) | 简体中文

Unity 6.3 混合现实多人项目：Vision Pro（PolySpatial / RealityKit）和 Meta Quest（OpenXR 透视）的用户进入同一个共享会话，同步头、手和共享物体。

联网基于 Unity 栈：Netcode for GameObjects（Distributed Authority）+ Multiplayer Services Sessions + Vivox。

- 整体计划：[Docs/Plan.zh-CN.md](Docs/Plan.zh-CN.md)
- 阶段 0 的设置、测试方法和结果：[Docs/Phase0-Spike.zh-CN.md](Docs/Phase0-Spike.zh-CN.md)
- 共享房间、桌上物体和跨平台指针输入：[Docs/Room.zh-CN.md](Docs/Room.zh-CN.md)
- 大厅面板、语音指令和字幕：[Docs/Lobby.zh-CN.md](Docs/Lobby.zh-CN.md)

## 当前状态

**阶段 0（跨平台联网技术验证）和阶段 1（大厅与会话流程，含语音指令）已完成，可以开始阶段 2 的开发。**

### 已通过的测试

本地三人测试，使用 Multiplayer Play Mode（主编辑器 + 2 个虚拟玩家）：

- 三人通过 Quick Join 进入同一个会话（Distributed Authority，无主机）
- 每个玩家有独立的 Unity Authentication 身份和不同的昵称（`Editor-P1/P2/P3`）
- 每个玩家的 Avatar（头 + 手 + 昵称标签）都能生成并保持同步
- 每个玩家被分配到共享内容周围的不同座位，面朝中心，一进入就能看到其他人和方块
- 共享方块的所有权在所有客户端之间轮换（1 → 2 → 3 → 1），颜色跟随所有者变化
- **会话创建者离开后会话仍然存在**：创建者离开后，剩下两人仍在会话中，方块继续在他们之间传递
- 主编辑器的 Vivox 语音连接成功；虚拟玩家按设计跳过语音

真机：

- **Apple Vision Pro ↔ 编辑器**：visionOS 版本（RealityKit with PolySpatial）能签名、安装并在 Vision Pro 上运行，并且和 Mac 上的编辑器进入同一个共享会话

没有头显时的 Quest 验证：

- **Android 构建**：APK 能成功打包，清单里有 OpenXR、VR 启动类别、透视和手部追踪
- **Meta XR Simulator（macOS）**：OpenXR 正常运行，模拟出头显和两个 Touch Plus 手柄，手的位置来自手柄，会话、座位、语音都正常

### 尚未验证

- Meta Quest 真机：Android 构建已经配好，APK 也能成功打包，但还没在 Quest 上运行过。没有头显时，可以在 macOS 或 Windows 上用 Meta XR Simulator 测试（见 [Docs/Phase0-Spike.zh-CN.md](Docs/Phase0-Spike.zh-CN.md) 的测试 B2）
- Vision Pro 上的座位效果：XR Origin 和 PolySpatial Volume Camera 一起移动的做法在编辑器里正常，还没在真机上确认

### 共享房间（阶段 0 之后新增）

- 大厅里的 **Enter Room** 按钮（3D 按钮，支持注视 + 捏合、手柄、手势和鼠标）：整个会话一起进入一个 12 × 12 米、中央有桌子的房间
- 桌上三个物体：点一下换颜色，拖动可以移动；谁碰谁拿到所有权，状态同步给所有人
- 已在编辑器里用指针注入钩子验证：打开房间 → 点击 → 拖动。详见 [Docs/Room.zh-CN.md](Docs/Room.zh-CN.md)

### 大厅与语音指令（阶段 1）

- 跟随用户的世界空间大厅面板：进会话前有 Create / Quick Join / Join by code（面板键盘）；进会话后显示会话码、玩家列表（平台、说话中、已静音）、会话聊天和 Enter Room / Mute / Leave。自动 Quick Join 默认关闭（编辑器开关和 `-autojoin` 供测试用）
- 从 VoiceCreator 移植的语音层：Vision Pro 上用设备端 Apple 语音识别和朗读（Swift 桥接），Quest 上用 Vivox 转写，编辑器里用文本替身。面板上的每个操作都是语音指令（"create a session"、"join code F R seven M K M"、"enter the room"、"mute" 等）；其他话作为字幕发给所有玩家
- 已在编辑器验证全部指令，并用 Multiplayer Play Mode 两人验证：加入 / 离开提示、字幕和房间打开都到达另一位玩家。崩溃和强退恢复已在 visionOS 模拟器验证。详见 [Docs/Lobby.zh-CN.md](Docs/Lobby.zh-CN.md)

### 下一步：阶段 2

Rig 选择和 Avatar 完善（Quest MR 透视 Rig、Vision Pro 有界模式、手部关节），然后是阶段 3 的交互。Vision Pro 真机的语音识别和 Quest 的 Vivox 转写路径还需要头显测试（见 [Docs/Lobby.zh-CN.md](Docs/Lobby.zh-CN.md) 的真机清单）。阶段 2–5 详见 [Docs/Plan.zh-CN.md](Docs/Plan.zh-CN.md)。

## 快速开始

1. 用 Unity **6000.3.25f1** 打开项目（需要 Unity Pro 授权才能构建 visionOS）。
2. 按 [Docs/Phase0-Spike.zh-CN.md](Docs/Phase0-Spike.zh-CN.md) 的"一次性设置"完成配置。
3. 菜单 **Tools → MR → Generate Phase 0 Spike Content**，打开 `Assets/_Project/Scenes/Main.unity` 后按 Play。

编辑器菜单：

| 菜单 | 作用 |
|---|---|
| **Tools → MR → Generate Phase 0 Spike Content** | 生成 Prefab、网络 Prefab 列表、visionOS 的 Volume Camera 配置和 `Main.unity` |
| **Tools → MR → Generate Room Content** | 生成 Room 场景、桌上物体和 RoomController Prefab（*Generate Phase 0 Spike Content* 也会顺带执行） |
| **Tools → MR → Generate Lobby Content** | 生成大厅面板、键盘、麦克风小球、语音组件和 SessionChat Prefab（*Generate Phase 0 Spike Content* 也会顺带执行） |
| **Tools → MR → Auto Quick Join In Editor** | 开关编辑器和 MPPM 虚拟玩家按 Play 时的自动 Quick Join（关闭 = 使用大厅面板） |
| **Tools → MR → Apply visionOS Player Settings** | 设置 Apple 签名团队和麦克风权限说明（缺少麦克风说明时，visionOS 会在语音启动时直接杀掉 App）；语音识别权限说明在构建时写入 Xcode 工程 |
| **Tools → MR → Enable / Disable OpenXR in Editor** | 开启或关闭编辑器 Play 模式下的 OpenXR 加载器（配合 Meta XR Simulator 使用；默认开启，没有激活的 OpenXR 运行时时，编辑器继续用键盘鼠标操作） |

> **visionOS 构建：** 从 Unity 构建前先退出 Xcode（Xcode 27 开着时构建步骤会让编辑器闪退）。
>
> **Multiplayer Play Mode 提示：** 修改场景之后，要在 Multiplayer Play Mode 窗口里把虚拟玩家关掉再打开。代码改动会自动同步给它们，但它们会继续使用之前已经打开的那份场景。

## 历史

这个项目源自一个桌面版多人大厅原型（用户名密码登录、Relay 主机、自建房间、文字聊天），原型保留在原仓库 [xz2690/Lobby-System](https://github.com/xz2690/Lobby-System) 中。
