# Cross-Platform MR Multiplayer (Apple Vision Pro + Meta Quest)

English | [简体中文](README.zh-CN.md)

A Unity 6.3 mixed reality multiplayer project: users on Apple Vision Pro (PolySpatial / RealityKit) and Meta Quest (OpenXR passthrough) join the same shared session and see each other's head, hands and shared objects.

Networking is built entirely on the Unity stack: Netcode for GameObjects (Distributed Authority) + Multiplayer Services Sessions + Vivox.

- Project plan: [Docs/Plan.md](Docs/Plan.md)
- Current phase (Phase 0: cross-platform networking spike) setup and test guide: [Docs/Phase0-Spike.md](Docs/Phase0-Spike.md)

## Quick start

1. Open the project with Unity **6000.3.25f1** (building for visionOS requires a Unity Pro license).
2. Complete the one-time setup in [Docs/Phase0-Spike.md](Docs/Phase0-Spike.md).
3. Run **Tools → MR → Generate Phase 0 Spike Content**, open `Assets/_Project/Scenes/Main.unity` and press Play.

## History

This project grew out of a desktop multiplayer lobby prototype (username/password login, Relay host, custom room list, text chat). That prototype lives in the original repository: [xz2690/Lobby-System](https://github.com/xz2690/Lobby-System).
