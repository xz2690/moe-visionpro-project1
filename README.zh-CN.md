# Cross-Platform MR Multiplayer (Apple Vision Pro + Meta Quest)

[English](README.md) | 简体中文

Unity 6.3 混合现实多人项目：Vision Pro（PolySpatial / RealityKit）和 Meta Quest（OpenXR 透视）的用户进入同一个共享会话，同步头、手和共享物体。

联网基于 Unity 栈：Netcode for GameObjects（Distributed Authority）+ Multiplayer Services Sessions + Vivox。

- 整体计划：[Docs/Plan.zh-CN.md](Docs/Plan.zh-CN.md)
- 当前阶段（阶段 0：三端联网验证）的设置与测试：[Docs/Phase0-Spike.zh-CN.md](Docs/Phase0-Spike.zh-CN.md)

## 快速开始

1. 用 Unity **6000.3.25f1** 打开项目（需要 Unity Pro 授权才能构建 visionOS）。
2. 按 [Docs/Phase0-Spike.zh-CN.md](Docs/Phase0-Spike.zh-CN.md) 的"一次性设置"完成配置。
3. 菜单 **Tools → MR → Generate Phase 0 Spike Content**，打开 `Assets/_Project/Scenes/Main.unity` 后按 Play。

## 历史

原来的桌面版大厅原型（用户名密码登录、Relay 主机、自建房间、聊天）保留在原仓库 [xz2690/Lobby-System](https://github.com/xz2690/Lobby-System) 中。
