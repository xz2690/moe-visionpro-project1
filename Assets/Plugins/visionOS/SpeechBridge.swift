// Apple speech-to-text and text-to-speech for the Unity client, exported as C functions.
// Unity calls these through [DllImport("__Internal")] in AppleSpeechVoice.cs.
// Callbacks run on the main queue; the C# side still marshals them to Unity's main loop.
//
// Ported from VoiceCreator (tsoeacadrsch/metaverse-for-education); the vocabulary is this app's.

import AVFoundation
import Foundation
import Speech

public typealias MRTextCallback = @convention(c) (UnsafePointer<CChar>?) -> Void
public typealias MRIntCallback = @convention(c) (Int32) -> Void

// State codes shared with AppleSpeechVoice.cs
private let kIdle: Int32 = 0
private let kListening: Int32 = 1
private let kDenied: Int32 = 2
private let kError: Int32 = 3

final class MRSpeech: NSObject, AVSpeechSynthesizerDelegate {
    static let shared = MRSpeech()

    var onPartial: MRTextCallback?
    var onFinal: MRTextCallback?
    var onState: MRIntCallback?
    var onSpeakDone: MRIntCallback?

    /// Seconds of silence after the last recognized word that end the utterance.
    var silenceSeconds: TimeInterval = 1.5

    /// Lobby commands, plus letters and digits for spelling a session code.
    static let vocabulary: [String] = [
        "create a session", "create session", "new session", "quick join", "join a session",
        "join code", "join the session", "enter the room", "enter room", "leave the room", "leave room",
        "leave the session", "leave session", "mute", "unmute", "mute me", "unmute me", "help",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T",
        "U", "V", "W", "X", "Y", "Z",
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet", "kilo",
        "lima", "mike", "november", "oscar", "papa", "quebec", "romeo", "sierra", "tango", "uniform",
        "victor", "whiskey", "x-ray", "yankee", "zulu",
    ]

    /// Words a request doesn't end on: the speaker paused mid-sentence.
    private static let danglingWords: Set<String> = [
        "a", "an", "the", "and", "or", "of", "to", "on", "in", "into", "at", "with", "from",
        "my", "join", "code", "enter", "leave", "create", "please", "can", "could", "would",
    ]

    static func isUnfinished(_ text: String) -> Bool {
        let words = text.lowercased().split(whereSeparator: { !$0.isLetter && $0 != "'" })
        guard let last = words.last else { return false }
        return danglingWords.contains(String(last))
    }

    private let recognizer = SFSpeechRecognizer(locale: Locale(identifier: "en-US"))
    private let engine = AVAudioEngine()
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var task: SFSpeechRecognitionTask?
    private var silenceTimer: Timer?
    private var latestText = ""
    private var finished = true
    private let synth = AVSpeechSynthesizer()
    private var pendingUtterances = 0

    override init() {
        super.init()
        synth.delegate = self
        NotificationCenter.default.addObserver(
            self, selector: #selector(onInterruption(_:)),
            name: AVAudioSession.interruptionNotification, object: nil)
    }

    // MARK: listening

    func startListening() {
        stopSpeaking()
        authorize { [weak self] ok in
            guard let self else { return }
            guard ok else { self.emitState(kDenied); return }
            do { try self.beginRecognition() } catch {
                NSLog("[SpeechBridge] start failed: \(error)")
                self.teardownAudio()
                self.emitState(kError)
            }
        }
    }

    func stopListening() {
        finish(send: true)
    }

    private func authorize(_ done: @escaping (Bool) -> Void) {
        SFSpeechRecognizer.requestAuthorization { status in
            guard status == .authorized else { DispatchQueue.main.async { done(false) }; return }
            AVAudioApplication.requestRecordPermission { granted in
                DispatchQueue.main.async { done(granted) }
            }
        }
    }

