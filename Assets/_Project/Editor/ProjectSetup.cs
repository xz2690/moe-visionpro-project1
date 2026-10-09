using System.IO;
using MR.App;
using MR.Auth;
using MR.Networking;
using MR.Voice;
using MR.XR;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using Unity.PolySpatial;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace MR.EditorTools
{
    /// <summary>
    /// One-click generator for the Phase 0 spike content, in the spirit of the
    /// prototype's LobbySetup: prefabs, materials, network prefab list, the
    /// visionOS volume camera config and the Main scene. Safe to re-run.
    /// </summary>
    public static class ProjectSetup
    {
        private const string Root = "Assets/_Project";
        private const string PrefabDir = Root + "/Prefabs";
        private const string MaterialDir = Root + "/Materials";
        private const string SettingsDir = Root + "/Settings";
        private const string SceneDir = Root + "/Scenes";
        private const string ScenePath = SceneDir + "/Main.unity";

        [MenuItem("Tools/MR/Generate Phase 0 Spike Content")]
        public static void GenerateAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            foreach (var dir in new[] { PrefabDir, MaterialDir, SettingsDir, SceneDir })
                Directory.CreateDirectory(dir);

            var avatarMat = CreateLitMaterial("Avatar", new Color(0.8f, 0.8f, 0.8f));
            var probeMat = CreateLitMaterial("Probe", Color.white);

            var rigPrefab = CreateNetworkRigPrefab(avatarMat);
            var probePrefab = CreateProbePrefab(probeMat);
            var prefabList = CreatePrefabList(probePrefab);
            var unboundedConfig = CreateVolumeConfig("UnboundedVolume", VolumeCamera.PolySpatialVolumeCameraMode.Unbounded);

            CreateMainScene(rigPrefab, probePrefab, prefabList, unboundedConfig);

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Phase 0 spike content generated. Open " + ScenePath);
        }

        // ── Assets ─────────────────────────────────

        private static Material CreateLitMaterial(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject CreateNetworkRigPrefab(Material mat)
        {
            var root = new GameObject("NetworkRig");
            root.AddComponent<NetworkObject>();
            var rig = root.AddComponent<NetworkRig>();

            var head = CreatePart(root.transform, "Head", PrimitiveType.Sphere, new Vector3(0.18f, 0.2f, 0.18f), mat);
            // A small "visor" shows which way the head faces.
            var visor = CreatePart(head.transform, "Visor", PrimitiveType.Cube, new Vector3(0.7f, 0.25f, 0.3f), mat);
            visor.transform.localPosition = new Vector3(0f, 0.08f, 0.38f);

            var left = CreatePart(root.transform, "LeftHand", PrimitiveType.Cube, new Vector3(0.07f, 0.025f, 0.1f), mat);
            var right = CreatePart(root.transform, "RightHand", PrimitiveType.Cube, new Vector3(0.07f, 0.025f, 0.1f), mat);

            foreach (var part in new[] { head, left, right })
                AddOwnerTransform(part, syncScale: false);

            var labelGo = new GameObject("NameLabel");
            labelGo.transform.SetParent(head.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            // The label lives under the scaled head, so undo that scale.
            labelGo.transform.localScale = new Vector3(1f / 0.18f, 1f / 0.2f, 1f / 0.18f);
            var label = CreateLabel(labelGo, new Vector2(0.4f, 0.12f), 0.1f, 0.6f);

            var so = new SerializedObject(rig);
            so.FindProperty("head").objectReferenceValue = head.transform;
            so.FindProperty("leftHand").objectReferenceValue = left.transform;
            so.FindProperty("rightHand").objectReferenceValue = right.transform;
            so.FindProperty("nameLabel").objectReferenceValue = label;
            var tinted = so.FindProperty("tintedRenderers");
            var renderers = new[] { head.GetComponent<Renderer>(), visor.GetComponent<Renderer>(), left.GetComponent<Renderer>(), right.GetComponent<Renderer>() };
            tinted.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++)
                tinted.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, $"{PrefabDir}/NetworkRig.prefab");
        }

        private static GameObject CreateProbePrefab(Material mat)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "OwnershipProbe";
            root.transform.localScale = Vector3.one * 0.2f;
            root.GetComponent<Renderer>().sharedMaterial = mat;
            root.AddComponent<NetworkObject>();
            AddOwnerTransform(root, syncScale: false);
            root.AddComponent<OwnershipRotationProbe>();
            return SavePrefab(root, $"{PrefabDir}/OwnershipProbe.prefab");
        }

        private static NetworkPrefabsList CreatePrefabList(params GameObject[] prefabs)
        {
            string path = $"{SettingsDir}/NetworkPrefabs.asset";
            AssetDatabase.DeleteAsset(path);
            var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            foreach (var prefab in prefabs)
                list.Add(new NetworkPrefab { Prefab = prefab });
            AssetDatabase.CreateAsset(list, path);
            return list;
        }

        private static VolumeCameraWindowConfiguration CreateVolumeConfig(string name, VolumeCamera.PolySpatialVolumeCameraMode mode)
        {
            string path = $"{SettingsDir}/{name}.asset";
            var config = AssetDatabase.LoadAssetAtPath<VolumeCameraWindowConfiguration>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<VolumeCameraWindowConfiguration>();
                AssetDatabase.CreateAsset(config, path);
            }
            // Mode has no public setter; it is meant to be edited in the inspector.
            var so = new SerializedObject(config);
            so.FindProperty("m_Mode").enumValueIndex = (int)mode;
            so.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        // ── Scene ──────────────────────────────────

        private static void CreateMainScene(GameObject rigPrefab, GameObject probePrefab,
            NetworkPrefabsList prefabList, VolumeCameraWindowConfiguration volumeConfig)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            new GameObject("AR Session").AddComponent<ARSession>();
            var cameraGo = CreateXROrigin();

            var volumeGo = new GameObject("Volume Camera");
            volumeGo.AddComponent<VolumeCamera>().WindowConfiguration = volumeConfig;

            // Networking
            var netGo = new GameObject("NetworkManager");
            var netManager = netGo.AddComponent<NetworkManager>();
            var transport = netGo.AddComponent<UnityTransport>();
            netManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = rigPrefab,
                // The session's network handler switches this to DistributedAuthority on start.
                NetworkTopology = NetworkTopologyTypes.DistributedAuthority
            };
            netManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);

            // App services
            var servicesGo = new GameObject("Services");
            servicesGo.AddComponent<AuthManager>();
            servicesGo.AddComponent<SessionManager>();
            servicesGo.AddComponent<VoiceManager>();
            var bootstrap = servicesGo.AddComponent<AppBootstrap>();
            var bootstrapSo = new SerializedObject(bootstrap);
            bootstrapSo.FindProperty("probePrefab").objectReferenceValue = probePrefab;
            bootstrapSo.ApplyModifiedPropertiesWithoutUndo();

            // Status panel, 1.4 m in front of the starting position at eye height.
            var panelGo = new GameObject("StatusPanel");
            panelGo.transform.position = new Vector3(-0.5f, 1.45f, 1.4f);
            panelGo.transform.rotation = Quaternion.Euler(0f, -15f, 0f);
            var panelLabel = CreateLabel(panelGo, new Vector2(0.7f, 0.45f), 0.1f, 0.4f);
            panelLabel.alignment = TextAlignmentOptions.TopLeft;
            var panel = panelGo.AddComponent<StatusPanel>();
            var panelSo = new SerializedObject(panel);
            panelSo.FindProperty("label").objectReferenceValue = panelLabel;
            panelSo.ApplyModifiedPropertiesWithoutUndo();

            // Each player gets their own seat around the shared content. The Volume Camera
            // (visionOS) and this player's status panel move together with the XR Origin.
            var originGo = cameraGo.transform.root.gameObject;
            var seats = originGo.AddComponent<SeatAssigner>();
            var seatSo = new SerializedObject(seats);
            seatSo.FindProperty("origin").objectReferenceValue = originGo.transform;
            var carry = seatSo.FindProperty("carryAlong");
            carry.arraySize = 2;
            carry.GetArrayElementAtIndex(0).objectReferenceValue = volumeGo.transform;
            carry.GetArrayElementAtIndex(1).objectReferenceValue = panelGo.transform;
            seatSo.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = cameraGo;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static GameObject CreateXROrigin()
        {
            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();
            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(originGo.transform, false);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraGo.transform.SetParent(offsetGo.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.6f, 0f); // editor eye height; overwritten by tracking
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            // Transparent clear color lets Quest passthrough show through.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<ARCameraManager>();
            cameraGo.AddComponent<EditorFlyCamera>();

            var poseDriver = cameraGo.AddComponent<TrackedPoseDriver>();
            poseDriver.positionInput = new InputActionProperty(new InputAction("Head Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            poseDriver.rotationInput = new InputActionProperty(new InputAction("Head Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            poseDriver.trackingStateInput = new InputActionProperty(new InputAction("Head Tracking State", binding: "<XRHMD>/trackingState", expectedControlType: "Integer"));

            origin.Camera = camera;
            origin.CameraFloorOffsetObject = offsetGo;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            var hardwareRig = originGo.AddComponent<HardwareRig>();
            var so = new SerializedObject(hardwareRig);
            so.FindProperty("head").objectReferenceValue = cameraGo.transform;
            so.FindProperty("trackingSpace").objectReferenceValue = offsetGo.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            return cameraGo;
        }

        // ── Helpers ────────────────────────────────

        private static GameObject CreatePart(Transform parent, string name, PrimitiveType type, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void AddOwnerTransform(GameObject go, bool syncScale)
        {
            var nt = go.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = syncScale;
            nt.UseQuaternionCompression = true;
            nt.UseHalfFloatPrecision = true;
        }

        private static TextMeshPro CreateLabel(GameObject go, Vector2 size, float minFont, float maxFont)
        {
            var label = go.AddComponent<TextMeshPro>();
            label.rectTransform.sizeDelta = size;
            label.enableAutoSizing = true;
            label.fontSizeMin = minFont;
            label.fontSizeMax = maxFont;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.text = "...";
            return label;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
