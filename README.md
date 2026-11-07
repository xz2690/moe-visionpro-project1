# Cross-Platform MR Multiplayer (Apple Vision Pro + Meta Quest)

English | [简体中文](README.zh-CN.md)

A Unity 6.3 mixed reality multiplayer project: users on Apple Vision Pro (PolySpatial / RealityKit) and Meta Quest (OpenXR passthrough) join the same shared session and see each other's head, hands and shared objects.

Networking is built entirely on the Unity stack: Netcode for GameObjects (Distributed Authority) + Multiplayer Services Sessions + Vivox.

- Project plan: [Docs/Plan.md](Docs/Plan.md)
- Phase 0 setup, test guide and results: [Docs/Phase0-Spike.md](Docs/Phase0-Spike.md)
- Shared room, table objects and the cross-platform pointer: [Docs/Room.md](Docs/Room.md)
- Lobby panel, voice commands and subtitles: [Docs/Lobby.md](Docs/Lobby.md)

## Status

**Phase 0 (cross-platform networking spike) and Phase 1 (lobby and session flow, with voice commands) are complete. The project is ready for Phase 2.**

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

Quest path without a headset:

- **Android build**: the APK builds; its manifest has OpenXR, the VR launcher category, passthrough and hand tracking
- **Meta XR Simulator (macOS)**: OpenXR runs with a simulated headset and two Touch Plus controllers; hand poses come from the controllers; session, seat and voice work

### Not verified yet

- Meta Quest on a headset: the Android build is configured and the APK builds, but it has not run on a Quest yet. Without a headset, use the Meta XR Simulator on macOS or Windows ([Docs/Phase0-Spike.md](Docs/Phase0-Spike.md), test B2)
- Seat placement on Vision Pro: moving the XR Origin together with the PolySpatial Volume Camera works in the editor, but has not been checked on device

### Shared room (added after Phase 0)

- **Enter Room** button in the lobby (3D, works with gaze + pinch, controllers, hands and the mouse): the whole session enters a 12 × 12 m room with a table
- Three objects on the table: tap to cycle color, drag to move; ownership follows whoever touches them, state syncs to everyone
- Verified in the editor with the pointer injection hook: open room → tap → drag. See [Docs/Room.md](Docs/Room.md)

### Lobby and voice commands (Phase 1)

- World-space lobby panel that follows the user: Create / Quick Join / Join by code (on-panel keypad) before a session; code, player list (platform, speaking, muted), session chat and Enter Room / Mute / Leave inside one. Automatic Quick Join is off by default (editor toggle and `-autojoin` for tests)
- Speech layer ported from VoiceCreator: on-device Apple speech recognition and text-to-speech on Vision Pro (Swift bridge), Vivox transcription on Quest, a typed stand-in in the editor. Every panel action is a voice command ("create a session", "join code F R seven M K M", "enter the room", "mute", ...); anything else is sent to all players as a subtitle
- Verified in the editor (all commands) and with two Multiplayer Play Mode players: join / leave lines, subtitles and the room opening arrive on the other player. Crash and force-quit recovery verified in the visionOS simulator. See [Docs/Lobby.md](Docs/Lobby.md)

### Next: Phase 2

Rig selection and avatar polish (Quest MR passthrough rig, Vision Pro bounded mode, hand joints), then Phase 3 interaction. Voice recognition on a real Vision Pro and the Quest Vivox transcription path still need headset tests ([Docs/Lobby.md](Docs/Lobby.md), real-device checklist). See [Docs/Plan.md](Docs/Plan.md) for Phases 2–5.

## Quick start

1. Open the project with Unity **6000.3.25f1** (building for visionOS requires a Unity Pro license).
2. Complete the one-time setup in [Docs/Phase0-Spike.md](Docs/Phase0-Spike.md).
3. Run **Tools → MR → Generate Phase 0 Spike Content**, open `Assets/_Project/Scenes/Main.unity` and press Play.

Editor menu items:

| Menu | What it does |
|---|---|
| **Tools → MR → Generate Phase 0 Spike Content** | Generates prefabs, the network prefab list, the visionOS volume camera config and `Main.unity` |
| **Tools → MR → Generate Room Content** | Generates the Room scene, the table objects and the RoomController prefab (also run by *Generate Phase 0 Spike Content*) |
| **Tools → MR → Generate Lobby Content** | Generates the lobby panel, keypad, mic orb, voice components and the SessionChat prefab (also run by *Generate Phase 0 Spike Content*) |
| **Tools → MR → Auto Quick Join In Editor** | Toggles automatic Quick Join on Play for the editor and MPPM virtual players (off = use the lobby panel) |
| **Tools → MR → Apply visionOS Player Settings** | Sets the Apple signing team and the microphone usage description (without it visionOS kills the app when voice starts); the speech recognition usage description is added to the Xcode project at build time |
| **Tools → MR → Enable / Disable OpenXR in Editor** | Turns the OpenXR loader for editor Play mode on or off (used with the Meta XR Simulator; enabled by default, and without an active OpenXR runtime the editor just keeps keyboard / mouse) |

> **visionOS builds:** quit Xcode before building from Unity (with Xcode 27 open the build step crashes the editor).
>
> **Multiplayer Play Mode tip:** after changing a scene, turn the virtual players off and on again in the Multiplayer Play Mode window. Code changes reach them automatically, but they keep using the copy of the scene they already had open.

## History

This project grew out of a desktop multiplayer lobby prototype (username/password login, Relay host, custom room list, text chat). That prototype lives in the original repository: [xz2690/Lobby-System](https://github.com/xz2690/Lobby-System).
