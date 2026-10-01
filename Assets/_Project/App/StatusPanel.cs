using System.Text;
using MR.Auth;
using MR.Core;
using MR.Networking;
using MR.Voice;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace MR.App
{
    /// <summary>
    /// World-space diagnostics for Phase 0: who am I, which session, who else is in it,
    /// and the Netcode / ownership state. Readable in a headset without any input.
    /// </summary>
    public class StatusPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshPro label;
        [SerializeField] private float refreshInterval = 0.5f;

        private readonly StringBuilder _sb = new();
        private float _nextRefresh;

        private void Update()
        {
            if (label == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;
            label.text = Build();
        }

        private string Build()
        {
            _sb.Clear();
            var auth = AuthManager.Instance;
            _sb.Append("<b>").Append(PlatformInfo.ShortName(PlatformInfo.Current)).Append("</b>  ");
            _sb.AppendLine(auth != null && auth.IsSignedIn ? auth.DisplayName : "signing in...");

            var sessions = SessionManager.Instance;
            if (sessions != null)
            {
                _sb.AppendLine(sessions.LastStatus);
                var session = sessions.ActiveSession;
                if (session != null)
                {
                    _sb.Append("Code <b>").Append(session.Code).Append("</b>  ")
                       .AppendLine(sessions.NetworkMode.ToString());
                    foreach (var player in session.Players)
                    {
                        _sb.Append(player.Id == session.CurrentPlayer.Id ? "> " : "  ")
                           .Append(SessionManager.NameOf(player)).Append(" (")
                           .Append(SessionManager.PlatformOf(player)).AppendLine(")");
                    }
                }
            }

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsConnectedClient)
            {
                _sb.Append("Client ").Append(nm.LocalClientId)
                   .Append("  rigs ").Append(NetworkRig.All.Count);
                if (nm.LocalClient.IsSessionOwner) _sb.Append("  [session owner]");
                _sb.AppendLine();

                var probe = FindAnyObjectByType<OwnershipRotationProbe>();
                if (probe != null && probe.IsSpawned)
                    _sb.Append("Cube owner ").Append(probe.OwnerClientId)
                       .Append("  handoffs ").Append(probe.Handoffs).AppendLine();
            }

            if (VoiceManager.Instance != null)
                _sb.AppendLine(VoiceManager.Instance.LastStatus);

            return _sb.ToString();
        }
    }
}
