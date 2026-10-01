namespace MR.Core
{
    public enum DevicePlatform : byte
    {
        Editor,
        Quest,
        VisionOS,
        Other
    }

    public static class PlatformInfo
    {
        public static DevicePlatform Current
        {
            get
            {
#if UNITY_EDITOR
                return DevicePlatform.Editor;
#elif UNITY_VISIONOS
                return DevicePlatform.VisionOS;
#elif UNITY_ANDROID
                return DevicePlatform.Quest;
#else
                return DevicePlatform.Other;
#endif
            }
        }

        public static string ShortName(DevicePlatform platform) => platform switch
        {
            DevicePlatform.Editor => "Editor",
            DevicePlatform.Quest => "Quest",
            DevicePlatform.VisionOS => "AVP",
            _ => "Other"
        };
    }
}
