using System.Linq;
using MR.App;
using MR.Networking;
using MR.Voice;
using MR.XR;
using TMPro;
using Unity.Netcode;
using Unity.PolySpatial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MR.EditorTools
{
    /// <summary>
    /// Generates the lobby: the world-space LobbyPanel with its buttons, keypad and mic orb,
    /// the SessionChat network prefab, the voice components on the Services object, and the
    /// visionOS speech plugin's import settings. Replaces the Phase 0 Enter Room button.
    /// Safe to re-run.
    /// </summary>
    public static class LobbySetup
    {
        private const string Root = "Assets/_Project";
        private const string PrefabDir = Root + "/Prefabs";
        private const string MainScenePath = Root + "/Scenes/Main.unity";
        private const string SwiftPluginPath = "Assets/Plugins/visionOS/SpeechBridge.swift";

        private const float ButtonDepth = -0.012f; // in front of the panel background (front face is -Z)
        private const float LabelDepth = -0.004f;
        private const float KeyPitch = 0.048f;

        [MenuItem("Tools/MR/Generate Lobby Content")]
        public static void Generate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ConfigureSwiftPlugin();

            var panelMat = ProjectSetup.CreateLitMaterial("Lobby_Panel", new Color(0.13f, 0.14f, 0.17f));
            var buttonMat = ProjectSetup.CreateLitMaterial("Button", new Color(0.18f, 0.44f, 0.85f));
            var keyMat = ProjectSetup.CreateLitMaterial("Lobby_Key", new Color(0.30f, 0.33f, 0.40f));
            var orbMat = ProjectSetup.CreateLitMaterial("Lobby_Orb", new Color(0.55f, 0.75f, 1f));

            var chatPrefab = CreateSessionChatPrefab();
            RoomSetup.RegisterNetworkPrefabs(new[] { chatPrefab });

            WireMainScene(chatPrefab, panelMat, buttonMat, keyMat, orbMat);

            AssetDatabase.SaveAssets();
            Debug.Log("[LobbySetup] Lobby generated: LobbyPanel, mic orb, SessionChat, voice components.");
        }

        private const string AutoJoinMenu = "Tools/MR/Auto Quick Join In Editor";

        [MenuItem(AutoJoinMenu)]
        private static void ToggleAutoJoin() => AppBootstrap.AutoJoinInEditor = !AppBootstrap.AutoJoinInEditor;

        [MenuItem(AutoJoinMenu, true)]
        private static bool ToggleAutoJoinValidate()
        {
            Menu.SetChecked(AutoJoinMenu, AppBootstrap.AutoJoinInEditor);
            return true;
        }

        // ── Assets ─────────────────────────────────

        /// <summary>The speech plugin compiles only into the visionOS Xcode project.</summary>
        private static void ConfigureSwiftPlugin()
        {
            AssetDatabase.ImportAsset(SwiftPluginPath);
            if (AssetImporter.GetAtPath(SwiftPluginPath) is not PluginImporter importer)
            {
                Debug.LogWarning("[LobbySetup] Swift plugin not found at " + SwiftPluginPath);
                return;
            }
            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(false);
            importer.SetCompatibleWithPlatform(BuildTarget.VisionOS, true);
            importer.SaveAndReimport();
        }

        private static GameObject CreateSessionChatPrefab()
        {
            var go = new GameObject("SessionChat");
            go.AddComponent<NetworkObject>().DontDestroyWithOwner = true;
            go.AddComponent<SessionChat>();
            return ProjectSetup.SavePrefab(go, $"{PrefabDir}/SessionChat.prefab");
        }

        // ── Main scene ─────────────────────────────

        private static void WireMainScene(GameObject chatPrefab, Material panelMat, Material buttonMat, Material keyMat, Material orbMat)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath)
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            GameObject Find(string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

            // Services: voice stack + lobby controller
            var services = Find("Services");
            if (services == null)
            {
                Debug.LogError("[LobbySetup] 'Services' not found in Main.unity. Run Generate Phase 0 Spike Content first.");
                return;
            }
            Ensure<AppleSpeechVoice>(services);
            Ensure<VivoxVoice>(services);
            Ensure<TypedVoice>(services);
            Ensure<VoiceController>(services);
            Ensure<LobbyController>(services);

            var bootstrap = services.GetComponent<AppBootstrap>();
            if (bootstrap != null)
            {
                var so = new SerializedObject(bootstrap);
                so.FindProperty("sessionChatPrefab").objectReferenceValue = chatPrefab;
                so.FindProperty("autoQuickJoin").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // The Phase 0 button lives in the panel now.
            var oldButton = Find("EnterRoomButton");
            var origin = Find("XR Origin");
            var seats = origin != null ? origin.GetComponent<SeatAssigner>() : null;
            if (seats != null)
            {
                var so = new SerializedObject(seats);
                var carry = so.FindProperty("carryAlong");
                for (int i = carry.arraySize - 1; i >= 0; i--)
                {
                    var t = carry.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (t == null || (oldButton != null && t == oldButton.transform)) carry.DeleteArrayElementAtIndex(i);
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (oldButton != null) Object.DestroyImmediate(oldButton);

            var existing = Find("LobbyPanel");
            if (existing != null) Object.DestroyImmediate(existing);
            var panel = CreatePanel(panelMat, buttonMat, keyMat, orbMat);
            SceneManager.MoveGameObjectToScene(panel, scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static T Ensure<T>(GameObject go) where T : Component =>
            go.GetComponent<T>() != null ? go.GetComponent<T>() : go.AddComponent<T>();

        private static GameObject CreatePanel(Material panelMat, Material buttonMat, Material keyMat, Material orbMat)
        {
            var root = new GameObject("LobbyPanel");
            root.transform.position = new Vector3(0f, 1.2f, 1.1f); // LazyFollow re-places it at runtime
            root.AddComponent<LazyFollow>();
            var panel = root.AddComponent<LobbyPanel>();

            // Background slab: front face at z ≈ 0, no collider (the pointer should hit buttons, not the panel)
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "Background";
            bg.transform.SetParent(root.transform, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            bg.transform.localScale = new Vector3(0.66f, 0.46f, 0.01f);
            bg.GetComponent<Renderer>().sharedMaterial = panelMat;
            Object.DestroyImmediate(bg.GetComponent<Collider>());

            var title = Label(root.transform, "Title", new Vector3(0f, 0.19f, LabelDepth), new Vector2(0.62f, 0.05f), 0.1f, 0.4f, TextAlignmentOptions.Left);
            var status = Label(root.transform, "Status", new Vector3(0f, 0.135f, LabelDepth), new Vector2(0.62f, 0.07f), 0.08f, 0.3f, TextAlignmentOptions.TopLeft);
            var code = Label(root.transform, "Code", new Vector3(0f, 0.08f, LabelDepth), new Vector2(0.62f, 0.07f), 0.1f, 0.5f, TextAlignmentOptions.Center);
            var players = Label(root.transform, "Players", new Vector3(-0.165f, -0.045f, LabelDepth), new Vector2(0.31f, 0.2f), 0.08f, 0.25f, TextAlignmentOptions.TopLeft);
            var chat = Label(root.transform, "Chat", new Vector3(0.165f, -0.045f, LabelDepth), new Vector2(0.31f, 0.2f), 0.08f, 0.22f, TextAlignmentOptions.TopLeft);

            // Lobby row
            var create = Button(root.transform, "CreateButton", new Vector3(-0.21f, 0.07f, ButtonDepth), new Vector2(0.19f, 0.06f), "Create", buttonMat);
            var quick = Button(root.transform, "QuickJoinButton", new Vector3(0f, 0.07f, ButtonDepth), new Vector2(0.19f, 0.06f), "Quick Join", buttonMat);
            var byCode = Button(root.transform, "JoinCodeButton", new Vector3(0.21f, 0.07f, ButtonDepth), new Vector2(0.19f, 0.06f), "Join by code", buttonMat);

            // Keypad: a template key the panel clones at runtime, plus backspace and join
            var keypad = new GameObject("Keypad");
            keypad.transform.SetParent(root.transform, false);
            var keyTemplate = Button(keypad.transform, "KeyTemplate", new Vector3(-4f * KeyPitch, -0.03f, ButtonDepth), new Vector2(0.044f, 0.044f), "A", keyMat);
            var backspace = Button(keypad.transform, "Backspace", new Vector3(-0.1f, -0.215f, ButtonDepth), new Vector2(0.16f, 0.04f), "Delete", keyMat);
            var joinKey = Button(keypad.transform, "JoinKey", new Vector3(0.1f, -0.215f, ButtonDepth), new Vector2(0.16f, 0.04f), "Join", buttonMat);

            // Session row
            var enter = Button(root.transform, "EnterRoomButton", new Vector3(-0.21f, -0.185f, ButtonDepth), new Vector2(0.19f, 0.06f), "Connecting...", buttonMat);
            enter.gameObject.AddComponent<EnterRoomButton>();
            var mute = Button(root.transform, "MuteButton", new Vector3(0f, -0.185f, ButtonDepth), new Vector2(0.19f, 0.06f), "Mute", buttonMat);
            var leave = Button(root.transform, "LeaveButton", new Vector3(0.21f, -0.185f, ButtonDepth), new Vector2(0.19f, 0.06f), "Leave", buttonMat);

            // Mic orb to the right of the panel, with what it hears underneath
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); // keeps its SphereCollider
            orb.name = "MicOrb";
            orb.transform.SetParent(root.transform, false);
            orb.transform.localPosition = new Vector3(0.41f, 0.02f, 0f);
            orb.transform.localScale = Vector3.one * 0.07f;
            orb.GetComponent<Renderer>().sharedMaterial = orbMat;
            orb.AddComponent<PointerInteractable>();
            orb.AddComponent<MicOrb>();
            orb.AddComponent<VisionOSHoverEffect>();
            var partial = Label(root.transform, "Partial", new Vector3(0.41f, -0.06f, 0f), new Vector2(0.22f, 0.08f), 0.06f, 0.2f, TextAlignmentOptions.Top);

            var so = new SerializedObject(panel);
            so.FindProperty("titleLabel").objectReferenceValue = title;
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.FindProperty("codeLabel").objectReferenceValue = code;
            so.FindProperty("playersLabel").objectReferenceValue = players;
            so.FindProperty("chatLabel").objectReferenceValue = chat;
            so.FindProperty("partialLabel").objectReferenceValue = partial;
            so.FindProperty("createButton").objectReferenceValue = create;
            so.FindProperty("quickJoinButton").objectReferenceValue = quick;
            so.FindProperty("joinCodeButton").objectReferenceValue = byCode;
            so.FindProperty("keypadRoot").objectReferenceValue = keypad;
            so.FindProperty("keyTemplate").objectReferenceValue = keyTemplate;
            so.FindProperty("backspaceKey").objectReferenceValue = backspace;
            so.FindProperty("joinKey").objectReferenceValue = joinKey;
            so.FindProperty("enterRoomButton").objectReferenceValue = enter;
            so.FindProperty("muteButton").objectReferenceValue = mute;
            so.FindProperty("leaveButton").objectReferenceValue = leave;
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // ── Helpers ────────────────────────────────

        private static TextMeshPro Label(Transform parent, string name, Vector3 localPos, Vector2 size, float minFont, float maxFont, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var label = ProjectSetup.CreateLabel(go, size, minFont, maxFont);
            label.alignment = align;
            label.text = "";
            return label;
        }

        private static WorldButton Button(Transform parent, string name, Vector3 localPos, Vector2 size, string text, Material mat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;

            var background = GameObject.CreatePrimitive(PrimitiveType.Cube); // keeps its BoxCollider
            background.name = "Background";
            background.transform.SetParent(root.transform, false);
            background.transform.localScale = new Vector3(size.x, size.y, 0.02f);
            background.GetComponent<Renderer>().sharedMaterial = mat;
            background.AddComponent<VisionOSHoverEffect>();

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.011f);
            var label = ProjectSetup.CreateLabel(labelGo, new Vector2(size.x - 0.01f, size.y - 0.01f), 0.06f, 0.35f);
            label.text = text;

            root.AddComponent<PointerInteractable>();
            var button = root.AddComponent<WorldButton>();
            var so = new SerializedObject(button);
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("background").objectReferenceValue = background.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }
    }
}
