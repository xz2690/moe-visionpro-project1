# 大厅与语音指令

[English](Lobby.md) | 简体中文

大厅是一块跟随用户的世界空间面板，旁边有一个麦克风小球。进入会话前，面板提供 **Create**、**Quick Join** 和 **Join by code**（带面板上的键盘）；进入会话后显示会话码、在场玩家（平台、正在说话、已静音）、会话聊天，以及 **Enter Room / Mute / Leave**。面板上的每个操作都可以直接说出来：语音层从 VoiceCreator（[tsoeacadrsch/metaverse-for-education](https://github.com/tsoeacadrsch/metaverse-for-education)）移植而来，完全在设备上运行，不依赖任何外部 AI 服务或网络代理。说的话如果不是指令，就作为字幕显示给会话里的所有人。

昵称仍然自动生成（`AVP-1234`、`Editor-P1` 等），没有昵称输入。

## 语音层

| 组成 | 文件 | 作用 |
|---|---|---|
| 识别器接口 | `Assets/_Project/Voice/Speech/IVoice.cs` | `StartListening / StopListening / Speak`，事件 `Partial / Final / MicStateChanged / SpeakingFinished`。 |
| Apple 语音（visionOS） | `Assets/Plugins/visionOS/SpeechBridge.swift`、`Speech/AppleSpeechVoice.cs` | 设备端 `SFSpeechRecognizer`，把大厅词汇（指令、字母、数字、NATO 字母）作为上下文词表；`AVSpeechSynthesizer` 朗读回复。静音 1.5 秒后结束一句话（句子看起来没说完时会多等一会）。原生回调是静态函数，排队到 Unity 主循环处理。Swift 文件只在 visionOS 平台导入。 |
| Vivox 转写（Quest） | `Speech/VivoxVoice.cs` | Quest 没有原生识别器，所以"聆听"时打开 Vivox 频道转写，收集 Vivox 返回的本地玩家句子，停顿 2.5 秒（最长 15 秒）后结束。只有在会话语音频道里才能用；回复只显示不朗读。 |
| 文本替身（编辑器、桌面） | `Speech/TypedVoice.cs` | Game 视图里的小文本框（XR 设备激活时隐藏）。`Submit(text)` 供测试注入文本。这样在没有麦克风的 Mac mini 上也能测完整条指令链路。 |
| 按键说话状态机 | `Assets/_Project/Voice/VoiceController.cs` | 选择识别器（Apple → Android 上的 Vivox → 文本），运行 Idle → Listening → Processing → Speaking，Apple 语音占用音频输入时让 Vivox 麦克风静音，每句完成的话通过 `Transcript` 事件交出去。处理超过 20 秒回到 Idle。启动参数 `-say "text"`（或环境变量 `MR_SAY`）在启动 4 秒后提交文本，用于设备测试。 |
| 麦克风小球 | `Assets/_Project/App/MicOrb.cs` | 面板旁的 3D 按钮：点一下开始聆听，再点一下提前发送，朗读时点一下打断。颜色表示当前阶段。 |
| 指令解析 | `Assets/_Project/App/VoiceCommands.cs` | 基于正则的意图识别，加上会话码解析：接受字母、字母读音、数字单词、NATO 字母和识别器常见的同音词（"see" → C，"for" → 4）。纯 C#，22 条短语单元测试通过。 |
| 大厅逻辑 | `Assets/_Project/App/LobbyController.cs` | 执行来自按钮和语音的指令，在面板上回复（指令是说出来的就同时朗读），维护聊天（字幕、加入 / 离开提示）。创建 / 加入前最多等待 15 秒登录完成。 |
| 会话聊天 | `Assets/_Project/Networking/SessionChat.cs` | 每个会话一个 NetworkObject（`DontDestroyWithOwner`）；`Say()` 通过 `[Rpc(SendTo.Everyone)]` 发送发送者名字和文本，字幕到达包括发送者在内的每个客户端。 |
| 面板 | `Assets/_Project/App/LobbyPanel.cs`、`LazyFollow.cs` | 标签、按钮和 36 键键盘，由统一指针驱动（注视 + 捏合、手柄、手势、鼠标）。登录完成前和请求执行中按钮不可用。`LazyFollow` 让面板待在视野下方，转头幅度大时平滑跟回来。 |
| 语音聊天 | `Assets/_Project/Voice/VoiceManager.cs` | Vivox 登录（转写语言 `en-US`）和群组频道 `s_<sessionId>`；向玩家列表提供每个玩家的说话 / 静音状态。 |

### 指令

| 说 | 效果 |
|---|---|
| "create a session"、"start a new session" | 创建会话并逐字朗读会话码 |
| "quick join"、"join any session" | 加入一个就绪的会话，没有就创建 |
| "join code F R seven M K M"、"join foxtrot romeo 7 mike kilo mike" | 按码加入（6 位；太短 / 太长会得到语音纠正） |
| "enter the room"、"leave the room" | 为整个会话打开 / 关闭共享房间 |
| "mute"、"unmute"、"microphone on / off" | Vivox 麦克风 |
| "leave the session"、"disconnect" | 离开会话 |
| "help" | 列出指令 |
| 其他任何话 | 作为字幕发到会话聊天，所有人可见 |

### 各平台行为

| | Vision Pro | Quest | 编辑器 / 桌面 |
|---|---|---|---|
| 识别 | Apple 设备端 | Vivox 转写（需要语音频道，即先进会话） | 文本框 |
| 朗读回复 | 有（`AVSpeechSynthesizer`） | 无，只显示 | 无，只显示 |
| 需要 | `NSSpeechRecognitionUsageDescription` + `NSMicrophoneUsageDescription`（`AppleSigningSetup.cs` 的构建后步骤写入 Xcode 工程） | 麦克风权限 | — |

Vision Pro 上，Apple 语音聆听期间 Vivox 麦克风静音，结束后恢复，两者不会争抢音频输入。

## 设置

**Tools → MR → Generate Lobby Content**（*Generate Phase 0 Spike Content* 也会执行）会配置 Swift 插件导入设置、创建并注册 `SessionChat` Prefab、把语音组件和 `LobbyController` 加到 `Services` 对象、生成带键盘和麦克风小球的 `LobbyPanel`，并移除旧的独立 Enter Room 按钮。

启动时自动 Quick Join 现在默认关闭，入口是大厅面板。测试时：

- **Tools → MR → Auto Quick Join In Editor** 为编辑器和 MPPM 虚拟玩家开关自动加入（存在 EditorPrefs）。虚拟玩家按编号每人错开 4 秒再加入，同时启动也能进同一个会话。
- 构建版本：启动参数 `-autojoin`（或环境变量 `MR_AUTOJOIN=1`）。

## 测试

### 编辑器（文本指令）
打开 `Main.unity`，按 Play，等标题显示你的名字。点小球（或在 Game 视图左下角的文本框里输入）并提交指令。测试脚本里：`VoiceController.Instance.Submit("create a session")`。

已通过：创建会话 → 进入房间 → 字幕 → 静音 / 取消静音 → 离开房间 → 离开会话 → 快速加入，面板上都出现预期回复。

### Multiplayer Play Mode（2 人）
打开 *Auto Quick Join In Editor*，重启虚拟玩家让它加载新场景，按 Play。通过读取 Player 2 的日志（`Library/VP/<player>/Logs/Editor.log`）验证：`[Lobby] Editor-P1 joined` → 字幕 `[Lobby] Editor-P1: hello everyone, welcome to the lab` → Player 1 说 "enter the room" 后 Player 2 的房间打开 → `[Room] Closing the room` → Player 1 离开后 `[Lobby] A player left`。

### visionOS 模拟器
关闭 Xcode 后构建，然后安装并带着注入的语音指令启动：

```bash
xcrun simctl launch --terminate-running-process <UDID> edu.nyu.moe.visionpro1 ; SIMCTL_CHILD_MR_SAY="create a session" xcrun simctl launch <UDID> edu.nyu.moe.visionpro1
```

模拟器没有麦克风也没有 `SFSpeechRecognizer`，所以 `MR_SAY` 是在模拟器里跑指令链路的方法。面板、键盘和小球用鼠标点击（等同注视 + 捏合）。

### Vision Pro 真机（需要头显）
1. 从 Xcode 构建并运行；首次使用时同意麦克风和语音识别权限。
2. 点小球，说 "create a session"；应朗读会话码并显示在面板上。
3. 在第二台设备（或编辑器）说 "join code ..." 加上这个码；两边的玩家列表都应显示两个名字。
4. 说一句普通的话；应作为字幕出现在每台设备上。
5. 说 "enter the room"；所有人的房间都应打开。确认语音指令之后 Vivox 语音聊天仍然正常（聆听结束后麦克风恢复）。
6. 检查 `help`、`mute`、`unmute`、`leave the session`。

## 已知限制

- 两个实例在同一瞬间 Quick Join 可能创建两个会话（互相搜索时双方都还"未就绪"）。用户按按钮几乎不会撞上；编辑器里的错开延迟覆盖了测试场景。
- Quest 上的 Vivox 转写只能在会话里用（加入前没有频道），所以 Quest 进会话前的操作只能按按钮。头显到手之前识别质量和延迟都未测。
- 本项目还没在 Vision Pro 真机上测过 Apple 语音和朗读（移植自一个能用的 VoiceCreator 构建；模拟器跑不了语音）。
- 聊天只显示最近 8 行，不保存。
