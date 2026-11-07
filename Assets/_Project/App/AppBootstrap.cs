using MR.Auth;
using MR.Core;
using MR.Networking;
using Unity.Netcode;
using System.Threading.Tasks;
using UnityEngine;

namespace MR.App
{
    /// <summary>
    /// Entry point: sign in anonymously, then either show the lobby panel or (for tests)
    /// quick-join straight away. As session owner it spawns the per-session objects:
    /// the ownership test cube, the RoomController and the SessionChat.
    /// </summary>
    public class AppBootstrap : MonoBehaviour
    {
        private const string AutoJoinEditorPref = "MR.AutoQuickJoin";

        [Tooltip("Skip the lobby and quick-join at start. Tests force it with -autojoin or MR_AUTOJOIN=1; " +
                 "in the editor Tools → MR → Auto Quick Join In Editor controls it.")]
        [SerializeField] private bool autoQuickJoin = false;
        [Tooltip("Spawned once per session by the session owner.")]
        [SerializeField] private GameObject probePrefab;
        [Tooltip("Spawned once per session by the session owner; owns the shared room state.")]
        [SerializeField] private GameObject roomControllerPrefab;
        [Tooltip("Spawned once per session by the session owner; session-wide subtitles and chat.")]
        [SerializeField] private GameObject sessionChatPrefab;

        /// <summary>Editor-only switch so the automated tests keep joining without touching the panel.</summary>
        public static bool AutoJoinInEditor
        {
#if UNITY_EDITOR
            get => UnityEditor.EditorPrefs.GetBool(AutoJoinEditorPref, true);
            set => UnityEditor.EditorPrefs.SetBool(AutoJoinEditorPref, value);
#else
            get => false;
            set { }
#endif
        }

        private bool ShouldAutoJoin()
        {
            if (autoQuickJoin || LaunchArgs.Flag("autojoin")) return true;
#if UNITY_EDITOR
            return AutoJoinInEditor;
#else
            return false;
#endif
        }

        private NetworkManager _netManager;

        private async void Start()
        {
            _netManager = NetworkManager.Singleton;
            if (_netManager != null)
            {
                _netManager.OnClientConnectedCallback += OnClientConnected;
                _netManager.OnSessionOwnerPromoted += OnSessionOwnerPromoted;
            }

            var auth = AuthManager.Instance;
            var result = await auth.SignInAsync(AuthManager.SavedDisplayName);
            if (!result.Success)
            {
                Debug.LogError($"[Bootstrap] {result.ErrorMessage}");
                return;
            }

            Debug.Log($"[Bootstrap] Signed in as {result.DisplayName} ({result.PlayerId})");
            if (ShouldAutoJoin())
            {
                // Instances that start at the same instant (MPPM virtual players) would each
                // create a session before any is "ready"; stagger them so they meet in one.
                int stagger = EditorInstanceInfo.PlayerNumber - 1;
                if (stagger > 0) await Task.Delay(stagger * 4000);
                await SessionManager.Instance.QuickJoinAsync();
            }
        }

        private void OnDestroy()
        {
            if (_netManager != null)
            {
                _netManager.OnClientConnectedCallback -= OnClientConnected;
                _netManager.OnSessionOwnerPromoted -= OnSessionOwnerPromoted;
            }
        }

        // When the previous session owner leaves, the session objects it owned are
        // redistributed (DontDestroyWithOwner). This covers the case where one is
        // missing anyway, e.g. it was never spawned or an older build destroyed it.
        private void OnSessionOwnerPromoted(ulong promotedClientId)
        {
            if (promotedClientId != _netManager.LocalClientId) return;
            // Can fire during connection approval, before spawning is allowed.
            StartCoroutine(SpawnSessionObjectsWhenReady());
        }

        private void OnClientConnected(ulong clientId)
        {
            if (clientId != _netManager.LocalClientId) return;
            StartCoroutine(SpawnSessionObjectsWhenReady());
        }

        // Under Distributed Authority the connected callback can arrive a frame before the
        // client is approved, and spawning before that is rejected ("Cannot spawn ... until
        // the client is fully connected"). Wait for approval, then spawn as session owner.
        private System.Collections.IEnumerator SpawnSessionObjectsWhenReady()
        {
            float deadline = Time.time + 10f;
            while (Time.time < deadline &&
                   (_netManager.LocalClient == null || !_netManager.IsConnectedClient || !_netManager.IsApproved))
                yield return null;

            if (!_netManager.IsConnectedClient) yield break;
            yield return null; // let Netcode finish the approval frame before spawning

            // The session owner is the first player in; under the Relay fallback the host plays that role.
            bool isSessionOwner = _netManager.LocalClient.IsSessionOwner || _netManager.IsHost;
            if (!isSessionOwner) yield break;

            SpawnIfMissing<OwnershipRotationProbe>(probePrefab);
            SpawnIfMissing<RoomController>(roomControllerPrefab);
            SpawnIfMissing<SessionChat>(sessionChatPrefab);
        }

        private void SpawnIfMissing<T>(GameObject prefab) where T : NetworkBehaviour
        {
            if (prefab == null) return;
            var existing = FindAnyObjectByType<T>();
            if (existing != null)
            {
                if (existing.IsSpawned) return;
                // A spawn that Netcode rejected leaves an unspawned instance behind; clear it.
                Destroy(existing.gameObject);
            }
            NetworkObject.InstantiateAndSpawn(prefab, _netManager, _netManager.LocalClientId);
        }
    }
}
