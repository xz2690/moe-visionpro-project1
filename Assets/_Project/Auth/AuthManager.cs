using System;
using System.Threading.Tasks;
using MR.Core;
using UnityEngine;

namespace MR.Auth
{
    public class AuthManager : MonoBehaviour
    {
        private const string DisplayNameKey = "mr.displayName";

        public static AuthManager Instance { get; private set; }

        public event Action<AuthResult> SignedIn;
        public event Action<string> SignInFailed;
        public event Action SignedOut;

        private IAuthProvider _provider;
        private Task<AuthResult> _pendingSignIn;

        public bool IsSignedIn { get; private set; }
        public string PlayerId { get; private set; }
        public string DisplayName { get; private set; }

        /// <summary>Last name the user chose on this device, or a generated default.</summary>
        public static string SavedDisplayName
        {
            get
            {
#if UNITY_EDITOR
                // Multiplayer Play Mode instances share one PlayerPrefs store, so a saved
                // name would be identical for every player. Name them by player number.
                return $"Editor-P{EditorInstanceInfo.PlayerNumber}";
#else
                string saved = PlayerPrefs.GetString(DisplayNameKey, "");
                if (!string.IsNullOrEmpty(saved)) return saved;
                return $"{PlatformInfo.ShortName(PlatformInfo.Current)}-{UnityEngine.Random.Range(100, 1000)}";
#endif
            }
            set
            {
#if !UNITY_EDITOR
                PlayerPrefs.SetString(DisplayNameKey, value);
                PlayerPrefs.Save();
#endif
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _provider = new AnonymousAuthProvider();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetAuthProvider(IAuthProvider provider) =>
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));

        public Task<AuthResult> SignInAsync(string displayName)
        {
            if (IsSignedIn) return Task.FromResult(AuthResult.Ok(PlayerId, DisplayName));
            // Collapse concurrent requests (e.g. bootstrap + UI) into one.
            return _pendingSignIn ??= SignInInternalAsync(displayName);
        }

        private async Task<AuthResult> SignInInternalAsync(string displayName)
        {
            try
            {
                var result = await _provider.SignInAsync(displayName);
                if (result.Success)
                {
                    IsSignedIn = true;
                    PlayerId = result.PlayerId;
                    DisplayName = result.DisplayName;
                    SavedDisplayName = result.DisplayName;
                    SignedIn?.Invoke(result);
                }
                else
                {
                    SignInFailed?.Invoke(result.ErrorMessage);
                }
                return result;
            }
            finally
            {
                _pendingSignIn = null;
            }
        }

        public void SignOut()
        {
            _provider.SignOut();
            IsSignedIn = false;
            PlayerId = null;
            SignedOut?.Invoke();
        }
    }
}
