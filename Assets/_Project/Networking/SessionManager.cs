using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MR.Auth;
using MR.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MR.Networking
{
    public enum SessionNetworkMode
    {
        /// <summary>No host: every client owns its own objects; the session survives anyone leaving.</summary>
        DistributedAuthority,
        /// <summary>Fallback: the creator is the Netcode host over Unity Relay (prototype behaviour).</summary>
        RelayClientHost
    }

    /// <summary>
    /// Thin wrapper over Multiplayer Services sessions. A session is the "room":
    /// it replaces the prototype's hand-rolled Relay allocation and server-side room list.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        public const string PlayerNameProperty = "name";
        public const string PlayerPlatformProperty = "platform";
        /// <summary>"1" once the creator's network is up; Quick Join only considers such sessions.</summary>
        public const string ReadyProperty = "ready";

        public static SessionManager Instance { get; private set; }

        public event Action<ISession> Joined;
        public event Action Left;
        public event Action<ISession> PlayersChanged;
        public event Action<string> StatusChanged;

        [SerializeField] private SessionNetworkMode networkMode = SessionNetworkMode.DistributedAuthority;
        [SerializeField, Range(2, 16)] private int maxPlayers = 8;
        [Tooltip("Sessions of a different type are invisible to Quick Join, so test builds don't mix with real ones.")]
        [SerializeField] private string sessionType = "mr-shared-space";

        public ISession ActiveSession { get; private set; }
        public bool IsBusy { get; private set; }
        public string LastStatus { get; private set; } = "";
        public SessionNetworkMode NetworkMode => networkMode;

        private bool _quitting;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Application.quitting += OnApplicationQuitting;
        }

        // Also raised when exiting Play mode in the editor.
        private void OnApplicationQuitting() => _quitting = true;

        private async void OnDestroy()
        {
            Application.quitting -= OnApplicationQuitting;
            if (Instance == this) Instance = null;

            // On quit the Multiplayer SDK leaves the session itself; leaving again here
            // only fails with "lobby not found". Leave explicitly only when this manager
            // is destroyed while the app keeps running.
            if (ActiveSession != null && !_quitting)
            {
                try { await ActiveSession.LeaveAsync(); }
                catch (Exception ex) { Debug.LogWarning($"[Session] Leave on destroy failed: {ex.Message}"); }
            }
        }

        // ── Public API ─────────────────────────────

        public Task<bool> CreateAsync() =>
            RunAsync("Creating session...",
                async () => await MultiplayerService.Instance.CreateSessionAsync(BuildSessionOptions()));

        /// <summary>Joins any open session of this type, or creates one if none exists.</summary>
        /// <summary>
        /// Quick Join with a watchdog. The Distributed Authority allocation can take minutes to
        /// time out inside the SDK (e.g. right after a crash, while the service still holds the
        /// old connection), so an attempt that does not finish in time is abandoned and retried.
        /// </summary>
        public async Task<bool> QuickJoinAsync()
        {
            for (int attempt = 1; attempt <= MaxQuickJoinAttempts; attempt++)
            {
                if (await RunAsync("Looking for a session...", () => WithTimeout(QuickJoinOrReconnectAsync(), SetupTimeout)))
                    return true;
                if (ActiveSession != null) return false; // RunAsync refused for another reason
                Report($"Session setup failed, retrying ({attempt}/{MaxQuickJoinAttempts})...");
                await Task.Delay(2000);
            }
            Report("Could not join a session. Check the connection and restart.");
            return false;
        }

        private const int MaxQuickJoinAttempts = 4;
        private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(30);

        private static async Task<ISession> WithTimeout(Task<ISession> setup, TimeSpan timeout)
        {
            var finished = await Task.WhenAny(setup, Task.Delay(timeout));
            if (finished != setup)
            {
                _ = ObserveAbandonedAsync(setup);
                throw new TimeoutException($"session setup took longer than {timeout.TotalSeconds:0}s");
            }
            return await setup;
        }

        // The SDK task keeps running after we gave up on it. If it ever completes, its lobby is
        // left behind as a stale membership (cleaned on the next start); it is not touched here,
        // because leaving it would also shut down the Netcode instance of the session we did join.
        private static async Task ObserveAbandonedAsync(Task<ISession> setup)
        {
            try
            {
                var late = await setup;
                Debug.LogWarning($"[Session] Abandoned attempt finished late with session {late.Code}; ignoring it.");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Session] Abandoned attempt ended: {ex.Message}");
            }
        }

        private async Task<ISession> QuickJoinOrReconnectAsync()
        {
            await LeaveStaleSessionsAsync();

            Report("Looking for a session...");
            var quickJoin = new QuickJoinOptions
            {
                CreateSession = true,
                Timeout = TimeSpan.FromSeconds(5),
                Filters = new List<FilterOption> { new(FilterField.StringIndex1, "1", FilterOperation.Equal) }
            };
            return await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoin, BuildSessionOptions());
        }

        /// <summary>
        /// A crash or force-quit skips the leave, so the service may still list this player
        /// in a session. Quick Join then cannot put us back into it, and reconnecting to a
        /// session whose network never finished starting gives a session without Netcode.
        /// Removing the stale membership first lets Quick Join find or create a healthy one
        /// (including the same session, if others are still in it).
        /// </summary>
        private async Task LeaveStaleSessionsAsync()
        {
            List<string> joined;
            try { joined = await MultiplayerService.Instance.GetJoinedSessionIdsAsync(); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Session] Could not list joined sessions: {ex.Message}");
                return;
            }

            if (joined.Count == 0) return;

            var lobbies = Unity.Services.Lobbies.LobbyService.Instance;
            string me = AuthManager.Instance.PlayerId;
            foreach (var id in joined)
            {
                try
                {
                    var lobby = await lobbies.GetLobbyAsync(id);
                    if (lobby.HostId == me && lobby.Players.Count <= 1)
                    {
                        // Our own dead session: delete it so Quick Join can't hand it back to us.
                        await lobbies.DeleteLobbyAsync(id);
                        Debug.Log($"[Session] Deleted stale session {id}");
                    }
                    else
                    {
                        await lobbies.RemovePlayerAsync(id, me);
                        Debug.Log($"[Session] Left stale session {id}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Session] Could not leave stale session {id}: {ex.Message}");
                }
            }

            // The lobby query index is eventually consistent; give it a moment before Quick Join.
            await Task.Delay(1500);
        }

        /// <summary>
        /// The session is only advertised to Quick Join once its network is up. A creator
        /// that dies between creating the lobby and starting Netcode would otherwise leave a
        /// joinable session that nobody can actually connect to.
        /// </summary>
        private static async Task MarkReadyIfHostAsync(ISession session)
        {
            if (!session.IsHost) return;
            try
            {
                var host = session.AsHost();
                host.SetProperty(ReadyProperty, new SessionProperty("1", VisibilityPropertyOptions.Public, PropertyIndex.String1));
                await host.SavePropertiesAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Session] Could not mark the session ready: {ex.Message}");
            }
        }

        public Task<bool> JoinByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                Report("Enter a join code.");
                return Task.FromResult(false);
            }

            var options = new JoinSessionOptions
            {
                Type = sessionType,
                PlayerProperties = BuildPlayerProperties()
            };
            return RunAsync($"Joining {code.Trim().ToUpperInvariant()}...",
                () => MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(), options));
        }

        public async Task LeaveAsync()
        {
            var session = ActiveSession;
            if (session == null) return;

            Report("Leaving session...");
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Session] Leave failed: {ex.Message}");
            }
            Detach(session);
        }

        /// <summary>Display name a session member chose, falling back to a shortened PlayerId.</summary>
        public static string NameOf(IReadOnlyPlayer player)
        {
            if (player.Properties != null &&
                player.Properties.TryGetValue(PlayerNameProperty, out var name) &&
                !string.IsNullOrEmpty(name.Value))
                return name.Value;
            return player.Id.Length > 6 ? player.Id[..6] : player.Id;
        }

        public static string PlatformOf(IReadOnlyPlayer player)
        {
            if (player.Properties != null &&
                player.Properties.TryGetValue(PlayerPlatformProperty, out var platform))
                return platform.Value;
            return "?";
        }

        // ── Internals ──────────────────────────────

        private async Task<bool> RunAsync(string status, Func<Task<ISession>> operation)
        {
            if (IsBusy) return false;
            if (ActiveSession != null)
            {
                Report("Already in a session. Leave first.");
                return false;
            }
            if (AuthManager.Instance == null || !AuthManager.Instance.IsSignedIn)
            {
                Report("Not signed in.");
                return false;
            }

            IsBusy = true;
            Report(status);
            try
            {
                var session = await operation();
                Attach(session);
                await MarkReadyIfHostAsync(session);
                Report($"In session {session.Code} ({session.PlayerCount}/{session.MaxPlayers})");
                return true;
            }
            catch (SessionException ex)
            {
                Report($"Session error: {ex.Message}");
                Debug.LogException(ex);
            }
            catch (Exception ex)
            {
                Report($"Error: {ex.Message}");
                Debug.LogException(ex);
            }
            finally
            {
                IsBusy = false;
            }
            return false;
        }

        private SessionOptions BuildSessionOptions()
        {
            var options = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                Type = sessionType,
                PlayerProperties = BuildPlayerProperties(),
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    // Flipped to "1" by MarkReadyIfHostAsync once Netcode is connected.
                    [ReadyProperty] = new("0", VisibilityPropertyOptions.Public, PropertyIndex.String1)
                }
            };

            return networkMode == SessionNetworkMode.DistributedAuthority
                ? options.WithDistributedAuthorityNetwork()
                : options.WithRelayNetwork();
        }

        private static Dictionary<string, PlayerProperty> BuildPlayerProperties() => new()
        {
            [PlayerNameProperty] = new PlayerProperty(AuthManager.Instance.DisplayName, VisibilityPropertyOptions.Member),
            [PlayerPlatformProperty] = new PlayerProperty(PlatformInfo.ShortName(PlatformInfo.Current), VisibilityPropertyOptions.Member)
        };

        private void Attach(ISession session)
        {
            ActiveSession = session;
            session.PlayerJoined += OnPlayerJoined;
            session.PlayerHasLeft += OnPlayerHasLeft;
            session.RemovedFromSession += OnRemovedFromSession;
            session.Deleted += OnRemovedFromSession;
            Joined?.Invoke(session);
        }

        private void Detach(ISession session)
        {
            if (session == null || ActiveSession != session) return;

            session.PlayerJoined -= OnPlayerJoined;
            session.PlayerHasLeft -= OnPlayerHasLeft;
            session.RemovedFromSession -= OnRemovedFromSession;
            session.Deleted -= OnRemovedFromSession;
            ActiveSession = null;
            Report("Not in a session.");
            Left?.Invoke();
        }

        private void OnPlayerJoined(string playerId)
        {
            Report($"In session {ActiveSession.Code} ({ActiveSession.PlayerCount}/{ActiveSession.MaxPlayers})");
            PlayersChanged?.Invoke(ActiveSession);
        }

        private void OnPlayerHasLeft(string playerId)
        {
            Report($"In session {ActiveSession.Code} ({ActiveSession.PlayerCount}/{ActiveSession.MaxPlayers})");
            PlayersChanged?.Invoke(ActiveSession);
        }

        private void OnRemovedFromSession() => Detach(ActiveSession);

        private void Report(string message)
        {
            LastStatus = message;
            Debug.Log($"[Session] {message}");
            StatusChanged?.Invoke(message);
        }
    }
}
