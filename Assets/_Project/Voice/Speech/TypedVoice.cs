using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;

namespace MR.Voice
{
    /// <summary>
    /// Stand-in voice for the editor and desktop builds: "listening" ends with whatever was
    /// typed into the small text box at the bottom of the Game view (or passed to
    /// <see cref="Submit"/> from a test), and "speaking" just logs. This keeps the whole
    /// command path testable on a Mac mini with no microphone.
    /// </summary>
    public class TypedVoice : MonoBehaviour, IVoice
    {
        public string Name => "Typed text";
        public bool IsAvailable => true;
        public string UnavailableReason => "";

        public event Action<string> Partial;
        public event Action<string> Final;
        public event Action<MicState> MicStateChanged;
        public event Action SpeakingFinished;

        private bool _listening;
        private string _typed = "";
        private int _queued;

        public void StartListening()
        {
            StopSpeaking();
            _listening = true;
            MicStateChanged?.Invoke(MicState.Listening);
        }

        public void StopListening()
        {
            if (!_listening) return;
            _listening = false;
            Final?.Invoke("");
            MicStateChanged?.Invoke(MicState.Idle);
        }

        /// <summary>Test hook: treated exactly like a finished utterance.</summary>
        public void Submit(string text)
        {
            if (!_listening) StartListening();
            Partial?.Invoke(text);
            _listening = false;
            Final?.Invoke(text);
            MicStateChanged?.Invoke(MicState.Idle);
        }

        public void Speak(string sentence)
        {
            Debug.Log("[Voice] say: " + sentence);
            _queued++;
            StartCoroutine(SpeakOne(sentence));
        }

        private IEnumerator SpeakOne(string sentence)
        {
            yield return new WaitForSeconds(0.2f * sentence.Split(' ').Length);
            if (_queued == 0) yield break;
            if (--_queued == 0) SpeakingFinished?.Invoke();
        }

        public void StopSpeaking()
        {
            if (_queued == 0) return;
            _queued = 0;
            SpeakingFinished?.Invoke();
        }

        // A plain IMGUI box: it only exists in the editor and desktop builds, never in XR.
        private void OnGUI()
        {
            if (XRSettings.isDeviceActive) return;

            const float w = 420f, h = 26f;
            var rect = new Rect(10f, Screen.height - h - 10f, w, h);
            GUI.Box(new Rect(rect.x - 4f, rect.y - 4f, rect.width + 8f, rect.height + 8f), GUIContent.none);
            GUI.Label(new Rect(rect.x, rect.y, 40f, h), _listening ? "Say:" : "Type:");

            GUI.SetNextControlName("mr-say");
            _typed = GUI.TextField(new Rect(rect.x + 44f, rect.y, w - 120f, h), _typed);
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return &&
                         GUI.GetNameOfFocusedControl() == "mr-say";
            if ((GUI.Button(new Rect(rect.x + w - 70f, rect.y, 70f, h), "Send") || enter) && !string.IsNullOrWhiteSpace(_typed))
            {
                string text = _typed.Trim();
                _typed = "";
                Submit(text);
            }
        }
    }
}
