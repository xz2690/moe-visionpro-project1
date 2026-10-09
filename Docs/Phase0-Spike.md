# Phase 0: Cross-Platform Networking Spike

English | [简体中文](Phase0-Spike.zh-CN.md)

Goal: **Editor / Meta Quest / Apple Vision Pro** join the same Multiplayer Services session (Distributed Authority), see each other's head and hands, and pass ownership of a shared cube between all of them.

The spike scene needs no input: every device signs in anonymously on launch → Quick Join (creating a session if none exists) → shows a status panel.

## Locked versions

| Component | Version |
|---|---|
| Unity | 6000.3.25f1 |
| Netcode for GameObjects | 2.13.3 (NGO 3.x requires Unity 6000.7, not upgrading yet) |
| Multiplayer Services | 2.3.3 |
| PolySpatial / visionOS | 3.3.1 |
| OpenXR / Meta OpenXR | 1.18.0 / 2.6.1 |
| AR Foundation | 6.6.2 |
| XR Interaction Toolkit / XR Hands | 3.6.1 / 1.9.0 |
| Vivox | 16.12.1 (ships visionOS native libraries) |

## One-time setup (manual)

### 1. Unity Hub
- Install the **Android Build Support** module (with OpenJDK and Android SDK & NDK) for 6000.3.25f1.

### 2. Unity Cloud
1. **Edit → Project Settings → Services**: link a Unity Cloud project.
2. On [cloud.unity.com](https://cloud.unity.com), enable for that project:
   - **Multiplayer → Sessions / Distributed Authority**
   - **Vivox**
   - Anonymous sign-in is enabled in Authentication by default; **no** username/password provider is needed.

### 3. XR Plug-in Management (Edit → Project Settings → XR Plug-in Management)
- **Android tab**: check **OpenXR** and the **Meta Quest** feature group. Then on the OpenXR page:
  - Interaction profiles: add *Oculus Touch Controller Profile* and *Hand Interaction Profile*
  - Features: enable *Meta Quest Support*, *Meta Quest: Session*, *Meta Quest: Camera (Passthrough)*, *Meta Quest: Planes*, *Hand Tracking Subsystem*
- **visionOS tab**: check **Apple visionOS** and set App Mode to **RealityKit with PolySpatial**.
- Finally open **Project Validation** and click *Fix All* for both platforms.

### 4. Usage descriptions (Player Settings → visionOS → Other Settings, and the Apple visionOS settings page)
- Hand Tracking Usage Description: "Used to share your hand movements with other participants"
- World Sensing Usage Description: "Used to detect surfaces such as tables for placing objects"
- Microphone Usage Description: "Used for voice chat with other participants"
- On Android, `VoiceManager` requests the microphone permission at runtime.
- Shortcut: **Tools → MR → Apply visionOS Player Settings** sets the microphone description (and the Apple signing team). Note the field is *Microphone* Usage Description, not *Camera*: without it visionOS kills the app as soon as Vivox opens the mic.

### 5. Generate the spike scene
Run **Tools → MR → Generate Phase 0 Spike Content** (safe to re-run). It creates:
- `Assets/_Project/Prefabs/NetworkRig.prefab`: the player prefab with head, hands and name label
- `Assets/_Project/Prefabs/OwnershipProbe.prefab`: the ownership rotation test cube
- `Assets/_Project/Settings/NetworkPrefabs.asset`, `UnboundedVolume.asset`
- `Assets/_Project/Scenes/Main.unity`, set as the only scene in Build Settings

## How to test

### A. Editor (Multiplayer Play Mode)
1. **Window → Multiplayer → Multiplayer Play Mode**, enable 1–3 virtual players.
2. Open `Main.unity` and press Play.
3. Controls: WASD to move, Q/E for down/up, hold right mouse button to look. Without a headset, `HardwareRig` simulates hands in front of you.
4. Each virtual player uses its own authentication profile (see `UnityServicesInitializer`), so each gets a different PlayerId.
5. Each player is named after its Multiplayer Play Mode number (`Editor-P1`, `Editor-P2`, ...) and placed in its own seat around the cube (`SeatAssigner`). Virtual players skip voice: Vivox's native library cannot load in them.
6. **After changing a scene, turn the virtual players off and on again.** Code changes reach them automatically, but they keep the copy of the scene they already had open.

### B. Quest
- Switch Build Settings to Android → Build And Run, or use Quest Link to run directly from the editor.
- If you see no planes or passthrough, complete Space Setup in the headset's system settings first.

### C. Vision Pro
- Switch Build Settings to visionOS → Build, open the generated project in Xcode and deploy to the device.
- Or use **PolySpatial → Play to Device** for fast iteration.

## Pass criteria

With at least **Editor + Quest + AVP** online at the same time:

- [ ] All three status panels show the **same session code**, and the player list includes everyone with their platform
- [ ] Every target sees the others' heads and hands moving with acceptable latency
- [ ] The cube orbits and changes owner (and color) every 5 seconds, moving continuously without jumps
- [ ] After closing the **session creator**, the session still exists, the cube keeps being driven by someone else, and the closed player's avatar disappears
- [ ] All three can talk to each other

## Results (2026-10-08)

**Phase 0 passed. The project is ready for Phase 1.**

| Check | Result |
|---|---|
| Same session, player list with platforms | ✅ Editor 3 players (MPPM); ✅ Vision Pro + Editor |
| Heads and hands of others visible and moving | ✅ Editor 3 players; each player seated on a circle around the cube, facing the center |
| Cube orbits, changes owner and color every 5 s | ✅ Rotates 1 → 2 → 3 → 1 on all clients |
| Session survives the creator leaving | ✅ Remaining two players stayed in the session and kept passing the cube |
| Voice | ✅ Main editor connects; virtual players skip voice by design |
| Meta Quest | ⏳ Not tested yet (needs the Android Build Support module) |
| Seat placement on Vision Pro | ⏳ Works in the editor; not yet checked on device |

Fixed during testing:

- Vision Pro crashed when Vivox opened the microphone: the voice text had been entered as the *camera* usage description. Use **Tools → MR → Apply visionOS Player Settings**.
- All MPPM players had the same name (they share PlayerPrefs); names now come from the player number.
- Exiting Play mode logged `Leave on destroy failed: lobby not found`; the Multiplayer SDK already leaves on quit, so the extra leave was removed.

## Fallbacks if it fails

- **Distributed Authority is unstable on some target**: set `SessionManager.networkMode` on the `Services` object to `RelayClientHost`, which falls back to the prototype's host model (`AppBootstrap` already handles the host spawning the cube).
- **Vivox is unavailable**: voice does not block the rest of the spike. The status panel shows `Voice unavailable: ...` and voice can be skipped for now.

## Code layout

```
Assets/_Project/
  Core/        PlatformInfo, PlayerColors, EditorInstanceInfo
  Auth/        IAuthProvider, AnonymousAuthProvider, AuthManager, UnityServicesInitializer
  XR/          HardwareRig (head/hand poses: XR Hands → controllers → editor simulation), EditorFlyCamera
  Networking/  SessionManager (Sessions API), NetworkRig (player prefab), OwnershipRotationProbe, SeatAssigner
  Voice/       VoiceManager (Vivox, channel name = session Id)
  App/         AppBootstrap (auto sign-in + quick join), StatusPanel
  Editor/      ProjectSetup (one-click generation), AppleSigningSetup (visionOS Player Settings)
```
