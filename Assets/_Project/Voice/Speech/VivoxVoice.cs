using System;
using System.Text;
using Unity.Services.Vivox;
using UnityEngine;

namespace MR.Voice
{
    /// <summary>
    /// Speech recognition through Vivox's channel transcription, for platforms without a
    /// native recognizer (Quest). Listening turns transcription on for the voice channel and
    /// collects the sentences Vivox returns for the local player until a pause; it therefore
    /// only works while the player is in the session's voice channel. Replies are not spoken
    /// (no text-to-speech here); the panel shows them.
    /// </summary>
    public class VivoxVoice : MonoBehaviour, IVoice
    {
        [Tooltip("Seconds without a new transcribed sentence that end the utterance.")]
        [SerializeField] private float silenceSeconds = 2.5f;
        [Tooltip("Longest listen before it ends on its own.")]
        [SerializeField] private float maxSeconds = 15f;

        public string Name => "Vivox transcription";
        public bool IsAvailable => VoiceManager.Instance != null && VoiceManager.Instance.IsInChannel;
        public string UnavailableReason => "Join a session first: voice commands use the voice channel";

        public event Action<string> Partial;
        public event Action<string> Final;
        public event Action<MicState> MicStateChanged;
        public event Action SpeakingFinished;

        private readonly StringBuilder _text = new();
        private bool _listening;
        private float _lastSentenceAt;
        private float _startedAt;
        private string _channel;

        public async void StartListening()
        {
            if (_listening) return;
            if (!IsAvailable)
            {
                MicStateChanged?.Invoke(MicState.Error);
                return;
            }

            _channel = VoiceManager.Instance.ChannelName;
            _text.Clear();
            _listening = true;
            _startedAt = _lastSentenceAt = Time.unscaledTime;
            VivoxService.Instance.SpeechToTextMessageReceived += OnTranscribed;
            try
            {
                if (!VivoxService.Instance.IsSpeechToTextEnabled(_channel))
                    await VivoxService.Instance.SpeechToTextEnableTranscription(_channel);
                MicStateChanged?.Invoke(MicState.Listening);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Voice] Vivox transcription failed: {ex.Message}");
                Finish(send: false, MicState.Error);
            }
        }

        public void StopListening() => Finish(send: true, MicState.Idle);

        public void Speak(string sentence)
        {
            Debug.Log("[Voice] say (no TTS on this platform): " + sentence);
            SpeakingFinished?.Invoke();
        }

        public void StopSpeaking() { }

        private void OnTranscribed(VivoxMessage message)
        {
            if (!_listening || !message.FromSelf || !message.IsTranscribedMessage) return;
            if (_text.Length > 0) _text.Append(' ');
            _text.Append(message.MessageText.Trim());
            _lastSentenceAt = Time.unscaledTime;
            Partial?.Invoke(_text.ToString());
        }

        private void Update()
        {
            if (!_listening) return;
            float now = Time.unscaledTime;
            bool gotSomething = _text.Length > 0;
            if ((gotSomething && now - _lastSentenceAt > silenceSeconds) || now - _startedAt > maxSeconds)
                Finish(send: true, MicState.Idle);
        }

        private async void Finish(bool send, MicState state)
        {
            if (!_listening) return;
            _listening = false;
            VivoxService.Instance.SpeechToTextMessageReceived -= OnTranscribed;
            string text = _text.ToString().Trim();
            try
            {
                if (_channel != null && VivoxService.Instance.IsSpeechToTextEnabled(_channel))
                    await VivoxService.Instance.SpeechToTextDisableTranscription(_channel);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Voice] Could not stop Vivox transcription: {ex.Message}");
            }
            if (send) Final?.Invoke(text);
            MicStateChanged?.Invoke(state);
        }

        private void OnDisable()
        {
            if (_listening) Finish(send: false, MicState.Idle);
        }
    }
}