    private func configureSession() throws {
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playAndRecord, mode: .default, options: [.duckOthers, .allowBluetoothA2DP])
        try session.setActive(true, options: .notifyOthersOnDeactivation)
    }

    private func beginRecognition() throws {
        guard let recognizer, recognizer.isAvailable else { throw NSError(domain: "SpeechBridge", code: 1) }
        teardownAudio()
        try configureSession()

        let req = SFSpeechAudioBufferRecognitionRequest()
        req.shouldReportPartialResults = true
        if recognizer.supportsOnDeviceRecognition { req.requiresOnDeviceRecognition = true }
        // Biases recognition toward the app's words and spelled-out session codes.
        req.contextualStrings = MRSpeech.vocabulary
        request = req
        latestText = ""
        finished = false

        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in
            self?.request?.append(buffer)
        }
        engine.prepare()
        try engine.start()

        task = recognizer.recognitionTask(with: req) { [weak self] result, error in
            DispatchQueue.main.async {
                guard let self, !self.finished else { return }
                if let result {
                    self.latestText = result.bestTranscription.formattedString
                    self.emit(self.onPartial, self.latestText)
                    self.restartSilenceTimer()
                    if result.isFinal { self.finish(send: true) }
                } else if error != nil {
                    self.finish(send: true)
                }
            }
        }
        emitState(kListening)
        restartSilenceTimer(initial: true)
    }

    private func restartSilenceTimer(initial: Bool = false) {
        silenceTimer?.invalidate()
        // Give the user longer to start talking than to pause between words, and longer again
        // when the sentence clearly isn't finished ("join code").
        let wait = initial ? max(silenceSeconds, 6.0)
            : MRSpeech.isUnfinished(latestText) ? silenceSeconds * 2 : silenceSeconds
        silenceTimer = Timer.scheduledTimer(withTimeInterval: wait, repeats: false) { [weak self] _ in
            self?.finish(send: true)
        }
    }

    private func finish(send: Bool) {
        guard !finished else { return }
        finished = true
        silenceTimer?.invalidate()
        silenceTimer = nil
        task?.cancel()
        task = nil
        request?.endAudio()
        request = nil
        teardownAudio()
        if send { emit(onFinal, latestText.trimmingCharacters(in: .whitespacesAndNewlines)) }
        emitState(kIdle)
    }

    private func teardownAudio() {
        if engine.isRunning { engine.stop() }
        engine.inputNode.removeTap(onBus: 0)
    }

    @objc private func onInterruption(_ note: Notification) {
        DispatchQueue.main.async { self.finish(send: false) }
    }

    // MARK: speaking

    func speak(_ text: String) {
        guard !text.isEmpty else { return }
        if !finished { finish(send: false) }
        try? configureSession()
        let u = AVSpeechUtterance(string: text)
        u.voice = AVSpeechSynthesisVoice(language: "en-US")
        u.rate = AVSpeechUtteranceDefaultSpeechRate
        pendingUtterances += 1
        synth.speak(u)
    }

    func stopSpeaking() {
        if synth.isSpeaking { synth.stopSpeaking(at: .immediate) }
        pendingUtterances = 0
    }

    var isSpeaking: Bool { synth.isSpeaking || pendingUtterances > 0 }

    func speechSynthesizer(_ s: AVSpeechSynthesizer, didFinish u: AVSpeechUtterance) { utteranceEnded() }
    func speechSynthesizer(_ s: AVSpeechSynthesizer, didCancel u: AVSpeechUtterance) { utteranceEnded() }

    private func utteranceEnded() {
        pendingUtterances = max(0, pendingUtterances - 1)
        if pendingUtterances == 0 { onSpeakDone?(1) }
    }

    // MARK: helpers

    private func emit(_ cb: MRTextCallback?, _ text: String) {
        guard let cb else { return }
        text.withCString { cb($0) }
    }

    private func emitState(_ s: Int32) { onState?(s) }
}

// MARK: C exports (names match AppleSpeechVoice.cs)

@_cdecl("MRSpeech_SetCallbacks")
public func MRSpeech_SetCallbacks(_ partial: MRTextCallback?, _ final: MRTextCallback?, _ state: MRIntCallback?, _ speakDone: MRIntCallback?) {
    let s = MRSpeech.shared
    s.onPartial = partial
    s.onFinal = final
    s.onState = state
    s.onSpeakDone = speakDone
}

@_cdecl("MRSpeech_SetSilenceSeconds")
public func MRSpeech_SetSilenceSeconds(_ seconds: Double) {
    MRSpeech.shared.silenceSeconds = seconds
}

@_cdecl("MRSpeech_StartListening")
public func MRSpeech_StartListening() {
    DispatchQueue.main.async { MRSpeech.shared.startListening() }
}

@_cdecl("MRSpeech_StopListening")
public func MRSpeech_StopListening() {
    DispatchQueue.main.async { MRSpeech.shared.stopListening() }
}

@_cdecl("MRSpeech_Speak")
public func MRSpeech_Speak(_ text: UnsafePointer<CChar>?) {
    guard let text else { return }
    let s = String(cString: text) // copied before returning, so the C# buffer may be freed
    DispatchQueue.main.async { MRSpeech.shared.speak(s) }
}

@_cdecl("MRSpeech_StopSpeaking")
public func MRSpeech_StopSpeaking() {
    DispatchQueue.main.async { MRSpeech.shared.stopSpeaking() }
}
