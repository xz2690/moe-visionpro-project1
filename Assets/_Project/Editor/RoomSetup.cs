using System.Linq;
using MR.App;
using MR.Networking;
using MR.XR;
using TMPro;
using Unity.Netcode;
using Unity.PolySpatial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MR.EditorTools
{
    /// <summary>
    /// Generates the shared room: materials, the three table objects (network prefabs),
    /// the RoomController prefab, the Room scene (visuals only, loaded additively) and
    /// the "Enter Room" button in the Main scene. Safe to re-run.
    /// </summary>
    public static class RoomSetup
    {
        private const string Root = "Assets/_Project";
        private const string PrefabDir = Root + "/Prefabs";
        private const string SettingsDir = Root + "/Settings";
        private const string MainScenePath = Root + "/Scenes/Main.unity";
        private const string RoomScenePath = Root + "/Scenes/Room.unity";
        private const string PrefabListPath = SettingsDir + "/NetworkPrefabs.asset";

        // The table sits at the centre of the seat circle (SeatAssigner) and under the probe's orbit.
        private static readonly Vector3 TableCenter = new(0f, 0f, 1.2f);
        private const float TableHeight = 0.76f;

        [MenuItem("Tools/MR/Generate Room Content")]
        public static void Generate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var mats = new RoomMaterials
            {
                Floor = ProjectSetup.CreateLitMaterial("Room_Floor", new Color(0.62f, 0.47f, 0.33f)),
                Wall = ProjectSetup.CreateLitMaterial("Room_Wall", new Color(0.91f, 0.89f, 0.85f)),
                Ceiling = ProjectSetup.CreateLitMaterial("Room_Ceiling", new Color(0.95f, 0.94f, 0.92f)),
                Trim = ProjectSetup.CreateLitMaterial("Room_Trim", new Color(0.29f, 0.25f, 0.21f)),
                TableTop = ProjectSetup.CreateLitMaterial("Room_TableTop", new Color(0.36f, 0.24f, 0.17f)),
                Metal = ProjectSetup.CreateLitMaterial("Room_Metal", new Color(0.23f, 0.23f, 0.25f)),
                Rug = ProjectSetup.CreateLitMaterial("Room_Rug", new Color(0.48f, 0.55f, 0.50f)),
                Shelf = ProjectSetup.CreateLitMaterial("Room_Shelf", new Color(0.65f, 0.49f, 0.32f)),
                Plant = ProjectSetup.CreateLitMaterial("Room_Plant", new Color(0.25f, 0.48f, 0.29f)),
                Pot = ProjectSetup.CreateLitMaterial("Room_Pot", new Color(0.55f, 0.35f, 0.24f)),
                Shade = ProjectSetup.CreateLitMaterial("Room_LampShade", new Color(0.98f, 0.93f, 0.80f)),
                Button = ProjectSetup.CreateLitMaterial("Button", new Color(0.18f, 0.44f, 0.85f)),
                Accents = new[]
                {
                    ProjectSetup.CreateLitMaterial("Room_Accent_Blue", new Color(0.17f, 0.29f, 0.43f)),
                    ProjectSetup.CreateLitMaterial("Room_Accent_Rust", new Color(0.71f, 0.33f, 0.24f)),
                    ProjectSetup.CreateLitMaterial("Room_Accent_Sand", new Color(0.85f, 0.72f, 0.48f)),
                    ProjectSetup.CreateLitMaterial("Room_Accent_Sage", new Color(0.55f, 0.65f, 0.50f)),
                }
            };
            mats.Shade.EnableKeyword("_EMISSION");
            mats.Shade.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mats.Shade.SetColor("_EmissionColor", new Color(1.0f, 0.85f, 0.6f) * 1.5f);

            var interactableMat = ProjectSetup.CreateLitMaterial("Interactable", Color.white);
            var prefabs = new[]
            {
                CreateInteractablePrefab("Interactable_Cube", PrimitiveType.Cube, new Vector3(0.12f, 0.12f, 0.12f), interactableMat),
                CreateInteractablePrefab("Interactable_Sphere", PrimitiveType.Sphere, new Vector3(0.12f, 0.12f, 0.12f), interactableMat),
                CreateInteractablePrefab("Interactable_Cylinder", PrimitiveType.Cylinder, new Vector3(0.12f, 0.03f, 0.12f), interactableMat),
            };
            var spawnPositions = new[]
            {
                TableCenter + new Vector3(-0.4f, TableHeight + 0.06f, 0f),
                TableCenter + new Vector3(0f, TableHeight + 0.06f, 0f),
                TableCenter + new Vector3(0.4f, TableHeight + 0.03f, 0f),
            };
            var roomControllerPrefab = CreateRoomControllerPrefab(prefabs, spawnPositions);

            RegisterNetworkPrefabs(prefabs.Append(roomControllerPrefab).ToArray());
            CreateRoomScene(mats);
            AddSceneToBuild(RoomScenePath);
            WireMainScene(roomControllerPrefab, mats.Button);

            AssetDatabase.SaveAssets();
            Debug.Log("[RoomSetup] Room content generated: Room.unity, table objects, RoomController, Enter Room button.");
        }

        private struct RoomMaterials
        {
            public Material Floor, Wall, Ceiling, Trim, TableTop, Metal, Rug, Shelf, Plant, Pot, Shade, Button;
            public Material[] Accents;
        }

        // ── Prefabs ────────────────────────────────

        private static GameObject CreateInteractablePrefab(string name, PrimitiveType type, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type); // keeps its collider: pointer targeting needs one
            go.name = name;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            // Survive the owner leaving: Distributed Authority hands the object to another
            // client only when DontDestroyWithOwner is set.
            go.AddComponent<NetworkObject>().DontDestroyWithOwner = true;
            ProjectSetup.AddOwnerTransform(go, syncScale: false);
            go.AddComponent<PointerInteractable>();
            var interactable = go.AddComponent<NetworkInteractable>();
            var so = new SerializedObject(interactable);
            so.FindProperty("tintedRenderer").objectReferenceValue = go.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<VisionOSHoverEffect>();

            return ProjectSetup.SavePrefab(go, $"{PrefabDir}/{name}.prefab");
        }

        private static GameObject CreateRoomControllerPrefab(GameObject[] interactables, Vector3[] positions)
        {
            var go = new GameObject("RoomController");
            go.AddComponent<NetworkObject>().DontDestroyWithOwner = true;
            var controller = go.AddComponent<RoomController>();

            var so = new SerializedObject(controller);
            var prefabsProp = so.FindProperty("interactablePrefabs");
            prefabsProp.arraySize = interactables.Length;
            for (int i = 0; i < interactables.Length; i++)
                prefabsProp.GetArrayElementAtIndex(i).objectReferenceValue = interactables[i];
            var posProp = so.FindProperty("spawnPositions");
            posProp.arraySize = positions.Length;
            for (int i = 0; i < positions.Length; i++)
                posProp.GetArrayElementAtIndex(i).vector3Value = positions[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            return ProjectSetup.SavePrefab(go, $"{PrefabDir}/RoomController.prefab");
        }

        internal static void RegisterNetworkPrefabs(GameObject[] prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                Debug.LogError($"[RoomSetup] {PrefabListPath} not found. Run Tools → MR → Generate Phase 0 Spike Content first.");
                return;
            }
            foreach (var prefab in prefabs)
                if (!list.PrefabList.Any(p => p.Prefab == prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ── Room scene ─────────────────────────────

        private static void CreateRoomScene(RoomMaterials m)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var root = new GameObject("Room");
            SceneManager.MoveGameObjectToScene(root, scene);
            var t = root.transform;

            const float size = 12f, height = 3.4f, half = 6f;

            // Shell. Walls and ceiling don't cast shadows, so the Main scene's sun still lights the room.
            Box(t, "Floor", new Vector3(0f, -0.025f, 0f), new Vector3(size, 0.05f, size), m.Floor, castShadows: false);
            Box(t, "Ceiling", new Vector3(0f, height + 0.025f, 0f), new Vector3(size, 0.05f, size), m.Ceiling, castShadows: false);
            Box(t, "Wall_North", new Vector3(0f, height / 2f, half + 0.05f), new Vector3(size + 0.2f, height, 0.1f), m.Wall, castShadows: false);
            Box(t, "Wall_South", new Vector3(0f, height / 2f, -half - 0.05f), new Vector3(size + 0.2f, height, 0.1f), m.Wall, castShadows: false);
            Box(t, "Wall_East", new Vector3(half + 0.05f, height / 2f, 0f), new Vector3(0.1f, height, size), m.Wall, castShadows: false);
            Box(t, "Wall_West", new Vector3(-half - 0.05f, height / 2f, 0f), new Vector3(0.1f, height, size), m.Wall, castShadows: false);
            Box(t, "Trim_North", new Vector3(0f, 0.05f, half - 0.015f), new Vector3(size, 0.1f, 0.03f), m.Trim, collider: false);
            Box(t, "Trim_South", new Vector3(0f, 0.05f, -half + 0.015f), new Vector3(size, 0.1f, 0.03f), m.Trim, collider: false);
            Box(t, "Trim_East", new Vector3(half - 0.015f, 0.05f, 0f), new Vector3(0.03f, 0.1f, size), m.Trim, collider: false);
            Box(t, "Trim_West", new Vector3(-half + 0.015f, 0.05f, 0f), new Vector3(0.03f, 0.1f, size), m.Trim, collider: false);

            // Rug and table at the shared centre.
            var rug = Cylinder(t, "Rug", TableCenter + new Vector3(0f, 0.005f, 0f), new Vector3(3.2f, 0.005f, 3.2f), m.Rug, collider: false);
            rug.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var table = new GameObject("Table");
            table.transform.SetParent(t, false);
            table.transform.position = TableCenter;
            Box(table.transform, "Top", new Vector3(0f, TableHeight - 0.025f, 0f), new Vector3(1.4f, 0.05f, 0.9f), m.TableTop);
            foreach (var (x, z) in new[] { (-0.62f, -0.37f), (0.62f, -0.37f), (-0.62f, 0.37f), (0.62f, 0.37f) })
                Box(table.transform, "Leg", new Vector3(x, (TableHeight - 0.05f) / 2f, z), new Vector3(0.06f, TableHeight - 0.05f, 0.06f), m.Metal, collider: false);

            // Shelf against the north wall, with a few books.
            var shelf = new GameObject("Shelf");
            shelf.transform.SetParent(t, false);
            shelf.transform.position = new Vector3(2.5f, 0f, half - 0.2f);
            Box(shelf.transform, "Side_L", new Vector3(-0.6f, 0.9f, 0f), new Vector3(0.04f, 1.8f, 0.3f), m.Shelf, collider: false);
            Box(shelf.transform, "Side_R", new Vector3(0.6f, 0.9f, 0f), new Vector3(0.04f, 1.8f, 0.3f), m.Shelf, collider: false);
            for (int i = 0; i < 4; i++)
                Box(shelf.transform, "Plank", new Vector3(0f, 0.3f + i * 0.5f, 0f), new Vector3(1.2f, 0.03f, 0.3f), m.Shelf, collider: false);
            var rng = new System.Random(7);
            for (int row = 0; row < 3; row++)
            {
                float x = -0.52f;
                while (x < 0.45f)
                {
                    float w = 0.04f + (float)rng.NextDouble() * 0.04f;
                    float h = 0.18f + (float)rng.NextDouble() * 0.12f;
                    Box(shelf.transform, "Book", new Vector3(x + w / 2f, 0.315f + row * 0.5f + h / 2f, 0f),
                        new Vector3(w, h, 0.2f), m.Accents[rng.Next(m.Accents.Length)], collider: false);
                    x += w + 0.01f;
                    if (rng.NextDouble() < 0.15) x += 0.08f;
                }
            }

            // Pictures on the west wall.
            for (int i = 0; i < 2; i++)
            {
                float z = -1.2f + i * 2.4f;
                Box(t, "Frame", new Vector3(-half + 0.03f, 1.7f, z), new Vector3(0.03f, 0.9f, 1.2f), m.Trim, collider: false);
                Box(t, "Picture", new Vector3(-half + 0.05f, 1.7f, z), new Vector3(0.01f, 0.78f, 1.08f), m.Accents[i], collider: false);
            }

            // Plant and a floor lamp in opposite corners.
            Cylinder(t, "Pot", new Vector3(-4.5f, 0.2f, 4.5f), new Vector3(0.45f, 0.2f, 0.45f), m.Pot, collider: false);
            Sphere(t, "Foliage", new Vector3(-4.5f, 0.95f, 4.5f), new Vector3(1.0f, 1.1f, 1.0f), m.Plant);
            Sphere(t, "Foliage", new Vector3(-4.3f, 1.35f, 4.3f), new Vector3(0.6f, 0.6f, 0.6f), m.Plant);
            Cylinder(t, "LampPole", new Vector3(4.5f, 0.8f, -4.5f), new Vector3(0.04f, 0.8f, 0.04f), m.Metal, collider: false);
            Cylinder(t, "LampBase", new Vector3(4.5f, 0.015f, -4.5f), new Vector3(0.35f, 0.015f, 0.35f), m.Metal, collider: false);
            Cylinder(t, "LampShade", new Vector3(4.5f, 1.75f, -4.5f), new Vector3(0.45f, 0.15f, 0.45f), m.Shade, collider: false);

            PointLight(t, "Lamp Light", new Vector3(4.5f, 1.7f, -4.5f), new Color(1.0f, 0.88f, 0.7f), 6f, 2.5f);
            PointLight(t, "Table Light", TableCenter + new Vector3(0f, 2.6f, 0f), new Color(1.0f, 0.96f, 0.9f), 7f, 2f);

            EditorSceneManager.SaveScene(scene, RoomScenePath);
            EditorSceneManager.CloseScene(scene, true);
        }

        private static void AddSceneToBuild(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ── Main scene ─────────────────────────────

        private static void WireMainScene(GameObject roomControllerPrefab, Material buttonMat)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MainScenePath)
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            GameObject Find(string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

            var bootstrap = Find("Services")?.GetComponent<AppBootstrap>();
            if (bootstrap != null)
            {
                var so = new SerializedObject(bootstrap);
                so.FindProperty("roomControllerPrefab").objectReferenceValue = roomControllerPrefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else Debug.LogError("[RoomSetup] 'Services' with AppBootstrap not found in Main.unity.");

            var origin = Find("XR Origin");
            if (origin != null && origin.GetComponent<PointerInput>() == null)
                origin.AddComponent<PointerInput>();

            var button = Find("EnterRoomButton") ?? CreateEnterRoomButton(buttonMat);
            SceneManager.MoveGameObjectToScene(button, scene);

            var seats = origin != null ? origin.GetComponent<SeatAssigner>() : null;
            if (seats != null)
            {
                var so = new SerializedObject(seats);
                var carry = so.FindProperty("carryAlong");
                bool present = false;
                for (int i = 0; i < carry.arraySize; i++)
                    if (carry.GetArrayElementAtIndex(i).objectReferenceValue == button.transform) present = true;
                if (!present)
                {
                    carry.arraySize++;
                    carry.GetArrayElementAtIndex(carry.arraySize - 1).objectReferenceValue = button.transform;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static GameObject CreateEnterRoomButton(Material buttonMat)
        {
            // Beside the status panel, angled toward the player like the panel is, and
            // below the probe cube's orbit (y 1.1–1.3) so the cube never covers it.
            var root = new GameObject("EnterRoomButton");
            root.transform.position = new Vector3(0.45f, 0.95f, 1.4f);
            root.transform.rotation = Quaternion.Euler(0f, 12f, 0f);

            var background = GameObject.CreatePrimitive(PrimitiveType.Cube); // keeps its BoxCollider
            background.name = "Background";
            background.transform.SetParent(root.transform, false);
            background.transform.localScale = new Vector3(0.36f, 0.12f, 0.02f);
            background.GetComponent<Renderer>().sharedMaterial = buttonMat;
            background.AddComponent<VisionOSHoverEffect>();

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.011f);
            var label = ProjectSetup.CreateLabel(labelGo, new Vector2(0.34f, 0.1f), 0.1f, 0.5f);
            label.text = "Connecting...";

            root.AddComponent<PointerInteractable>();
            var button = root.AddComponent<WorldButton>();
            var so = new SerializedObject(button);
            so.FindProperty("label").objectReferenceValue = label;
            so.FindProperty("background").objectReferenceValue = background.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<EnterRoomButton>();
            return root;
        }

        // ── Primitive helpers ──────────────────────

        private static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat,
            bool collider = true, bool castShadows = true) =>
            Primitive(PrimitiveType.Cube, parent, name, localPos, size, mat, collider, castShadows);

        private static GameObject Cylinder(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat,
            bool collider = true) =>
            Primitive(PrimitiveType.Cylinder, parent, name, localPos, scale, mat, collider, true);

        private static GameObject Sphere(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat) =>
            Primitive(PrimitiveType.Sphere, parent, name, localPos, scale, mat, false, true);

        private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 localPos,
            Vector3 scale, Material mat, bool collider, bool castShadows)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = mat;
            if (!castShadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void PointLight(Transform parent, string name, Vector3 pos, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }
    }
}
