# Architecture

Naad is one Windows Forms process with no visible main window: a tray icon,
a global keyboard hook, and a small set of classes that each do one job.
`Program.cs` builds every object by hand and wires them together; there is
no dependency-injection container. This page describes how a dictation
flows through them. [STYLE.md](STYLE.md) has the rules the code follows.

## Folders

Code is organised by feature (`src/Naad/`), and the tests mirror it
(`tests/Naad.Tests/`).

| Folder | Holds |
|---|---|
| `Hotkey/` | `KeyboardHook` (the low-level keyboard hook, plus a mouse hook while the shortcut is held), `ChordMatcher` (pure: which key events are a press, a release or a cancel), `Chord` and `ChordCapture` (the shortcut and capturing a new one) |
| `Dictation/` | `DictationSession` (pure state machine: Idle, Recording, Transcribing, Inserting) and `DictationController`, which carries out what the session decides |
| `Audio/` | `AudioRecorder` (WinMM capture through NAudio, 16 kHz mono 16-bit, in memory), `SilenceGate` and `Wav` (pure), `LevelMeter` (for the overlay) |
| `Transcription/` | `ITranscriber` with two implementations: `LocalWhisperTranscriber` (Whisper.net, on the graphics card through Vulkan when there is one; `GpuChoice` picks a discrete card) and `OpenAiCompatibleTranscriber` (HTTP, with `RetryPolicy`). Also `LocalModel` (download and SHA-256 check), `ProviderSelection`, `FillerFilter` |
| `Live/` | `ILiveSession` with two implementations: `LiveTranscript` (phrases split by `SpeechChunker`, pure, and transcribed on this PC while the user speaks) and `RealtimeTranscript` (a WebSocket stream to OpenAI or Deepgram, whose messages `RealtimeProtocol` and `RealtimeText` read, both pure). `LiveTyper` types stable text into the window, checking focus before each phrase |
| `Meetings/` | `MeetingRecorder` (start and stop), `MeetingCapture` (the microphone through WinMM and the PC's sound through WASAPI process loopback), `MeetingTranscriber` (each track split at pauses and transcribed in turn, on the meeting's timeline), `PcmConverter` and `MeetingTranscript` (pure), `MeetingStore` (DPAPI files) |
| `Insertion/` | `FocusGuard` (may the original window receive text?), `TextInserter` (paste through the clipboard, then restore it), `WindowsClipboard`, `UnicodeTyping` (the fallback when the clipboard can't be used) |
| `Tray/` | `TrayIcon` (icon, menu, notifications), `RecordingOverlay` (a layered window that never activates) with `VoiceBars` (pure: bar heights from recent levels), `SoundCues` |
| `Settings/` | `AppSettings`, `SettingsStore` (versioned JSON with migration, repair and atomic writes), `SecretStore` (the DPAPI key), `SettingsWindow` and `SettingsInput`, `StartupEntry` (the Run key) |
| `Updates/` | `AppUpdater` (Velopack and GitHub Releases) and `UpdatePrompt` |
| `Native/` | Every P/Invoke declaration, and nothing else |
| top level | `Program`, `Log` and `LogEvent`, `SingleInstanceGuard` |

## One dictation

```text
key down ─► KeyboardHook ─► ChordMatcher ─► DictationController.Press
                                               │  session: Idle → Recording
                                               │  FocusGuard remembers the window
                                               ▼
                                   AudioRecorder.Start (thread pool)
key up ───► KeyboardHook ─► ChordMatcher ─► DictationController.Release
                                               │  session: Recording → Transcribing
                                               ▼
                                   AudioRecorder.StopAsync → PCM in memory
                                   SilenceGate: too short or silent? stop here
                                   Wav.Encode → ITranscriber.TranscribeAsync
                                               │  session: Transcribing → Inserting
                                               ▼
                                   FocusGuard: same window, still open, not elevated?
                                   TextInserter: wait for modifiers, snapshot the
                                   clipboard, write the text, Ctrl+V, restore
                                               │  session: Inserting → Idle
```

When a step can't go ahead, the transcript isn't lost. `FocusGuard` and
`TextInserter` return a reason, the controller keeps the text in memory for
**Copy last transcript**, and the tray shows a notification. Recording stops
by itself after five minutes, and the text is then pasted once the shortcut
is let go.

## Threads

- **The UI thread** runs the message loop, the hooks' callbacks, the
  session, the controller, the clipboard, the tray and the windows. Every
  `await` in the controller resumes there, so the session is only ever
  touched from one thread. The hook callbacks only post an event and
  return, because Windows removes a hook that answers slowly.
- **The thread pool** opens the microphone, which can take long enough to
  stall the hook, and runs Whisper.
- **NAudio's capture thread** delivers audio buffers into memory and raises
  the level for the overlay, which is posted to the UI thread.

## Pure logic and side effects

Anything that can be decided without the operating system lives in a class
that doesn't call Win32, the clipboard, the microphone, the network or the
UI: `ChordMatcher`, `DictationSession`, `SilenceGate`, `Wav`, `FillerFilter`,
`ProviderSelection`, `RetryPolicy`, `GpuChoice`, `VoiceBars`, `SettingsInput`, and the
decision parts of `FocusGuard` and `TextInserter`. These carry most of the
unit tests.

The classes that do the side effects stay thin, and are checked by hand
with [manual-tests.md](manual-tests.md). `DictationController` receives
everything it uses through its constructor (the recorder, the transcriber,
the inserter, the focus checks and a clock), so its tests run whole
dictations with fakes only at those edges.

## Startup and shutdown

`Program.Main` runs Velopack's install hooks first, then takes the
single-instance mutex, configures the log, loads the settings and the key,
creates the tray icon, recorder, hook, clipboard window and controller, and
opens Settings if Naad can't dictate yet. Settings changes apply at once, or
as soon as the current dictation ends.

Exit ends the message loop. The objects are then disposed in reverse
order: the controller first, so nothing is pasted after Exit, then the
clipboard window, the hook (unhooked), the recorder (the microphone), the
HTTP client, the overlay and the tray icon.

## Releases

Pushing a `v1.2.3` tag runs `.github/workflows/release.yml`. It tests,
publishes a self-contained win-x64 build stamped with the tag's version,
packs it with Velopack (package id `Naad.App`, installed per user to
`%LOCALAPPDATA%\Naad.App`), and publishes a GitHub Release with the
installer, a portable zip and the update feed. Installed copies find new
releases through that feed. User data lives outside the install folder,
in `%APPDATA%\Naad` and `%LOCALAPPDATA%\Naad`, so updates and uninstalls
keep it.
