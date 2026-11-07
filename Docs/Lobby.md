# Lobby and Voice Commands

English | [简体中文](Lobby.zh-CN.md)

The lobby is a world-space panel that follows the user, plus a mic orb beside it. Before a session it offers **Create**, **Quick Join** and **Join by code** (with an on-panel keypad). Inside a session it shows the code, who is here (platform, speaking, muted), the session chat, and **Enter Room / Mute / Leave**. Everything on the panel can also be said out loud: the speech layer was ported from VoiceCreator ([tsoeacadrsch/metaverse-for-education](https://github.com/tsoeacadrsch/metaverse-for-education)) and runs fully on device, without any external AI service or network proxy. Anything said that is not a command is shown to everyone in the session as a subtitle.

Display names stay auto-generated (`AVP-1234`, `Editor-P1`, ...); there is no nickname input.

## Speech layer

| Piece | File | Role |
|---|---|---|
| Recognizer interface | `Assets/_Project/Voice/Speech/IVoice.cs` | `StartListening / StopListening / Speak`, events `Partial / Final / MicStateChanged / SpeakingFinished`. |
| Apple speech (visionOS) | `Assets/Plugins/visionOS/SpeechBridge.swift`, `Speech/AppleSpeechVoice.cs` | On-device `SFSpeechRecognizer` with the lobby's vocabulary (commands, letters, digits, NATO words) as contextual strings, and `AVSpeechSynthesizer` for spoken replies. The utterance ends after 1.5 s of silence (longer while a sentence looks unfinished). Native callbacks are static and queued to Unity's main loop. The Swift file is imported for visionOS only. |
| Vivox transcription (Quest) | `Speech/VivoxVoice.cs` | Quest has no native recognizer, so listening turns on Vivox's channel transcription and collects the sentences Vivox returns for the local player until a 2.5 s pause (15 s max). Only works while in the session's voice channel; replies are shown, not spoken. |
| Typed stand-in (editor, desktop) | `Speech/TypedVoice.cs` | A small text box in the Game view (hidden when an XR device is active). `Submit(text)` feeds text from tests. Keeps the whole command path testable on a Mac mini without a microphone. |
| Push-to-talk state machine | `Assets/_Project/Voice/VoiceController.cs` | Picks the recognizer (Apple → Vivox on Android → Typed), runs Idle → Listening → Processing → Speaking, mutes the Vivox mic while Apple speech owns the audio input, and raises `Transcript` with each finished utterance. A 20 s processing timeout returns to Idle. Launch argument `-say "text"` (or env `MR_SAY`) submits text 4 s after start, for device tests. |
| Mic orb | `Assets/_Project/App/MicOrb.cs` | 3D button beside the panel: tap starts listening, tap again sends early, tap while speaking interrupts. Color shows the phase. |
| Command parser | `Assets/_Project/App/VoiceCommands.cs` | Regex-based intents plus a code parser that accepts letters, letter names, digit words, NATO words and the recognizer's usual homophones ("see" → C, "for" → 4). Pure C#, unit-tested with 22 phrases. |
| Lobby brain | `Assets/_Project/App/LobbyController.cs` | Runs commands from buttons and speech, replies on the panel (and by voice when the command was spoken), keeps the chat (subtitles, join / leave lines). Waits up to 15 s for sign-in before create / join. |
| Session chat | `Assets/_Project/Networking/SessionChat.cs` | Per-session NetworkObject (`DontDestroyWithOwner`); `Say()` sends an `[Rpc(SendTo.Everyone)]` with the sender's name and text, so subtitles reach every client including the sender. |
| Panel | `Assets/_Project/App/LobbyPanel.cs`, `LazyFollow.cs` | Labels, buttons and the 36-key keypad, driven by the unified pointer (gaze + pinch, controllers, hands, mouse). Buttons are disabled until signed in and while a request is running. `LazyFollow` keeps the panel in the lower part of the view and glides it back when the head turns. |
| Voice chat | `Assets/_Project/Voice/VoiceManager.cs` | Vivox login (with `en-US` transcription language) and the group channel `s_<sessionId>`; exposes per-player speaking / muted state for the player list. |

### Commands

| Say | Does |
|---|---|
| "create a session", "start a new session" | Creates a session and reads the code back letter by letter |
| "quick join", "join any session" | Joins a ready session or creates one |
| "join code F R seven M K M", "join foxtrot romeo 7 mike kilo mike" | Joins by code (6 characters; too short / too long gets a spoken correction) |
| "enter the room", "leave the room" | Opens / closes the shared room for the whole session |
| "mute", "unmute", "microphone on / off" | Vivox microphone |
| "leave the session", "disconnect" | Leaves |
| "help" | Lists the commands |
| anything else | Sent to everyone as a subtitle in the session chat |

### Platform behaviour

| | Vision Pro | Quest | Editor / desktop |
|---|---|---|---|
| Recognition | Apple on-device | Vivox transcription (needs the voice channel, i.e. a session) | Typed text box |
| Spoken replies | Yes (`AVSpeechSynthesizer`) | No, panel only | No, panel only |
| Needs | `NSSpeechRecognitionUsageDescription` + `NSMicrophoneUsageDescription` (added to the Xcode project by the post-build step in `AppleSigningSetup.cs`) | Microphone permission | — |

On Vision Pro the Vivox microphone is muted while Apple speech is listening and restored afterwards, so the two never fight over the audio input.

## Setup

**Tools → MR → Generate Lobby Content** (also run by *Generate Phase 0 Spike Content*) configures the Swift plugin importer, creates the `SessionChat` prefab and registers it, adds the voice components and `LobbyController` to the `Services` object, builds the `LobbyPanel` with its keypad and mic orb, and removes the old stand-alone Enter Room button.

Automatic Quick Join on start is now off by default; the lobby panel is the entry point. For tests:

- **Tools → MR → Auto Quick Join In Editor** toggles it for the editor and MPPM virtual players (stored in EditorPrefs). Virtual players stagger their join by 4 s per player so that simultaneous starts end in one session.
- Builds: launch argument `-autojoin` (or env `MR_AUTOJOIN=1`).

## Testing

### Editor (typed commands)
Open `Main.unity`, press Play, wait for the title to show your name. Click the orb (or type in the box at the bottom-left of the Game view) and submit a command. From a test script: `VoiceController.Instance.Submit("create a session")`.

Passed: create session → enter the room → subtitle → mute / unmute → leave the room → leave the session → quick join, each with the expected reply on the panel.

### Multiplayer Play Mode (2 players)
Enable *Auto Quick Join In Editor*, restart the virtual player so it loads the new scene, press Play. Verified with Player 2 reading its log (`Library/VP/<player>/Logs/Editor.log`): `[Lobby] Editor-P1 joined` → the subtitle `[Lobby] Editor-P1: hello everyone, welcome to the lab` → the room opening on Player 2 after Player 1 said "enter the room" → `[Room] Closing the room` → `[Lobby] A player left` after Player 1 left.

### visionOS simulator
Build with Xcode closed, then install and launch with a spoken command injected:

```bash
xcrun simctl launch --terminate-running-process <UDID> edu.nyu.moe.visionpro1 ; SIMCTL_CHILD_MR_SAY="create a session" xcrun simctl launch <UDID> edu.nyu.moe.visionpro1
```

The simulator has no microphone and no `SFSpeechRecognizer`, so `MR_SAY` is the way to exercise the command path there. The panel, keypad and orb are used with mouse clicks (gaze + pinch).

### Real Vision Pro (needs the headset)
1. Build and run from Xcode; accept the microphone and speech recognition prompts on first use.
2. Tap the orb, say "create a session"; the code should be read back and shown on the panel.
3. On a second device (or the editor) say "join code ..." with that code; both player lists should show two names.
4. Say a plain sentence; it should appear as a subtitle on every device.
5. Say "enter the room"; everyone's room should open. Check that Vivox voice chat still works after a speech command (the mic is restored after listening).
6. Check `help`, `mute`, `unmute`, `leave the session`.

## Known limitations

- Quick Join by two instances at the very same instant can create two sessions (both are still "not ready" when the other searches). Users pressing a button are unlikely to collide; the editor stagger covers tests.
- Vivox transcription on Quest only works inside a session (there is no channel before joining), so on Quest the pre-session commands are buttons only. Recognition quality and latency there are untested until a headset is available.
- Apple speech and spoken replies are untested on a real Vision Pro in this project (the port is from a working VoiceCreator build; the simulator cannot run speech).
- The chat shows the last 8 lines and is not persisted.
