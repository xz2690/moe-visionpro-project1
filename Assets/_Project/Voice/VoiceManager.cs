using System;
using System.Threading.Tasks;
using MR.Auth;
using MR.Core;
using MR.Networking;
using Unity.Services.Multiplayer;
using Unity.Services.Vivox;
using UnityEngine;

namespace MR.Voice
{
    /// <summary>
    /// Joins a Vivox voice channel named after the current session, so everyone in
    /// the same session can talk. Phase 0 uses a plain group channel; positional
    /// audio (Quest + AVP Unbounded) is added in Phase 4.
    /// </summary>
    public class VoiceManager : MonoBehaviour
    {
        public static VoiceManager Instance { get; private set; }

        public event Action<string> StatusChanged;

        [SerializeField] private bool joinWithSession = true;

        private bool _vivoxInitialized;
        private string _channelName;
        private SessionManager _sessions;

        public string LastStatus { get; private set; } = "Voice idle";
        public bool IsInChannel => _channelName != null;
        public bool IsMuted { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            // Vivox's native library cannot load in Multiplayer Play Mode virtual players
            // (EntryPointNotFoundException), and several voice clients on one machine would
            // only echo each other anyway. Voice is tested from the main editor and devices.
            if (EditorInstanceInfo.IsVirtualPlayer)
            {
                Report("Voice off (virtual player)");
                return;
            }

            _sessions = SessionManager.Instance;
            if (_sessions == null) return;
            _sessions.Joined += OnSessionJoined;
            _sessions.Left += OnSessionLeft;
        }

        private void OnDestroy()
        {
            if (_sessions != null)
            {
                _sessions.Joined -= OnSessionJoined;
                _sessions.Left -= OnSessionLeft;
            }
            if (Instance == this) Instance = null;
        }

        public void SetMuted(bool muted)
        {
            if (!_vivoxInitialized) return;
            if (muted) VivoxService.Instance.MuteInputDevice();
            else VivoxService.Instance.UnmuteInputDevice();
            IsMuted = muted;
            Report(muted ? "Mic muted" : "Mic live");
        }

        private async void OnSessionJoined(ISession session)
        {
            if (!joinWithSession) return;

            try
            {
                RequestMicrophonePermission();
                await EnsureLoggedInAsync();

                string channel = $"s_{session.Id}";
                await VivoxService.Instance.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
                _channelName = channel;
                Report("Voice connected");
            }
            catch (Exception ex)
            {
                Report($"Voice unavailable: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        private async void OnSessionLeft()
        {
            if (_channelName == null) return;
            _channelName = null;
            try
            {
                await VivoxService.Instance.LeaveAllChannelsAsync();
                Report("Voice idle");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Voice] Leave failed: {ex.Message}");
            }
        }

        private async Task EnsureLoggedInAsync()
        {
            if (!_vivoxInitialized)
            {
                await UnityServicesInitializer.EnsureInitializedAsync();
                await VivoxService.Instance.InitializeAsync();
                _vivoxInitialized = true;
            }

            if (!VivoxService.Instance.IsLoggedIn)
            {
                Report("Voice signing in...");
                await VivoxService.Instance.LoginAsync(new LoginOptions
                {
                    DisplayName = AuthManager.Instance != null ? AuthManager.Instance.DisplayName : null
                });
            }
        }

        // visionOS shows its own prompt from NSMicrophoneUsageDescription the first time
        // Vivox opens the mic; Android needs an explicit runtime request.
        private static void RequestMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif
        }

        private void Report(string message)
        {
            LastStatus = message;
            Debug.Log($"[Voice] {message}");
            StatusChanged?.Invoke(message);
        }
    }
}
