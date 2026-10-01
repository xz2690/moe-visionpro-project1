# Cross-Platform MR Multiplayer Plan (Apple Vision Pro + Meta Quest)

English | [简体中文](Plan.zh-CN.md)

## Context

`xz2690/Lobby-System` was a desktop multiplayer lobby prototype built with Unity 6 + Netcode for GameObjects (NGO) + Relay + Unity Authentication, used to validate the networking approach. Building on it, the goal is a **mixed reality (MR) multiplayer app where Vision Pro and Meta Quest users share one session**. Features follow Photon's *Fusion Cross-platform Mixed Reality* sample:

- On AVP, switch between Unbounded (immersive space with head and hand tracking) and Bounded (a volume window giving a top-down overview of everyone); on Quest, passthrough MR
- Head + both hands + finger joint sync (highly compressed)
- Shared object grabbing (direct pinch on all platforms; gaze + indirect pinch on visionOS)
- A hand menu that appears when you look at your palm; magnetic building blocks that snap to each other and to locally detected real-world planes (planes are not synced)
- Voice chat

Confirmed decisions:
- AVP uses **PolySpatial (RealityKit MR)**; the team has Unity Pro, an Apple Silicon Mac, AVP and Quest devices
- Sign-in uses **Unity Authentication anonymous sign-in + a display name**
- Networking stays on the **Unity stack (NGO)** instead of switching to Photon; each module of the Photon sample is mapped to a Unity equivalent

## Technology mapping (Photon sample → this project)

