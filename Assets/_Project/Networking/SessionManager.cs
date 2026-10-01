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

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private async void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (ActiveSession != null)
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
        public Task<bool> QuickJoinAsync() =>
            RunAsync("Looking for a session...",
                () => MultiplayerService.Instance.MatchmakeSessionAsync(
                    new QuickJoinOptions { CreateSession = true }, BuildSessionOptions()));

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
                PlayerProperties = BuildPlayerProperties()
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
