using MR.Auth;
using MR.Networking;
using Unity.Netcode;
using UnityEngine;

namespace MR.App
{
    /// <summary>
    /// Phase 0 entry point: sign in anonymously, quick-join the shared session
    /// (creating it if nobody is there yet) and, as session owner, spawn the
    /// ownership test cube. Needs no input, so it runs the same on every device.
    /// </summary>
    public class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private bool autoQuickJoin = true;
        [Tooltip("Spawned once per session by the session owner.")]
        [SerializeField] private GameObject probePrefab;

        private NetworkManager _netManager;

        private async void Start()
        {
            _netManager = NetworkManager.Singleton;
            if (_netManager != null)
                _netManager.OnClientConnectedCallback += OnClientConnected;

            var auth = AuthManager.Instance;
            var result = await auth.SignInAsync(AuthManager.SavedDisplayName);
            if (!result.Success)
            {
                Debug.LogError($"[Bootstrap] {result.ErrorMessage}");
                return;
            }

            Debug.Log($"[Bootstrap] Signed in as {result.DisplayName} ({result.PlayerId})");
            if (autoQuickJoin)
                await SessionManager.Instance.QuickJoinAsync();
        }

        private void OnDestroy()
        {
            if (_netManager != null)
                _netManager.OnClientConnectedCallback -= OnClientConnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (clientId != _netManager.LocalClientId) return;
            if (probePrefab == null || FindAnyObjectByType<OwnershipRotationProbe>() != null) return;

            // Under Distributed Authority the session owner is the first player in.
            // Under the Relay fallback the host plays that role.
            bool isSessionOwner = _netManager.LocalClient.IsSessionOwner || _netManager.IsHost;
            if (!isSessionOwner) return;

            NetworkObject.InstantiateAndSpawn(probePrefab, _netManager, _netManager.LocalClientId);
        }
    }
}
