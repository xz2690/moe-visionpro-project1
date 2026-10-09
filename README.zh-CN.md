# Cross-Platform MR Multiplayer (Apple Vision Pro + Meta Quest)

[English](README.md) | 简体中文

Unity 6.3 混合现实多人项目：Vision Pro（PolySpatial / RealityKit）和 Meta Quest（OpenXR 透视）的用户进入同一个共享会话，同步头、手和共享物体。

联网基于 Unity 栈：Netcode for GameObjects（Distributed Authority）+ Multiplayer Services Sessions + Vivox。

- 整体计划：[Docs/Plan.zh-CN.md](Docs/Plan.zh-CN.md)
- 阶段 0 的设置、测试方法和结果：[Docs/Phase0-Spike.zh-CN.md](Docs/Phase0-Spike.zh-CN.md)

## 当前状态

**阶段 0（跨平台联网技术验证）已完成，可以开始阶段 1 的开发。**

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

### 尚未验证

- Meta Quest 真机：Android 构建已经配好，APK 也能成功打包，但还没在 Quest 上运行过。没有头显时，可以在 Windows 上用 Meta XR Simulator 测试 OpenXR 这条路径（见 [Docs/Phase0-Spike.zh-CN.md](Docs/Phase0-Spike.zh-CN.md) 的测试 B2）
- Vision Pro 上的座位效果：XR Origin 和 PolySpatial Volume Camera 一起移动的做法在编辑器里正常，还没在真机上确认

### 下一步：阶段 1

在上面的会话流程基础上，做一块世界空间的连接面板（昵称、创建 / 快速加入 / 输入加入码），取代现在的自动快速加入。阶段 1–5 详见 [Docs/Plan.zh-CN.md](Docs/Plan.zh-CN.md)。

## 快速开始

1. 用 Unity **6000.3.25f1** 打开项目（需要 Unity Pro 授权才能构建 visionOS）。
2. 按 [Docs/Phase0-Spike.zh-CN.md](Docs/Phase0-Spike.zh-CN.md) 的"一次性设置"完成配置。
3. 菜单 **Tools → MR → Generate Phase 0 Spike Content**，打开 `Assets/_Project/Scenes/Main.unity` 后按 Play。

编辑器菜单：

| 菜单 | 作用 |
|---|---|
| **Tools → MR → Generate Phase 0 Spike Content** | 生成 Prefab、网络 Prefab 列表、visionOS 的 Volume Camera 配置和 `Main.unity` |
| **Tools → MR → Apply visionOS Player Settings** | 设置 Apple 签名团队和麦克风权限说明（缺少麦克风说明时，visionOS 会在语音启动时直接杀掉 App） |
| **Tools → MR → Enable / Disable OpenXR in Editor** | 开启或关闭编辑器 Play 模式下的 OpenXR 加载器（配合 Windows 上的 Meta XR Simulator 使用；默认开启，在 macOS 上没有影响） |

> **Multiplayer Play Mode 提示：** 修改场景之后，要在 Multiplayer Play Mode 窗口里把虚拟玩家关掉再打开。代码改动会自动同步给它们，但它们会继续使用之前已经打开的那份场景。

## 历史

这个项目源自一个桌面版多人大厅原型（用户名密码登录、Relay 主机、自建房间、文字聊天），原型保留在原仓库 [xz2690/Lobby-System](https://github.com/xz2690/Lobby-System) 中。
