using System;
using System.Collections;
using MR.Core;
using UnityEngine;

namespace MR.Voice
{
    public enum VoicePhase { Unavailable, Idle, Listening, Processing, Speaking, Error }

    /// <summary>
    /// Push-to-talk state machine over an IVoice: Idle → Listening → Processing → Speaking.
    /// Picks the recognizer for the platform, keeps the voice chat microphone quiet while
    /// Apple speech owns the audio input, and hands finished utterances to whoever handles
    /// them (LobbyController) through <see cref="Transcript"/>.
    /// Test hooks: <see cref="Submit"/> and the MR_SAY launch argument feed text as if spoken.
    /// </summary>
    public class VoiceController : MonoBehaviour
    {
        private const float ProcessingTimeout = 20f;

        public static VoiceController Instance { get; private set; }

        public VoicePhase Phase { get; private set; } = VoicePhase.Idle;
        public string Partial { get; private set; } = "";
        public string Status { get; private set; } = "";
        public string RecognizerName => _voice?.Name ?? "none";

        /// <summary>A finished utterance. The handler calls <see cref="Done"/> (or <see cref="Say"/>) when it has acted on it.</summary>
        public event Action<string> Transcript;
        public event Action Changed;

        private IVoice _voice;
        private bool _unmuteAfterListening;
        private float _processingSince;
        private bool _saidLaunchText;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // One recognizer per platform; the components for the others stay idle.
            var apple = GetComponent<AppleSpeechVoice>();
            var vivox = GetComponent<VivoxVoice>();
            var typed = GetComponent<TypedVoice>();
            if (AppleSpeechVoice.Supported && apple != null) _voice = apple;
#if UNITY_ANDROID && !UNITY_EDITOR
            else if (vivox != null) _voice = vivox;
#endif
            else _voice = typed != null ? typed : (IVoice)vivox ?? apple;

            if (typed != null && !ReferenceEquals(_voice, typed)) typed.enabled = false;
        }

        private void OnEnable()
        {
            if (_voice == null) { SetPhase(VoicePhase.Unavailable, "No speech recognizer"); return; }
            _voice.Partial += OnPartial;
            _voice.Final += OnFinal;
            _voice.MicStateChanged += OnMicState;
            _voice.SpeakingFinished += OnSpeakingFinished;
            RefreshAvailability();
        }

        private void OnDisable()
        {
            if (_voice == null) return;
            _voice.Partial -= OnPartial;
            _voice.Final -= OnFinal;
            _voice.MicStateChanged -= OnMicState;
            _voice.SpeakingFinished -= OnSpeakingFinished;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            string say = LaunchArgs.Get("say");
            if (!string.IsNullOrEmpty(say) && !_saidLaunchText)
            {
                _saidLaunchText = true;
                StartCoroutine(SubmitLater(say, 4f));
            }
        }

        private IEnumerator SubmitLater(string text, float delay)
        {
            yield return new WaitForSeconds(delay);
            Submit(text);
        }

        private void Update()
        {
            if (Phase == VoicePhase.Idle || Phase == VoicePhase.Unavailable) RefreshAvailability();

            if (Phase == VoicePhase.Processing && Time.unscaledTime - _processingSince > ProcessingTimeout)
                Say("Sorry, that took too long.");
        }

        private void RefreshAvailability()
        {
            if (_voice == null) return;
            if (!_voice.IsAvailable)
            {
                if (Phase != VoicePhase.Unavailable) SetPhase(VoicePhase.Unavailable, _voice.UnavailableReason);
            }
            else if (Phase == VoicePhase.Unavailable)
            {
                SetPhase(VoicePhase.Idle, "");
            }
        }

        // ── Public API ─────────────────────────────

        /// <summary>The mic orb: start listening, or stop and send early, or interrupt speech.</summary>
        public void Toggle()
        {
            switch (Phase)
            {
                case VoicePhase.Idle:
                case VoicePhase.Error:
                    BeginListening();
                    break;
                case VoicePhase.Listening:
                    _voice.StopListening();
                    break;
                case VoicePhase.Speaking:
                    _voice.StopSpeaking();
                    break;
            }
        }

        /// <summary>Test hook: handles text exactly like a finished utterance.</summary>
        public void Submit(string text)
        {
            if (_voice is TypedVoice typed)
            {
                typed.Submit(text);
                return;
            }
            if (Phase == VoicePhase.Listening) _voice.StopListening();
            HandleFinal(text);
        }

        /// <summary>Speak (where the platform can) and show a reply; ends the current request.</summary>
        public void Say(string text)
        {
            Status = text;
            if (string.IsNullOrEmpty(text)) { Done(); return; }
            SetPhase(VoicePhase.Speaking, text);
            _voice.Speak(text);
        }

        /// <summary>The handler finished without a spoken reply.</summary>
        public void Done()
        {
            if (Phase == VoicePhase.Processing) SetPhase(VoicePhase.Idle, Status);
        }

        // ── Voice events ───────────────────────────

        private void BeginListening()
        {
            if (!_voice.IsAvailable)
            {
                SetPhase(VoicePhase.Unavailable, _voice.UnavailableReason);
                return;
            }
            Partial = "";
            // Apple speech takes the audio input; keep the voice chat from sending noise meanwhile.
            var chat = VoiceManager.Instance;
            if (_voice is AppleSpeechVoice && chat != null && chat.IsInChannel && !chat.IsMuted)
            {
                chat.SetMuted(true);
                _unmuteAfterListening = true;
            }
            _voice.StartListening();
            SetPhase(VoicePhase.Listening, "Listening...");
        }

        private void RestoreVoiceChat()
        {
            if (!_unmuteAfterListening) return;
            _unmuteAfterListening = false;
            VoiceManager.Instance?.SetMuted(false);
        }

        private void OnPartial(string text)
        {
            Partial = text;
            Changed?.Invoke();
        }

        private void OnMicState(MicState state)
        {
            switch (state)
            {
                case MicState.Denied:
                    RestoreVoiceChat();
                    SetPhase(VoicePhase.Error, "Allow microphone and speech recognition in Settings");
                    break;
                case MicState.Error:
                    RestoreVoiceChat();
                    SetPhase(VoicePhase.Error, "Microphone unavailable");
                    break;
                case MicState.Idle:
                    RestoreVoiceChat();
                    if (Phase == VoicePhase.Listening && string.IsNullOrWhiteSpace(Partial))
                        SetPhase(VoicePhase.Idle, "I didn't catch that");
                    break;
            }
        }

        private void OnFinal(string text) => HandleFinal(text);

        private void HandleFinal(string text)
        {
            Partial = "";
            RestoreVoiceChat();
            if (string.IsNullOrWhiteSpace(text))
            {
                SetPhase(VoicePhase.Idle, "I didn't catch that");
                return;
            }

            _processingSince = Time.unscaledTime;
            SetPhase(VoicePhase.Processing, "");
            if (Transcript == null)
            {
                Say("Nobody is listening for commands.");
                return;
            }
            Transcript.Invoke(text.Trim());
        }

        private void OnSpeakingFinished()
        {
            if (Phase == VoicePhase.Speaking) SetPhase(VoicePhase.Idle, Status);
        }

        private void SetPhase(VoicePhase phase, string status)
        {
            Phase = phase;
            Status = status;
            Changed?.Invoke();
        }
    }
}
