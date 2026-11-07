# 共享房间与交互

[English](Room.md) | 简体中文

大厅把所有人匹配进同一个会话之后，任何玩家都可以按下世界空间里的 **Enter Room** 按钮。整个会话一起进入一个大房间，房间中央有一张桌子，桌上有三个物体：点一下换颜色，按住拖动可以移动，所有玩家都能看到结果。

## 工作原理

| 模块 | 文件 | 作用 |
|---|---|---|
| 统一指针输入 | `Assets/_Project/XR/PointerInput.cs` | 把各平台的"看着它然后捏合"统一成一种输入：visionOS 的注视 + 捏合（PolySpatial `SpatialPointerDevice`，在 visionOS 模拟器里就是鼠标点击）、XR 手柄（瞄准射线 + 扳机：Quest、Meta XR Simulator）、Meta 手势（捏合），以及编辑器里的鼠标（相机射线 + 左键）。把 Pressed / Dragged / Released / Tapped 分发给指针指向的 `PointerInteractable`。 |
| 可交互表面 | `Assets/_Project/XR/PointerInteractable.cs` | 挂在任何带碰撞体、需要响应指针的物体上。按下后移动距离小于 3 cm 就算一次 Tap。 |
| 桌上物体 | `Assets/_Project/Networking/NetworkInteractable.cs` | 点击循环切换颜色（`NetworkVariable`）；拖动移动由所有者授权的 `NetworkTransform`。谁碰它谁先取得所有权（Distributed Authority，`Transferable`），所有权到达前的点击会在 `OnGainedOwnership` 里补上。房间关闭时本地隐藏。 |
| 房间状态 | `Assets/_Project/Networking/RoomController.cs` | 整个会话共用一个 `NetworkVariable<bool>`。任何玩家的请求通过 `[Rpc(SendTo.Owner)]` 发给所有者，之后每个客户端各自以附加方式加载或卸载 `Room.unity`。房间第一次打开时，所有者负责生成桌上物体。 |
| 按钮 | `Assets/_Project/App/WorldButton.cs`、`EnterRoomButton.cs` | 一块 3D 面板 + 文字 + 碰撞体，由统一指针驱动，现在是大厅面板的一部分（[Lobby.zh-CN.md](Lobby.zh-CN.md)）。会话和 RoomController 就绪前显示 *Connecting...*，之后显示 *Enter Room* / *Leave Room*。语音指令 "enter the room" / "leave the room" 效果相同。 |
| 房间场景 | `Assets/_Project/Scenes/Room.unity` | 只有视觉内容，没有网络物体：12 × 12 米的房间，桌子位于座位圆圈的中心，还有地毯、带书的书架、挂画、绿植、落地灯和两盏点光源。由 **Tools → MR → Generate Room Content** 生成。 |

几个值得了解的设计决定：

- **不使用 NetworkSceneManager。** 房间场景里没有网络物体，所以每个客户端本地加载即可，Distributed Authority 不受影响。桌上的网络物体生成在会话里，房间关闭时只是隐藏。
- **整个会话一起进入。** 共享空间里大家应该在同一个地方，所以按钮是会话级的开关，不是单个玩家的传送。
- **桌子在座位圆圈的中心**（`SeatAssigner`），玩家围桌而站；阶段 0 的方块现在在桌子上方 1.9 米处绕圈。
- **会话物体不随所有者消失。** RoomController、桌上物体和方块都设置了 `DontDestroyWithOwner`；不设的话，Distributed Authority 会在所有者断线时直接销毁物体，而不是交给别的客户端（测试中发现：会话创建者退出后，房间和三个物体里的两个都消失了）。`AppBootstrap` 在本地客户端被提升为会话所有者时也会补生成缺失的 RoomController。
- **先拿所有权再改状态。** Distributed Authority 下只有所有者能写颜色变量和位置，所以非所有者点击时先请求所有权（`ChangeOwnership`），到手后在 `OnGainedOwnership` 里执行。

## 测试方法

### 编辑器
1. 打开 `Main.unity`，按 Play，等 *Enter Room* 变成蓝色。
2. 用鼠标左键点它（右键是飞行相机的转向）。房间加载后，桌上出现三个物体。
3. 点物体切换颜色；按住拖动可以移动。

自动化测试可以不用鼠标：`PointerInput.Instance.Inject(ray, pressed)`（仅编辑器 / 开发版本）把一条世界空间射线送进和真实设备相同的代码路径。测试时的用法：先用相机到按钮的射线调用 `pressed = true`，下一帧用同一条射线调用 `pressed = false`。

> Pipeline 的 `simulate_pointer` 命令在这里不可靠：它把事件排进真实鼠标的队列，下一次更新时会被真实鼠标覆盖，所以按住的状态永远不会出现。

### visionOS 模拟器
在 Apple 的模拟器窗口里，鼠标点击就是注视 + 捏合，按钮和物体都可以直接操作。Claude 桌面端的面板只能显示模拟器画面，不能向 visionOS 发送输入。

### 多人
用 Multiplayer Play Mode（先停用 Meta XR Simulator）：一个玩家按 *Enter Room*，其他人也会加载房间；颜色和位置在玩家之间同步。

## 崩溃和强制退出后的恢复

崩溃或强制退出（模拟器 `terminate`）不会执行离开会话，服务端仍会认为玩家在旧会话里。`SessionManager` 在下次启动时这样处理：

1. 用 `GetJoinedSessionIdsAsync` 列出残留的会话。如果是玩家自己创建、且只剩自己的会话，直接删除；否则把自己从会话里移除。之后 Quick Join 就能找到或创建一个健康的会话（如果其他人还在，会回到原来那个）。
2. 会话带有一个 `ready` 属性，创建者只有在 Netcode 连接成功后才把它置为 `1`，Quick Join 只匹配 `ready = 1` 的会话。这样创建者如果在"建好大厅、还没启动联网"的间隙崩溃，不会留下一个能加入却连不上的会话。
3. Distributed Authority 的分配步骤在 SDK 内部可能要几分钟才会失败（默认超时 315 秒），通常发生在崩溃后不久、服务端还保留着旧连接的时候。`SessionManager` 对每次尝试设了 30 秒的看门狗，超时就放弃、清理并重试（最多 4 次），状态面板会显示 *retrying*。
4. `AppBootstrap` 只在本地客户端通过审批后才生成会话物体。Netcode 会在审批阶段就触发 `OnSessionOwnerPromoted`，那时生成会被拒绝；被拒绝的生成以前会留下一个没联网的实例，挡住后面所有的生成，这就是按钮一直显示 *Connecting...* 的原因。现在生成会等到审批通过，并清理这类残留。

已在 visionOS 模拟器上连续验证四次启动，其中三次是强制退出后重启：每次都在 15–50 秒内进入正常会话（有一次用到了 30 秒看门狗和一次重试）。

## 已知限制

- 拖动时物体和观察者沿指针射线的距离保持不变，所以横向拖动时深度也会略微变化。桌面平面约束和真正的抓取（XRI）属于阶段 3 的工作。
- 桌上物体还没有物理，可以被拖进桌子里。
- Quest 徒手捏合用的是 Meta 的瞄准姿态，没有头显之前无法测试。

## PolySpatial 的一个坑

在编辑器和 visionOS 上，PolySpatial 会维护一套镜像物体（名字形如 `… - PolySpatial id N`），它们在相同位置复制了我们的碰撞体。普通的 `Physics.Raycast` 可能打中镜像物体，而镜像物体上没有我们的任何组件。因此 `PointerInput` 改用 `RaycastAll`，优先选择最近的、属于某个 `PointerInteractable` 的命中结果。
