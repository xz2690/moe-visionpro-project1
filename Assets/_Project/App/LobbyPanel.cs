using System.Collections.Generic;
using System.Text;
using MR.Auth;
using MR.Core;
using MR.Networking;
using MR.Voice;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MR.App
{
    /// <summary>
    /// The world-space lobby: before a session it offers Create / Quick Join / Join by code
    /// (with an on-panel keypad); inside one it shows the code, who is here (platform,
    /// speaking, muted), the session chat and the Enter Room / Mute / Leave buttons. The mic
    /// orb beside it drives speech commands. Everything is driven by the unified pointer,
    /// so it works with gaze + pinch, controllers, hands and the mouse.
    /// </summary>
    public class LobbyPanel : MonoBehaviour
    {
        private const string KeypadChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const int KeypadColumns = 9;
        private const float KeyPitch = 0.048f;
        private const float RefreshInterval = 0.4f;

        [Header("Labels")]
        [SerializeField] private TextMeshPro titleLabel;
        [SerializeField] private TextMeshPro statusLabel;
        [SerializeField] private TextMeshPro codeLabel;
        [SerializeField] private TextMeshPro playersLabel;
        [SerializeField] private TextMeshPro chatLabel;
        [SerializeField] private TextMeshPro partialLabel;

        [Header("Lobby buttons")]
        [SerializeField] private WorldButton createButton;
        [SerializeField] private WorldButton quickJoinButton;
        [SerializeField] private WorldButton joinCodeButton;

        [Header("Keypad")]
        [SerializeField] private GameObject keypadRoot;
        [SerializeField] private WorldButton keyTemplate;
        [SerializeField] private WorldButton backspaceKey;
        [SerializeField] private WorldButton joinKey;

        [Header("Session buttons")]
        [SerializeField] private WorldButton enterRoomButton;
        [SerializeField] private WorldButton muteButton;
        [SerializeField] private WorldButton leaveButton;

        private readonly StringBuilder _sb = new();
        private readonly List<WorldButton> _keys = new();
        private string _typedCode = "";
        private bool _keypadOpen;
        private float _nextRefresh;

        private void Start()
        {
            BuildKeypad();

            createButton.Clicked += () => LobbyController.Instance?.CreateSession();
            quickJoinButton.Clicked += () => LobbyController.Instance?.QuickJoin();
            joinCodeButton.Clicked += () => { _keypadOpen = !_keypadOpen; Refresh(); };
            backspaceKey.Clicked += () => { if (_typedCode.Length > 0) _typedCode = _typedCode[..^1]; Refresh(); };
            joinKey.Clicked += () =>
            {
                if (_typedCode.Length < VoiceCommands.CodeLength) return;
                LobbyController.Instance?.JoinCode(_typedCode);
                _typedCode = "";
                _keypadOpen = false;
                Refresh();
            };
            muteButton.Clicked += () => LobbyController.Instance?.ToggleMute();
            leaveButton.Clicked += () => LobbyController.Instance?.LeaveSession();

            if (LobbyController.Instance != null) LobbyController.Instance.Changed += Refresh;
            if (VoiceController.Instance != null) VoiceController.Instance.Changed += Refresh;
            if (VoiceManager.Instance != null) VoiceManager.Instance.ParticipantsChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (LobbyController.Instance != null) LobbyController.Instance.Changed -= Refresh;
            if (VoiceController.Instance != null) VoiceController.Instance.Changed -= Refresh;
            if (VoiceManager.Instance != null) VoiceManager.Instance.ParticipantsChanged -= Refresh;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void BuildKeypad()
        {
            if (keyTemplate == null || keypadRoot == null) return;
            keyTemplate.gameObject.SetActive(false);
            for (int i = 0; i < KeypadChars.Length; i++)
            {
                char c = KeypadChars[i];
                var key = Instantiate(keyTemplate, keypadRoot.transform);
                key.name = "Key_" + c;
                key.gameObject.SetActive(true);
                key.transform.localPosition = keyTemplate.transform.localPosition +
                    new Vector3((i % KeypadColumns) * KeyPitch, -(i / KeypadColumns) * KeyPitch, 0f);
                key.Text = c.ToString();
                key.Clicked += () =>
                {
                    if (_typedCode.Length < VoiceCommands.CodeLength) _typedCode += c;
                    Refresh();
                };
                _keys.Add(key);
            }
        }

        private void Refresh()
        {
            var sessions = SessionManager.Instance;
            var session = sessions?.ActiveSession;
            var lobby = LobbyController.Instance;
            var voice = VoiceController.Instance;
            var chat = VoiceManager.Instance;
            bool inSession = session != null;
            bool busy = (sessions != null && sessions.IsBusy) || (lobby != null && lobby.Busy);

            // Which half is visible
            createButton.gameObject.SetActive(!inSession);
            quickJoinButton.gameObject.SetActive(!inSession);
            joinCodeButton.gameObject.SetActive(!inSession);
            keypadRoot.SetActive(!inSession && _keypadOpen);
            codeLabel.gameObject.SetActive(inSession || (_keypadOpen && !inSession));
            playersLabel.gameObject.SetActive(inSession);
            chatLabel.gameObject.SetActive(inSession);
            enterRoomButton.gameObject.SetActive(inSession);
            muteButton.gameObject.SetActive(inSession);
            leaveButton.gameObject.SetActive(inSession);

            bool signedIn = AuthManager.Instance != null && AuthManager.Instance.IsSignedIn;
            createButton.Interactable = quickJoinButton.Interactable = joinCodeButton.Interactable = !busy && signedIn;
            joinKey.Interactable = !busy && _typedCode.Length == VoiceCommands.CodeLength;
            leaveButton.Interactable = !busy;
            joinCodeButton.Text = _keypadOpen ? "Close keypad" : "Join by code";

            string name = AuthManager.Instance != null && AuthManager.Instance.IsSignedIn ? AuthManager.Instance.DisplayName : "signing in...";
            titleLabel.text = $"<b>MR Lobby</b>   {name}  ({PlatformInfo.ShortName(PlatformInfo.Current)})";

            // Status: the lobby's last reply, else the session / voice state
            string status = lobby != null && !string.IsNullOrEmpty(lobby.Status) ? lobby.Status : sessions?.LastStatus ?? "";
            if (voice != null && voice.Phase == VoicePhase.Unavailable && !string.IsNullOrEmpty(voice.Status))
                status += $"\n<size=70%>Voice commands: {voice.Status}</size>";
            statusLabel.text = status;

            if (inSession)
            {
                codeLabel.text = $"Session <b>{session.Code}</b>   <size=60%>{session.PlayerCount}/{session.MaxPlayers}</size>";
                playersLabel.text = BuildPlayers(session, chat);
                chatLabel.text = BuildChat(lobby);
                muteButton.Text = chat != null && chat.IsMuted ? "Unmute" : "Mute";
                muteButton.Interactable = chat != null && chat.IsInChannel && !busy;
            }
            else if (_keypadOpen)
            {
                codeLabel.text = "Code: <b>" + _typedCode.PadRight(VoiceCommands.CodeLength, '_') + "</b>";
            }

            // What the recognizer hears right now
            if (voice == null) partialLabel.text = "";
            else if (voice.Phase == VoicePhase.Listening) partialLabel.text = string.IsNullOrEmpty(voice.Partial) ? "Listening..." : voice.Partial;
            else if (voice.Phase == VoicePhase.Processing) partialLabel.text = "...";
            else partialLabel.text = $"<size=70%>Tap the orb and speak  ({voice.RecognizerName})</size>";
        }

        private string BuildPlayers(ISession session, VoiceManager chat)
        {
            _sb.Clear();
            string me = session.CurrentPlayer?.Id;
            foreach (var player in session.Players)
            {
                bool self = player.Id == me;
                _sb.Append(self ? "> " : "  ").Append(SessionManager.NameOf(player))
                   .Append(" <size=70%>").Append(SessionManager.PlatformOf(player)).Append("</size>");
                if (chat != null)
                {
                    if (chat.IsSpeaking(player.Id)) _sb.Append("  <color=#4ade80>speaking</color>");
                    else if (self ? chat.IsMuted : chat.IsParticipantMuted(player.Id)) _sb.Append("  <color=#f87171>muted</color>");
                    else if (!chat.IsInVoice(player.Id)) _sb.Append("  <color=#9ca3af>no voice</color>");
                }
                _sb.AppendLine();
            }
            return _sb.ToString();
        }

        private string BuildChat(LobbyController lobby)
        {
            if (lobby == null) return "";
            _sb.Clear();
            foreach (var line in lobby.Chat)
            {
                if (line.Who == null) _sb.Append("<color=#9ca3af>").Append(line.Text).AppendLine("</color>");
                else _sb.Append("<b>").Append(line.Who).Append("</b>: ").AppendLine(line.Text);
            }
            return _sb.ToString();
        }
    }
}
