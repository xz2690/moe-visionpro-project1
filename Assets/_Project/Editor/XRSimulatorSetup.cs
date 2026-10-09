using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace MR.EditorTools
{
    /// <summary>
    /// Lets the desktop editor run the app through an OpenXR runtime, which is how
    /// Quest is tested without a headset: on Windows, the Meta XR Simulator
    /// (package com.meta.xr.simulator) shows up as an option in
    /// Project Settings → XR Plug-in Management → OpenXR → Play Mode OpenXR Runtime.
    ///
    /// Configures the Standalone platform only. Quest builds use the Android
    /// settings, which already have the Meta Quest features enabled. Without an
    /// OpenXR runtime (e.g. on macOS) XR simply doesn't start and the editor keeps
    /// the keyboard/mouse fallback (EditorFlyCamera, simulated hands).
    /// </summary>
    public static class XRSimulatorSetup
    {
        private const BuildTargetGroup Standalone = BuildTargetGroup.Standalone;
        private const string XRSettingsKey = "com.unity.xr.management.loader_settings";

        // Same input as on Quest: Touch / Touch Plus controllers and bare hands.
        private static readonly HashSet<string> Features = new()
        {
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile",
            "HandInteractionProfile",
            "HandTracking",
            "MetaHandTrackingAim",
        };

        [MenuItem("Tools/MR/Enable OpenXR in Editor (Meta XR Simulator)")]
        public static void Enable()
        {
            EditorBuildSettings.TryGetConfigObject(XRSettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                Debug.LogError("[XR Simulator Setup] XR Plug-in Management is not set up in this project.");
                return;
            }

            if (!perTarget.HasSettingsForBuildTarget(Standalone)) perTarget.CreateDefaultSettingsForBuildTarget(Standalone);
            if (!perTarget.HasManagerSettingsForBuildTarget(Standalone)) perTarget.CreateDefaultManagerSettingsForBuildTarget(Standalone);
            perTarget.SettingsForBuildTarget(Standalone).InitManagerOnStart = true;

            var manager = perTarget.ManagerSettingsForBuildTarget(Standalone);
            XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", Standalone);

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(Standalone);
            var enabled = new List<string>();
            foreach (var feature in settings.GetFeatures())
            {
                if (!Features.Contains(feature.GetType().Name)) continue;
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                enabled.Add(feature.GetType().Name);
            }

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();

            Debug.Log("[XR Simulator Setup] Standalone: OpenXR loader on, features: " + string.Join(", ", enabled) +
                      ". Now pick the runtime under OpenXR → Play Mode OpenXR Runtime.");
        }

        [MenuItem("Tools/MR/Disable OpenXR in Editor")]
        public static void Disable()
        {
            EditorBuildSettings.TryGetConfigObject(XRSettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            var manager = perTarget != null ? perTarget.ManagerSettingsForBuildTarget(Standalone) : null;
            if (manager == null) return;

            XRPackageMetadataStore.RemoveLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", Standalone);
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();
            Debug.Log("[XR Simulator Setup] Standalone OpenXR loader removed: the editor uses keyboard/mouse again.");
        }
    }
}
