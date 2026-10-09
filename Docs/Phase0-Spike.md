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
- **Android tab** (already configured in the repo): **OpenXR** loader, with
  - Meta Quest Support; interaction profiles for Oculus Touch, Meta Quest Touch Plus / Pro and Hand Interaction
  - Meta OpenXR AR features: Session, Camera (passthrough), Planes, Raycast; Hand Tracking + Meta Hand Tracking Aim; Composition Layers; Foveated Rendering
  - Player Settings: package `edu.nyu.moe.visionpro1`, IL2CPP, ARM64, Vulkan, minimum API 32, internet access required
- **Standalone tab** (already configured): **OpenXR** loader with Touch / Touch Plus / hand profiles, used for the Meta XR Simulator on Windows (see test B2). Without an OpenXR runtime, as on macOS, XR simply doesn't start and the editor keeps using keyboard and mouse. Menu items: **Tools → MR → Enable / Disable OpenXR in Editor**.
- **visionOS tab**: check **Apple visionOS** and set App Mode to **RealityKit with PolySpatial**.
- Finally open **Project Validation**. Known leftovers that can be ignored:
  - *At least one interaction profile must be added* and *Composition Layers Support feature is required*: false positives when the Build Settings window has a platform other than Android selected (those rules read the selected platform).
  - *Soft shadows* / *Screen Space Ambient Occlusion*: they come from the **PC** quality level; Quest uses **Mobile**, which has neither.
  - *PoseControl* / *StickControl*: optional, project-wide input changes; skipped on purpose.
  - A `NullReferenceException` in `MetaQuestFeature.cs:539` during validation or build is the same OpenXR package bug and does not affect the build.

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

### B. Quest (device)
1. Switch the build target to **Android** (Build Profiles).
2. Build: the APK goes to `Build/Android/`. Install it with Meta Quest Developer Hub or `adb install -r MRSpike.apk`, or use Build And Run with the headset connected.
3. If you see no planes or passthrough, complete Space Setup in the headset's system settings first.

Because the session runs in the cloud, the Quest doesn't need to be on the same network: whoever has a headset can install the APK and join the Vision Pro / editor players from anywhere.

### B2. Quest without a headset: Windows + Meta XR Simulator
1. On a Windows PC, install Unity **6000.3.25f1** and clone the repository.
2. Install the **Meta XR Simulator** package (`com.meta.xr.simulator`) through the Package Manager (Unity Asset Store, free). Keep it local: the simulator is a per-machine tool, so don't commit the package change.
3. **Project Settings → XR Plug-in Management → OpenXR (Windows tab) → Play Mode OpenXR Runtime → Meta XR Simulator**.
4. Open `Main.unity` and press Play. The simulator window opens; control the simulated headset, Touch controllers and hands from it. The editor joins the shared session like any other player, so a Mac editor or the Vision Pro can join at the same time.

What this covers: the OpenXR path on a Quest-like runtime (head and controller / hand poses through `HardwareRig`), together with the networking. Not covered: passthrough, plane detection and real-device performance, which still need a headset.

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
| Meta Quest | ✅ APK builds (OpenXR, Meta Quest features, passthrough, hand tracking, VR launcher category confirmed in the manifest); ⏳ not yet run on a headset or the Meta XR Simulator |
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
