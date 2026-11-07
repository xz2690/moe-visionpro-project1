using System;

namespace MR.Voice
{
    public enum MicState { Idle, Listening, Denied, Error }

    /// <summary>
    /// Speech in and out, one implementation per platform:
    /// visionOS → AppleSpeechVoice (on-device Apple speech), Quest → VivoxVoice
    /// (Vivox transcription of the voice channel), editor / desktop → TypedVoice.
    /// </summary>
    public interface IVoice
    {
        /// <summary>Short name for the status line, e.g. "Apple speech".</summary>
        string Name { get; }
        /// <summary>Whether listening can start right now (Vivox needs the channel first).</summary>
        bool IsAvailable { get; }
        /// <summary>Why it is unavailable, for the status line.</summary>
        string UnavailableReason { get; }

        event Action<string> Partial;
        event Action<string> Final;
        event Action<MicState> MicStateChanged;
        /// <summary>Raised when the last queued sentence has been spoken.</summary>
        event Action SpeakingFinished;

        void StartListening();
        void StopListening();
        /// <summary>Queues one sentence; sentences play in order.</summary>
        void Speak(string sentence);
        void StopSpeaking();
    }
}