| Photon sample | This project |
|---|---|
| Fusion 2 Shared Mode | NGO 2.x **Distributed Authority** via `com.unity.services.multiplayer` Sessions (no host; the session survives anyone leaving; closest match to Shared Mode) |
| ConnectionManager addon | `SessionManager` (Create / QuickJoin / JoinByCode; replaces the prototype's hand-written Relay code and custom room system) |
| XRShared (hardware rig / network rig split, grabbing) | XR Interaction Toolkit 3.x + custom `NetworkRig` / `NetworkGrabbable` |
| XRHands synchronization | XR Hands 1.x + custom quantized, compressed joint sync |
| ExtendedRigSelection | `RigSelector`: Quest MR rig / AVP Unbounded rig / AVP Bounded rig |
| visionOS helpers | PolySpatial 3.x + `com.unity.xr.visionos` + XRI visionOS input (SpatialPointer) + custom LineRenderer replacement, etc. |
| Photon Voice | Vivox (`com.unity.services.vivox`), one channel per session Id |
| Magnets | Custom `Magnet` / `MagnetAttractor` |
| Plane detection | AR Foundation 6.x `ARPlaneManager` (Quest: Meta OpenXR; AVP: visionOS ARKit), local only |

Rendering: URP (kept). All materials use only URP Lit/Unlit or Shader Graph, because PolySpatial does not support hand-written HLSL shaders or post-processing.

## What happens to the prototype code

> The prototype files mentioned below are not part of this repository; see the original prototype at [xz2690/Lobby-System](https://github.com/xz2690/Lobby-System).

- **Patterns reused**: the `IAuthProvider` interface (`AuthModels.cs`), singleton + `DontDestroyOnLoad` bootstrapping (`LobbyBootstrap.cs`), one-click editor generation (`LobbySetup.cs`), and the initialization / error-mapping logic of `UnityServicesAuthProvider`.
- **Replaced**: `NetworkLobbyManager`'s manual Relay allocation → Sessions API; `LobbyManager`'s custom rooms / ready state / chat → a session *is* the room, and voice replaces text chat; `ClientNetworkTransform` → `NetworkTransform` with owner authority; the self-generated token in the connection payload is dropped, and identity comes from `AuthenticationService.PlayerId` plus the display name in session player properties.
- **Removed**: username/password UI, `LocalAuthProvider`, screen-space canvas, WASD `PlayerController`, `.plastic/`, `ignore.conf`, `TutorialInfo/`.
- The 5 bugs found in the earlier review (ghost players, scene change for the whole session, joining multiple rooms, cross-room kicks, spoofable chat sender) all live in replaced code, so they disappear with the rewrite and are not fixed separately.

## Project structure

On a new branch (or a new repository, starting from this repo's `ProjectSettings` / `Packages`):

```
Assets/_Project/
  Core/          Bootstrap, platform detection, service init         (asmdef: Core)
  Auth/          AnonymousAuthProvider, display name storage         (asmdef: Auth)
  Networking/    SessionManager, NetworkRig, HandSync, NetworkGrabbable (asmdef: Networking)
  XR/            RigSelector, HardwareRig variants, gestures, HandMenu  (asmdef: XR)
  Interaction/   Magnets, building blocks, plane snapping
  Voice/         VoiceManager (Vivox)
  UI/            World-space connection panel, hand menu UI
  Platform/visionOS/  VolumeCamera switching, LineRenderer replacement, indirect pinch (#if UNITY_VISIONOS)
  Platform/Quest/     Passthrough / room setup hint (#if UNITY_ANDROID)
  Scenes/        Bootstrap.unity, Main.unity
```

XR Plug-in Management: Android → OpenXR (Meta Quest feature group + Meta OpenXR AR features); visionOS → Apple visionOS, App Mode = RealityKit with PolySpatial.

## Phases

### Phase 0: Technical spike (highest risk, do first)
Goal: on all three targets (Editor / Quest 3 / AVP), get "join the same session and sync a cube" working.
1. Confirm a compatible combination of Unity 6.3, PolySpatial 3.x, the visionOS plugin, Meta OpenXR, NGO and Multiplayer Services, and lock the versions.
2. Minimal scene: `SessionManager` with `CreateOrJoinSessionAsync` + `WithDistributedAuthorityNetwork()`, spawning an owner-authoritative NetworkObject cube.
3. Build for Quest (Android/APK) and AVP (visionOS/Xcode) and connect all three.
4. Verify Vivox compiles and works on visionOS.
5. **Exit criteria**: DA is stable on all three targets. If DA has problems on visionOS, fall back to Relay client-host mode (same idea as the prototype's `StartHostRelay`/`JoinRelay`, but via Sessions' `WithRelayNetwork()`). If Vivox does not support visionOS, make voice optional or switch providers.

### Phase 1: Project skeleton and session flow
- Restructure the repo as above, add asmdefs, remove prototype and template leftovers.
- `AnonymousAuthProvider`: `SignInAnonymouslyAsync` + `UpdatePlayerNameAsync`; display name stored in PlayerPrefs.
- `SessionManager`: Create (returns a short code) / QuickJoin / JoinByCode / Leave; player properties carry display name and platform / mode.
- World-space connection panel: display name input (system keyboard on Quest and visionOS), Create, join code, Quick Join.
- A single Main scene (MR does not need a separate lobby scene); the network rig spawns locally after connecting.

### Phase 2: Rig selection and avatar sync
- Split hardware rig and network rig: the local `HardwareRig` captures head / left hand / right hand / fingers; `NetworkRig` (the player prefab) copies from the local hardware rig every frame and syncs head and wrists with owner-authoritative `NetworkTransform`s.
- `RigSelector` with three configurations:
  - Quest MR: XR Origin + AR Camera (passthrough) + controllers and hand tracking
  - AVP Unbounded: VolumeCamera Unbounded + ARKit head/hand tracking (XR Hands) + SpatialPointer
  - AVP Bounded: VolumeCamera Bounded, no head/hand tracking; the shared scene is scaled into the volume for a top-down view; the mode is broadcast so other players hide this player's body
- `HandSync`: 26 joint rotations with smallest-three quantization (~4 bytes each) + wrist position at 20–30 Hz, sent via a custom `INetworkSerializable` over unreliable RPCs (or a NetworkVariable), interpolated on receivers. When a Quest user holds controllers, send preset hand poses.
- Avatar look: simple head + hand models, a name label, and a color hashed from the PlayerId (same palette idea as the prototype's `PlayerController`).

### Phase 3: Interaction
- `NetworkGrabbable`: XRI `XRGrabInteractable` select event → request ownership (DA `NetworkObject` ownership flags set to transferable / requestable); non-owners are kinematic; after release the owner simulates physics.
- Input: direct pinch on all platforms (XR Hands + XRI Poke/Direct interactors); gaze + indirect pinch on visionOS via SpatialPointer; Ray / Direct interactors for Quest controllers.
- Hand menu: shows the block menu when the palm faces the head (XR Hands gesture detection); taking a block from the menu spawns it with `NetworkObject.Spawn`.
- Magnets: blocks have snap points on top and sides and snap to the nearest point on release; local AR planes get colliders and act as snap targets too (planes are not synced, only the blocks' final positions).

### Phase 4: Voice
- `VoiceManager`: log in to Vivox → join a channel named after the session Id (positional channel for Unbounded / Quest users, regular channel for Bounded users); handle microphone permission (Android manifest, visionOS Info.plist); mute button in the hand menu.

### Phase 5: Platform polish and performance
- visionOS: switch all materials to Shader Graph / standard URP shaders, replace LineRenderer with a PolySpatial-supported approach, add Info.plist usage descriptions for hand tracking / world sensing, runtime Bounded ↔ Unbounded switching.
- Quest: URP mobile settings, foveated rendering, MSAA 4x, Single Pass Instanced; show a hint when room setup has not been done.
- Networking: tune send rates and interpolation; reconnect on disconnect (rejoin the session).

## Key files (new)

- `Networking/SessionManager.cs`: replaces `NetworkLobbyManager.cs`
- `Auth/AnonymousAuthProvider.cs`: implements the existing `IAuthProvider`
- `Networking/NetworkRig.cs`, `Networking/HandSync.cs`, `Networking/HandPoseSerializer.cs`
- `XR/RigSelector.cs`, `XR/HardwareRig*.cs`, `XR/HandMenu.cs`
- `Networking/NetworkGrabbable.cs`, `Interaction/Magnet*.cs`
- `Voice/VoiceManager.cs`
- `Platform/visionOS/VolumeModeSwitcher.cs`
- `Editor/ProjectSetup.cs`: one-click generation in the spirit of `LobbySetup`; creates prefabs and registers NetworkPrefabs

## Verification

1. **Editor**: Multiplayer Play Mode with 2–4 virtual players + XR Interaction Simulator to test sessions, head/hand sync and grab ownership transfer.
2. **AVP**: PolySpatial Play to Device for fast iteration; test Bounded mode in the Xcode visionOS simulator; test Unbounded hand tracking and indirect pinch on device.
3. **Quest**: Quest Link for debugging + APK on device for passthrough, plane detection, and switching between controllers and hands.
4. **Cross-platform acceptance checklist** (run at every milestone):
   - Quest + AVP Unbounded + Editor in the same session can see each other's heads, hands and fingers
   - When anyone leaves (including the session creator), the session continues and no stale avatar remains
   - A picks up a block and hands it to B without jitter or snapping back; magnet results match on all three
   - After AVP switches to Bounded, others no longer see its body, and it gets an overview of the whole scene
   - Voice works both ways with correct positional falloff
5. **Performance**: Quest holds 72/90 fps; check AVP with RealityKit Trace; record upstream bandwidth per player (target < 30 KB/s).

## Risks

- How well NGO Distributed Authority and Vivox work on visionOS → verified in Phase 0, with fallbacks.
- PolySpatial rendering and component limits (shaders, particles, LineRenderer, post-processing) → only use compatible assets from day one.
- The devices **do not share physical space** (as in the reference sample, there is no co-location alignment): each person sees the others' avatars in their own room. If same-room collaboration is needed later, plan spatial anchor alignment separately.
