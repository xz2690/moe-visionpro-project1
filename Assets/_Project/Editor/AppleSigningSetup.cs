using UnityEditor;
using UnityEngine;

namespace MR.EditorTools
{
    /// <summary>
    /// Applies the Player Settings a visionOS build needs to install and run:
    /// - Signing team + automatic signing, so every visionOS build produces an
    ///   Xcode project that is already signed (Unity's Replace build otherwise
    ///   resets the team each time).
    /// - Microphone usage description. Vivox opens the mic on login; without
    ///   NSMicrophoneUsageDescription visionOS kills the app (TCC privacy violation).
    /// These fields are shared by iOS, tvOS and visionOS. Hand tracking and world
    /// sensing descriptions live in the Apple visionOS XR settings instead.
    /// Teammates on a different Apple team: change TeamId or set it in
    /// Player Settings → visionOS → Other Settings → Signing Team ID.
    /// </summary>
    public static class AppleSigningSetup
    {
        // "Metaverse Education (Personal Team)" from Xcode → Settings → Accounts.
        private const string TeamId = "GCPXYQPT4W";
        private const string MicrophoneUsage = "Used for voice chat with other participants.";

        [MenuItem("Tools/MR/Apply visionOS Player Settings")]
        public static void Apply()
        {
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.appleDeveloperTeamID = TeamId;

            PlayerSettings.iOS.microphoneUsageDescription = MicrophoneUsage;
            // The app never uses the camera; this field previously held the mic text by mistake.
            PlayerSettings.iOS.cameraUsageDescription = "";

            AssetDatabase.SaveAssets();

            Debug.Log($"[visionOS Setup] Automatic signing on, team {PlayerSettings.iOS.appleDeveloperTeamID}; " +
                      "microphone usage description set. Rebuild visionOS (Replace) to regenerate the Xcode project.");
        }
    }
}
