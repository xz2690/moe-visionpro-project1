using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MR.Auth;
using MR.Networking;
using MR.Voice;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MR.App
{
    public struct ChatLine
    {
        public string Who;   // display name, or null for a system line
        public string Text;
        public bool FromSelf;
    }

    /// <summary>
    /// The lobby's brain: runs commands from the panel buttons and from speech
    /// (VoiceCommands), reports back on the panel and through text-to-speech, and keeps
    /// the session chat (subtitles of what people said, join / leave lines).
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        private const int MaxChatLines = 8;

        public static LobbyController Instance { get; private set; }

        public IReadOnlyList<ChatLine> Chat => _chat;
        public string Status { get; private set; } = "";
        public bool Busy { get; private set; }
        public event Action Changed;

        private readonly List<ChatLine> _chat = new();
        private readonly HashSet<string> _knownPlayers = new();
        private VoiceController _voice;
        private SessionManager _sessions;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            _voice = VoiceController.Instance != null ? VoiceController.Instance : GetComponent<VoiceController>();
            if (_voice != null) _voice.Transcript += OnTranscript;
            _sessions = SessionManager.Instance != null ? SessionManager.Instance : GetComponent<SessionManager>();
            if (_sessions != null)
            {
                _sessions.Joined += OnSessionJoined;
                _sessions.Left += OnSessionLeft;
                _sessions.PlayersChanged += OnPlayersChanged;
                _sessions.StatusChanged += OnSessionStatus;
            }
            SessionChat.MessageReceived += OnChatMessage;
        }

        private void OnDisable()
        {
            if (_voice != null) _voice.Transcript -= OnTranscript;
            if (_sessions != null)
            {
                _sessions.Joined -= OnSessionJoined;
                _sessions.Left -= OnSessionLeft;
                _sessions.PlayersChanged -= OnPlayersChanged;
                _sessions.StatusChanged -= OnSessionStatus;
            }
            SessionChat.MessageReceived -= OnChatMessage;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Buttons ────────────────────────────────

        public void CreateSession() => _ = Execute(new VoiceCommand(CommandKind.CreateSession), spoken: false);
        public void QuickJoin() => _ = Execute(new VoiceCommand(CommandKind.QuickJoin), spoken: false);
        public void JoinCode(string code) => _ = Execute(new VoiceCommand(CommandKind.JoinCode, code?.Trim().ToUpperInvariant()), spoken: false);
        public void LeaveSession() => _ = Execute(new VoiceCommand(CommandKind.LeaveSession), spoken: false);
        public void ToggleMute()
        {
            var chat = VoiceManager.Instance;
            _ = Execute(new VoiceCommand(chat != null && chat.IsMuted ? CommandKind.Unmute : CommandKind.Mute), spoken: false);
        }

        // ── Speech ─────────────────────────────────

        private void OnTranscript(string text)
        {
            var command = VoiceCommands.Parse(text);
            if (command.Kind == CommandKind.None)
            {
                // Not a command: show it to everyone as a subtitle.
                Broadcast(text);
                _voice.Done();
                return;
            }
            if (command.Kind == CommandKind.JoinCode && command.Code == null)
            {
                _voice.Say(command.Problem ?? "Please spell the session code, letter by letter.");
                return;
            }
            _ = Execute(command, spoken: true);
        }

        private async Task Execute(VoiceCommand command, bool spoken)
        {
            if (Busy)
            {
                Reply("One moment, still working on the last request.", spoken);
                return;
            }

            Busy = true;
            Changed?.Invoke();
            try
            {
                string reply = await Run(command);
                Reply(reply, spoken);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Reply("Sorry, that failed: " + ex.Message, spoken);
            }
            finally
            {
                Busy = false;
                Changed?.Invoke();
            }
        }

        private async Task<string> Run(VoiceCommand command)
        {
            var sessions = _sessions;
            var room = RoomController.Instance;
            var chat = VoiceManager.Instance;
            bool inSession = sessions != null && sessions.ActiveSession != null;

            switch (command.Kind)
            {
                case CommandKind.CreateSession:
                case CommandKind.QuickJoin:
                case CommandKind.JoinCode:
                    if (!inSession && !await WaitForSignInAsync())
                        return "Still signing in, please try again in a moment.";
                    break;
            }

            switch (command.Kind)
            {
                case CommandKind.CreateSession:
                    if (inSession) return $"You are already in session {VoiceCommands.Spell(sessions.ActiveSession.Code)}.";
                    return await sessions.CreateAsync()
                        ? $"Created session {VoiceCommands.Spell(sessions.ActiveSession.Code)}. Tell others the code."
                        : "Could not create a session. " + sessions.LastStatus;

                case CommandKind.QuickJoin:
                    if (inSession) return "You are already in a session.";
                    return await sessions.QuickJoinAsync()
                        ? $"Joined session {VoiceCommands.Spell(sessions.ActiveSession.Code)}."
                        : "Could not find a session. " + sessions.LastStatus;

                case CommandKind.JoinCode:
                    if (inSession) return "Leave the current session first.";
                    return await sessions.JoinByCodeAsync(command.Code)
                        ? $"Joined session {VoiceCommands.Spell(sessions.ActiveSession.Code)}."
                        : $"Could not join {VoiceCommands.Spell(command.Code)}. " + sessions.LastStatus;

                case CommandKind.LeaveSession:
                    if (!inSession) return "You are not in a session.";
                    await sessions.LeaveAsync();
                    return "Left the session.";

                case CommandKind.EnterRoom:
                case CommandKind.LeaveRoom:
                {
                    if (!inSession) return "Join a session first.";
                    if (room == null || !room.IsSpawned) return "The room is not ready yet.";
                    bool open = command.Kind == CommandKind.EnterRoom;
                    if (room.IsRoomOpen == open) return open ? "You are already in the room." : "You are not in the room.";
                    room.RequestSetRoom(open);
                    return open ? "Entering the room." : "Leaving the room.";
                }

                case CommandKind.Mute:
                case CommandKind.Unmute:
                {
                    if (chat == null || !chat.IsInChannel) return "Voice chat is not connected.";
                    bool mute = command.Kind == CommandKind.Mute;
                    chat.SetMuted(mute);
                    return mute ? "Microphone muted." : "Microphone on.";
                }

                case CommandKind.Help:
                    return VoiceCommands.HelpText;
            }
            return "";
        }

        // Sign-in runs at start and can take a while on a cold start; a request that
        // arrives before it finishes waits instead of failing.
        private static async Task<bool> WaitForSignInAsync(float timeoutSeconds = 15f)
        {
            float deadline = Time.unscaledTime + timeoutSeconds;
            while (AuthManager.Instance == null || !AuthManager.Instance.IsSignedIn)
            {
                if (Time.unscaledTime > deadline) return false;
                await Task.Delay(250);
            }
            return true;
        }

        private void Reply(string text, bool spoken)
        {
            Status = text;
            if (spoken) _voice.Say(text);
            else Changed?.Invoke();
        }

        // ── Chat ───────────────────────────────────

        private void Broadcast(string text)
        {
            string name = AuthManager.Instance != null ? AuthManager.Instance.DisplayName : "Me";
            if (SessionChat.Instance != null && SessionChat.Instance.IsSpawned)
                SessionChat.Instance.Say(name, text);   // echoes back to us through the RPC
            else
                AddLine(new ChatLine { Who = name, Text = text, FromSelf = true });
        }

        private void OnChatMessage(string who, string text, bool fromSelf) =>
            AddLine(new ChatLine { Who = who, Text = text, FromSelf = fromSelf });

        private void AddSystemLine(string text) => AddLine(new ChatLine { Who = null, Text = text });

        private void AddLine(ChatLine line)
        {
            Debug.Log(line.Who == null ? $"[Lobby] {line.Text}" : $"[Lobby] {line.Who}: {line.Text}");
            _chat.Add(line);
            while (_chat.Count > MaxChatLines) _chat.RemoveAt(0);
            Changed?.Invoke();
        }

        private void OnSessionJoined(ISession session)
        {
            _knownPlayers.Clear();
            foreach (var p in session.Players) _knownPlayers.Add(p.Id);
            AddSystemLine($"Joined {session.Code}");
        }

        private void OnSessionLeft()
        {
            _knownPlayers.Clear();
            AddSystemLine("Left the session");
        }

        private void OnPlayersChanged(ISession session)
        {
            var now = new HashSet<string>();
            foreach (var p in session.Players)
            {
                now.Add(p.Id);
                if (_knownPlayers.Add(p.Id)) AddSystemLine($"{SessionManager.NameOf(p)} joined");
            }
            _knownPlayers.RemoveWhere(id =>
            {
                if (now.Contains(id)) return false;
                AddSystemLine("A player left");
                return true;
            });
        }

        private void OnSessionStatus(string status)
        {
            if (!Busy) Status = status;
            Changed?.Invoke();
        }
    }
}
