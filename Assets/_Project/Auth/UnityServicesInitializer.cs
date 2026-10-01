using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace MR.Auth
{
    public static class UnityServicesInitializer
    {
        private static Task _initTask;

        /// <summary>Safe to call from several systems at once; initializes Unity Services exactly once.</summary>
        public static Task EnsureInitializedAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized) return Task.CompletedTask;
            return _initTask ??= InitializeAsync();
        }

        private static async Task InitializeAsync()
        {
            var options = new InitializationOptions();
            string profile = ProfileForThisInstance();
            if (!string.IsNullOrEmpty(profile))
                options.SetProfile(profile);

            try
            {
                await UnityServices.InitializeAsync(options);
            }
            catch
            {
                _initTask = null; // allow a retry
                throw;
            }
        }

        /// <summary>
        /// Multiplayer Play Mode virtual players share the editor's cached anonymous
        /// session, which would make them all the same PlayerId. Each virtual player
        /// runs from its own project copy, so the data path gives a unique profile.
        /// </summary>
        private static string ProfileForThisInstance()
        {
#if UNITY_EDITOR
            uint hash = 2166136261;
            foreach (char c in Application.dataPath)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return $"editor_{hash:x8}";
#else
            return null;
#endif
        }
    }
}
