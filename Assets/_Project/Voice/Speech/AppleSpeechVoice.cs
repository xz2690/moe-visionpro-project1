using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace MR.Voice
{
    /// <summary>
    /// C# side of Assets/Plugins/visionOS/SpeechBridge.swift: on-device Apple speech
    /// recognition and text-to-speech. Native callbacks are static (IL2CPP requires
    /// MonoPInvokeCallback) and are queued to Unity's main loop.
    /// Ported from VoiceCreator (tsoeacadrsch/metaverse-for-education).
    /// </summary>
    public class AppleSpeechVoice : MonoBehaviour, IVoice
    {
        [SerializeField] private float silenceSeconds = 1.5f;

        public string Name => "Apple speech";
        public bool IsAvailable => Supported;
        public string UnavailableReason => Supported ? "" : "Apple speech only runs on visionOS";

        public event Action<string> Partial;
        public event Action<string> Final;
        public event Action<MicState> MicStateChanged;
        public event Action SpeakingFinished;

        public static bool Supported =>
#if UNITY_VISIONOS && !UNITY_EDITOR
            true;
#else
            false;
#endif

        private static readonly ConcurrentQueue<Action<AppleSpeechVoice>> Pending = new();

        private void Awake()
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            MRSpeech_SetCallbacks(OnPartialNative, OnFinalNative, OnStateNative, OnSpeakDoneNative);
            MRSpeech_SetSilenceSeconds(silenceSeconds);
#endif
        }

        private void Update()
        {
            while (Pending.TryDequeue(out var action)) action(this);
        }

        public void StartListening()
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            MRSpeech_StartListening();
#endif
        }

        public void StopListening()
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            MRSpeech_StopListening();
#endif
        }

        public void Speak(string sentence)
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            MRSpeech_Speak(sentence);
#endif
        }

        public void StopSpeaking()
        {
#if UNITY_VISIONOS && !UNITY_EDITOR
            MRSpeech_StopSpeaking();
#endif
        }

        private delegate void TextCallback(IntPtr utf8);
        private delegate void IntCallback(int value);

        [MonoPInvokeCallback(typeof(TextCallback))]
        private static void OnPartialNative(IntPtr utf8)
        {
            string s = PtrToString(utf8);
            Pending.Enqueue(v => v.Partial?.Invoke(s));
        }

        [MonoPInvokeCallback(typeof(TextCallback))]
        private static void OnFinalNative(IntPtr utf8)
        {
            string s = PtrToString(utf8);
            Pending.Enqueue(v => v.Final?.Invoke(s));
        }

        [MonoPInvokeCallback(typeof(IntCallback))]
        private static void OnStateNative(int state) => Pending.Enqueue(v => v.MicStateChanged?.Invoke((MicState)state));

        [MonoPInvokeCallback(typeof(IntCallback))]
        private static void OnSpeakDoneNative(int _) => Pending.Enqueue(v => v.SpeakingFinished?.Invoke());

        // The Swift side owns the buffer only for the duration of the call, so copy now.
        private static string PtrToString(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p);

#if UNITY_VISIONOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void MRSpeech_SetCallbacks(TextCallback partial, TextCallback final, IntCallback state, IntCallback speakDone);
        [DllImport("__Internal")] private static extern void MRSpeech_SetSilenceSeconds(double seconds);
        [DllImport("__Internal")] private static extern void MRSpeech_StartListening();
        [DllImport("__Internal")] private static extern void MRSpeech_StopListening();
        [DllImport("__Internal")] private static extern void MRSpeech_Speak([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
        [DllImport("__Internal")] private static extern void MRSpeech_StopSpeaking();
#endif
    }
}
