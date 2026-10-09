# Cross-Platform MR Multiplayer (Apple Vision Pro + Meta Quest)

English | [简体中文](README.zh-CN.md)

A Unity 6.3 mixed reality multiplayer project: users on Apple Vision Pro (PolySpatial / RealityKit) and Meta Quest (OpenXR passthrough) join the same shared session and see each other's head, hands and shared objects.

Networking is built entirely on the Unity stack: Netcode for GameObjects (Distributed Authority) + Multiplayer Services Sessions + Vivox.

- Project plan: [Docs/Plan.md](Docs/Plan.md)
- Phase 0 setup, test guide and results: [Docs/Phase0-Spike.md](Docs/Phase0-Spike.md)

## Status

**Phase 0 (cross-platform networking spike) is complete. The project is ready for Phase 1.**

### Tests passed

Local, 3 players with Multiplayer Play Mode (main editor + 2 virtual players):

- All three join the same session through Quick Join (Distributed Authority, no host)
- Each player gets its own Unity Authentication identity and a distinct name (`Editor-P1/P2/P3`)
- Avatars (head + hands + name label) spawn and stay in sync for every player
- Every player is placed in their own seat around the shared content, facing the center, and can see the others and the cube from where they start
- The shared cube's ownership rotates through all clients (1 → 2 → 3 → 1) and its color follows the owner
- **The session survives its creator leaving**: after the session owner left, the remaining two stayed in the session and kept passing the cube between them
- Vivox voice connects in the main editor; virtual players skip voice by design

On device:

- **Apple Vision Pro ↔ Editor**: the visionOS build (RealityKit with PolySpatial) signs, installs and runs on Vision Pro, and joins the same shared session as the Mac editor

### Not verified yet

- Meta Quest on a headset: the Android build is configured and the APK builds, but it has not run on a Quest yet. Without a headset, test the OpenXR path on Windows with the Meta XR Simulator ([Docs/Phase0-Spike.md](Docs/Phase0-Spike.md), test B2)
- Seat placement on Vision Pro: moving the XR Origin together with the PolySpatial Volume Camera works in the editor, but has not been checked on device

### Next: Phase 1

World-space connection panel (display name, Create / Quick Join / join by code) in place of the automatic Quick Join, on top of the session flow above. See [Docs/Plan.md](Docs/Plan.md) for Phases 1–5.

## Quick start

1. Open the project with Unity **6000.3.25f1** (building for visionOS requires a Unity Pro license).
2. Complete the one-time setup in [Docs/Phase0-Spike.md](Docs/Phase0-Spike.md).
3. Run **Tools → MR → Generate Phase 0 Spike Content**, open `Assets/_Project/Scenes/Main.unity` and press Play.

Editor menu items:

| Menu | What it does |
|---|---|
| **Tools → MR → Generate Phase 0 Spike Content** | Generates prefabs, the network prefab list, the visionOS volume camera config and `Main.unity` |
| **Tools → MR → Apply visionOS Player Settings** | Sets the Apple signing team and the microphone usage description (without it visionOS kills the app when voice starts) |
| **Tools → MR → Enable / Disable OpenXR in Editor** | Turns the OpenXR loader for editor Play mode on or off (used with the Meta XR Simulator on Windows; enabled by default and harmless on macOS) |

> **Multiplayer Play Mode tip:** after changing a scene, turn the virtual players off and on again in the Multiplayer Play Mode window. Code changes reach them automatically, but they keep using the copy of the scene they already had open.

## History

This project grew out of a desktop multiplayer lobby prototype (username/password login, Relay host, custom room list, text chat). That prototype lives in the original repository: [xz2690/Lobby-System](https://github.com/xz2690/Lobby-System).
