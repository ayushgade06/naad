# Manual tests

These checks cover behaviour that unit tests can't: the microphone, the global
keyboard hook, the clipboard, synthetic input and window focus. Every phase
adds its checks here.

Record each run in the phase's pull request:

- the date
- the Windows version (`winver`)
- the microphone used
- pass or fail for each check
- any measured numbers

Never paste transcripts, API keys or screenshots containing them into the
results.

## Setup for the vertical slice (P3)

1. Check that your keyboard has a **Right Ctrl** key. Some newer laptops
   have a Copilot key in its place.
2. Get a Groq API key from https://console.groq.com/keys.
3. In a PowerShell window, set the key **for that window only**. It is not
   saved anywhere:

   ```powershell
   $env:NAAD_GROQ_API_KEY = [Net.NetworkCredential]::new('', (Read-Host -AsSecureString 'Groq API key')).Password
   ```

   Since P12 you can instead store the key once, encrypted; see "The API
   key" in `docs/settings.md`.

4. From the repository root, run `dotnet run --project src/Naad`. Leave the
   window open: it shows what Naad is doing. Since P4, Naad runs in the
   notification area. Quit it with **right-click the tray icon > Exit**;
   Ctrl+C in the terminal no longer stops it.

## Vertical slice: M-S1 to M-S5

Since P5, the terminal shows log lines such as
`2026-10-25T10:15:30.123Z INFO Pasted release_to_text_ms=640`, the same as
the log file (see M-L1). The expected results below give the event names.

| ID | Check | Steps | Expected |
|---|---|---|---|
| M-S1 | Missing key | Run Naad in a new window where `NAAD_GROQ_API_KEY` is not set. | The **Naad** error dialog (since P4) explains how to set the key. The log shows `ERROR ApiKeyMissing`, the exit code is 1 (`$LASTEXITCODE`), and no key is shown. |
| M-S2 | Dictate into Notepad | 1. Open Notepad and click in it. 2. Hold **Right Ctrl**, say "hello world this is a test", release. 3. Repeat 3 times. | The spoken text appears at the cursor each time. The log shows `RecordingStarted mic_open_ms=N`, `RecordingStopped held_ms=N wav_bytes=N`, `TranscriptionCompleted request_ms=N chars=N` and `Pasted release_to_text_ms=N`. Record the three `release_to_text_ms` values. |
| M-S3 | Ctrl shortcuts keep working | 1. In Notepad, use **Left** Ctrl+C and Ctrl+V. 2. Select text and press **Right** Ctrl+C, then paste with Ctrl+V. 3. Tap Right Ctrl as quickly as possible, 5 times, waiting 3 s between taps. | 1. Copy and paste work, and nothing is recorded. 2. Copy works, the log shows `DictationCancelledByKey`, and nothing is inserted. 3. Recording starts on key-down, so each tap logs `RecordingStarted` and then, on release, `DictationIgnoredShortHold held_ms=N`. Nothing is inserted. A tap must never leave a recording running; the next press always starts a new one. |
| M-S4 | Focus change blocks the paste | In Notepad, hold Right Ctrl and speak a long sentence. Release it and **at once** click the console window with the mouse, before the text appears. (Since P8, clicking while still holding Right Ctrl cancels the dictation instead; see M-H2.) | The log shows `WARN PasteSkippedFocusChanged`. No text is inserted anywhere. Pressing Ctrl+V in Notepad afterwards pastes the transcript. If the text was pasted before your click, retry with a longer sentence. |
| M-S5 | Microphone blocked | 1. In Settings > Privacy & security > Microphone, turn off **Let desktop apps access your microphone**. 2. Hold Right Ctrl, speak, release. 3. Turn the setting back on. | Naad does not crash. Note which happened: `WARN MicrophoneUnavailable mm_result=N`, or a recording of silence. With silence, Whisper may invent a phrase such as "Thank you." and Naad will paste it; note that too. Since P10 the expected result is M-A2's. |

## Tray app and lifecycle (P4): M-T1 to M-T6

Since P4, Naad is a windowless tray app. Launch it either way:

- From a terminal: `dotnet run --project src/Naad`. Log lines and errors
  appear in that terminal.
- Directly: double-click `src\Naad\bin\<Debug|Release>\net10.0-windows\Naad.exe`.
  No window opens; the log file (M-L1) is the only output.
  `NAAD_GROQ_API_KEY` must be set in your user environment for this route.

**Diagnosing a failed start.** If Naad can't start (the API key is missing,
or the keyboard hook can't be installed), it shows an error dialog titled
**Naad** that explains why. The same text goes to the terminal when you
started it with `dotnet run`, and the log records `ApiKeyMissing` or
`FatalError exception=<type>`. API keys are never shown in these messages.

**Single instance.** Only one Naad runs per Windows session. It holds the
named mutex `Local\Naad.SingleInstance.A5F2782B-067F-4CFB-8471-B110B09BBBEF`.
A second launch exits at once without installing another keyboard hook or
tray icon.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-T1 | Starts in the tray, no window | Launch Naad either way. Look in the notification area; Windows 11 may hide new icons behind the **^** (show hidden icons) button. | A teal icon with white sound-wave bars appears, with the tooltip "Naad: hold Right Ctrl to dictate" (since P7 it names the configured shortcut). No console or other window opens. | Yes. The P4 UI Automation harness checks the process has no window and finds the icon by name. |
| M-T2 | Exit from the tray | Right-click the icon > **Exit**. | The icon disappears, `Naad.exe` is gone from Task Manager, and `dotnet run` returns. | Yes. The harness opens the menu with a synthetic mouse right-click, invokes **Exit** through UI Automation, and checks exit code 0 and that the icon is gone. |
| M-T3 | Second launch | While Naad runs, launch it again. | No second icon appears. It logs `INFO SecondInstanceRejected` and exits with code 0. The first instance keeps working. | Yes. The harness checks exit code 0, the `SecondInstanceRejected` line, and that the first instance is still alive. |
| M-T4 | Missing key | Launch with `NAAD_GROQ_API_KEY` unset. | A **Naad** error dialog explains how to set the key. **OK** exits with code 1. No key is shown. | Yes. The harness reads the dialog through UI Automation and presses OK. |
| M-T5 | Dictation still works in the tray build | With Naad in the tray, repeat M-S2 (one dictation into Notepad) and M-S3(c) (quick taps). | Same results as P3. | **No.** It needs physical keys; injected keys are ignored by design. |
| M-T6 | Exit during a dictation | Hold Right Ctrl, speak a long sentence, release, then immediately choose tray > **Exit**. | Naad exits. Nothing is pasted after you chose Exit. | **No.** It needs physical keys. |

Automated checks drive only processes the harness started itself (tracked by
PID). They use a fake API key behind a dead local proxy, so no audio or
request can leave the machine. They never use the microphone.

## Logging (P5): M-L1

Naad writes a diagnostic log to `%LOCALAPPDATA%\Naad\logs\naad.log`. At
1 MB it is moved to `naad.1.log`, replacing the previous backup, so the log
never uses more than about 2 MB. Each line has a UTC timestamp, a level
(`INFO`, `WARN` or `ERROR`), an event name and numeric fields, for example:

```text
2026-10-25T10:15:30.123Z INFO TranscriptionCompleted request_ms=514 chars=27
2026-10-25T10:15:30.140Z WARN PasteSkippedFocusChanged
2026-10-25T10:16:02.001Z ERROR DictationFailed exception=System.Runtime.InteropServices.ExternalException
```

The log can only contain event names, numbers and exception type names. It
never holds audio, transcripts, clipboard text, window titles, exception
messages or API keys. Unit tests enforce this.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-L1 | Log contents after real use | Start Naad, do one dictation (M-S2) and one focus-change test (M-S4), exit from the tray, then open `naad.log` in Notepad. | You see `AppStarted`, the dictation events with numbers, `PasteSkippedFocusChanged` and `AppStopped`. No words you said appear anywhere in the file. | Partly. The harness checked start, stop, second-instance and missing-key runs against the real log file. Dictation events need physical keys. |

## Settings (P6): M-C1 to M-C4

Settings live in `%APPDATA%\Naad\settings.json`. [settings.md](settings.md)
describes the format, the versioning policy and every recovery case. Close
Naad before editing the file, and keep a copy if your file matters to you.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-C1 | First run creates settings | Rename `%APPDATA%\Naad` if it exists, then start Naad. | `settings.json` appears with `"version": 1` and `"fileLogging": true`. The log shows `SettingsCreated`. | Yes. The P6 harness ran it against the real exe. |
| M-C2 | Turning off file logging | Set `"fileLogging": false`, start Naad, then exit. | `naad.log` gets no new lines. The terminal (with `dotnet run`) still shows `SettingsLoaded` and `AppStarted`. | Yes. |
| M-C3 | Corrupt file recovery | Replace the file's contents with `{ this is not json`, then start Naad. | Naad starts normally. The original is kept as `settings.json.corrupt-<time>.bak`, `settings.json` holds the defaults again, and the log shows `WARN SettingsCorrupt`. | Yes. |
| M-C4 | Newer version is preserved | Set `"version": 99`, then start Naad. | Naad starts with defaults. The file is unchanged and no backup is made. The log shows `WARN SettingsFutureVersion file_version=99 supported_version=1`. | Yes. |

The harness restored the real `%APPDATA%\Naad` afterwards: it removed the
folder, because the folder had not existed before the run. Unit tests use
temporary folders only.

## Shortcut (P7): M-SC1 and M-SC2

Since P7 the shortcut comes from `dictationShortcut` in settings.json (see
[settings.md](settings.md#the-dictation-shortcut)), and the rules that turn
key events into a press, a release or a cancel live in `ChordMatcher`.
Those rules are unit tested with synthetic key events, so these
keyboard-only cases no longer depend on a manual run:

| Case | Covered by |
|---|---|
| Right Ctrl alone presses on key-down and releases on key-up | `ChordMatcherTests` |
| Right Ctrl plus another key (M-S3 step 2) cancels, and the release that follows does nothing | `ChordMatcherTests` |
| Left Ctrl, alone or with other keys (M-S3 step 1), never matches Right Ctrl | `ChordMatcherTests` |
| Auto-repeat of the shortcut or of another key | `ChordMatcherTests` |
| Repeated taps (M-S3 step 3): each tap is one press and one release | `ChordMatcherTests` |
| Multi-key chords in either press order, released in any order | `ChordMatcherTests` |
| The most specific chord wins; equal lengths fall back to list order | `ChordMatcherTests` |
| Parsing, refusing and writing `dictationShortcut` | `ChordTests`, `SettingsStoreTests` |
| The tray tooltip names the shortcut and fits the 127-character limit | `TrayIconTests`, P7 harness |

What still needs real hardware, because the tests feed the matcher directly
and synthetic input is no proof of how a physical keyboard behaves:

- that Windows reports the sided keys (Right Ctrl as `RControlKey`) to the
  hook on your keyboard, which M-S2 and M-S3 confirm;
- the 300 ms tap threshold and the recording itself (M-S3 step 3);
- the checks below for a non-default shortcut.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-SC1 | Custom shortcut | Close Naad. Set `"dictationShortcut": "LeftCtrl+Space"`, start Naad, then hover over the tray icon. In Notepad, hold Left Ctrl, then hold Space, speak, release both. | The tooltip reads "Naad: hold Left Ctrl+Space to dictate". The text is pasted, and the log shows `RecordingStarted` and `Pasted`. Right Ctrl alone does nothing. Since P8 no space is typed: the chord's Space is swallowed. | Partly. The P7 harness checked the tooltip and startup for the default, a custom and an invalid shortcut against the real exe. Dictation needs physical keys. |
| M-SC2 | Cancel stops the microphone at once | With the default shortcut, hold Right Ctrl, speak for 2 s, press C, keep holding Right Ctrl for 2 s, then release. | The log shows `DictationCancelledByKey` when C goes down, not at the release. Nothing is pasted. The Windows microphone indicator disappears right after C. | **No.** It needs physical keys. |

**Status after P7:** M-S2, M-S3, M-T5, M-T6, M-SC1 (dictation part),
M-SC2 and the dictation part of M-L1 need a person at a physical keyboard
and microphone, and have **not been run** against the P7 build. P7 changed
the cancel path (M-S3 step 2 now stops the recording when the other key
goes down), so M-S3 and M-SC2 matter most on the next manual pass.

## Keyboard hook (P8): M-H1 to M-H5

Since P8 the hook:

- **swallows** the shortcut's non-modifier keys, so Space in
  `LeftCtrl+Space` or F9 alone never reaches the app, while modifiers such
  as Right Ctrl always pass through;
- **cancels** the dictation when a mouse button or the wheel is used while
  the shortcut is held (`DictationCancelledByMouse`). The click or scroll
  still reaches the app. The mouse hook is installed only while the
  shortcut is held;
- **skips only Naad's own** synthetic input, recognised by a marker on its
  Ctrl+V. Synthetic input from other programs, such as a key remapper,
  counts like a physical key. Input injected by a lower-integrity
  (sandboxed) process is ignored.

The rules are unit tested (`ChordMatcherTests`, `KeyboardHookTests`). The
checks below need physical keys, a mouse and a microphone.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-H1 | Hold and release in real apps | Dictate one sentence each into Notepad, Chrome (the address bar or a text box) and VS Code (an editor). | Each time the text appears once at the cursor. The log shows one `RecordingStarted` and one `Pasted` per dictation. | **No.** It needs physical keys. |
| M-H2 | Mouse cancels | 1. In Chrome, hold Right Ctrl and click a link. 2. Hold Right Ctrl and scroll the wheel. 3. Hold Right Ctrl, speak, middle-click. | Each time the log shows `DictationCancelledByMouse` and nothing is pasted. The link still opens in a new tab and the page still zooms: the mouse input reaches Chrome. | **No.** It needs physical keys. |
| M-H3 | No stuck modifier | After M-H1 and M-H2, and one Right Ctrl+C cancel, open the On-Screen Keyboard (`osk`) and type `abc` in Notepad. | No modifier is lit in the On-Screen Keyboard, `abc` is typed in lowercase, and no shortcut fires. | **No.** It needs physical keys. |
| M-H4 | Naad's own Ctrl+V doesn't trigger it | Close Naad, set `"dictationShortcut": "V"`, start Naad. In Notepad, hold V, speak, release. Then restore `"RightCtrl"`. | The text is pasted once. After `Pasted`, the log shows no further `RecordingStarted`: the paste's own V was skipped. No `v` is typed while holding V. | Partly. The P8 harness injected Right Ctrl carrying Naad's marker into the real exe (SendInput accepted it) and no recording started. |
| M-H5 | Swallowed shortcut key | Close Naad, set `"dictationShortcut": "F9"`, start Naad. In VS Code, put the cursor on a code line, hold F9, speak, release. Then restore `"RightCtrl"`. | The text is pasted and no breakpoint is toggled: VS Code never saw F9. | **No.** It needs physical keys. |

The P8 harness injects keys into whichever window has focus and drives the
mouse to use the tray menu. Run it only on an idle machine.

**Status after P8:** M-H1 to M-H5 have **not been run**. Together with the
checks listed after P7, they are the physical-key pass still owed.

## Dictation state (P9): M-D1 and M-D2

Since P9, `DictationSession` owns the dictation state (Idle, Recording,
Transcribing, Inserting), and the controller only carries out what it
decides. `DictationSessionTests` covers every state and event pair: short
holds, cancels, presses while busy, the microphone failing, empty and
failed transcripts, and results for an old job, which are ignored so a
transcript can never be inserted twice. What still needs a person:

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-D1 | Five-minute limit | In Notepad, hold Right Ctrl for just over 5 minutes, speaking now and then, then release. | At 5 minutes the log shows `RecordingMaxDurationReached`, then `RecordingStopped held_ms=300000` (about), and the icon turns amber. The text is pasted as soon as you release Right Ctrl: a held modifier would change the paste, so Naad waits for it, for up to 30 s. Held longer, the transcript is kept instead (`PasteSkippedModifiersHeld` and the notification). The release starts nothing new. | **No.** It needs physical keys and a microphone. |
| M-D2 | Press again right after a cancel | Press Right Ctrl+C, then within a second hold Right Ctrl and dictate a sentence. | The log shows `DictationCancelledByKey`, then a new `RecordingStarted`, and the sentence is pasted. No `DictationFailed` or `MicrophoneUnavailable` appears. | **No.** It needs physical keys and a microphone. |

**Status after P9:** M-D1 and M-D2 have **not been run**. A smoke run of
the real exe (start, `AppStarted`, no errors) passed.

## Audio capture (P10): M-A1 to M-A3

Since P10, a dictation that can't record shows a tray notification titled
**Naad can't use the microphone** that says why, and logs one of these
events. Nothing is recorded, uploaded or pasted.

| Cause | Log event | Notification says |
|---|---|---|
| Windows privacy settings block the microphone | `WARN MicrophoneBlocked` | Privacy settings block the microphone; click to open them. |
| No recording device | `WARN MicrophoneNotFound mm_result=N` | No microphone was found. |
| Another app holds the device | `WARN MicrophoneInUse mm_result=N` | Another app is using the microphone. |
| Any other WinMM error | `WARN MicrophoneUnavailable mm_result=N` | The microphone could not be opened. |

Naad checks the privacy switches before opening the device: the
device-wide **Microphone access**, your own **Microphone access**, and
**Let desktop apps access your microphone**. A blocked device may open and
record silence, and Whisper turns silence into invented text, so the
device's own error can't be relied on. `MicrophoneFailureTests` covers the
error mapping and the privacy decision, and `TrayIconTests` the messages.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-A1 | Default microphone | Dictate one sentence into Notepad with your default microphone. | The text is pasted. The log shows `RecordingStarted mic_open_ms=N` and `RecordingStopped held_ms=N wav_bytes=N`. | **No.** It needs physical keys and a microphone. |
| M-A2 | Access blocked | 1. In Settings > Privacy & security > Microphone, turn off **Let desktop apps access your microphone**. 2. Hold Right Ctrl, speak, release. 3. Click the notification. 4. Turn the setting back on and dictate again. Repeat with **Microphone access** turned off. | Step 2: no recording, `WARN MicrophoneBlocked`, and the notification explains the block. Nothing is pasted, not even an invented phrase. Step 3 opens the microphone privacy page. Step 4 works normally. | **No.** It changes Windows privacy settings and needs physical keys. |
| M-A3 | No microphone | Unplug or disable every input device (Settings > System > Sound > Input, or Device Manager), then hold Right Ctrl and release. Re-enable the device afterwards. | No crash. The log shows `WARN MicrophoneNotFound`, and the notification says no microphone was found. After re-enabling, dictation works again without restarting Naad. | **No.** It changes devices and needs physical keys. |

With Focus Assist (Do not disturb) on, Windows may hold the notification
back; the log event still appears.

**Status after P10:** M-A1 to M-A3 have **not been run**. A smoke run of
the real exe (start, `AppStarted`, no errors) passed. The automated runs
never open the microphone or change privacy settings.

## Recording gates (P11): M-W1 to M-W3

Since P11, a finished recording is checked before it is uploaded. Naad
writes its own 44-byte WAV header (`Wav`), and `SilenceGate` rejects:

- **too short:** less than 300 ms of audio, which can happen when the
  microphone opens late. Logged as `INFO RecordingTooShort`.
- **silent:** no sample reaches about -45 dBFS (peak 180 of 32768), or
  fewer than three 30 ms windows reach about -55 dBFS RMS (60). Logged as
  `INFO RecordingSilent`.

Both lines carry `audio_ms`, `peak` and `rms`, and nothing is uploaded or
pasted. Accepted recordings log `RecordingStopped` with the same `peak` and
`rms` fields. These are levels, not audio. The thresholds are deliberately
low, so a quiet sentence is uploaded rather than lost; the numbers from
M-W2 decide whether they need tuning. `WavTests` checks the header against
a hand-written fixture, and `SilenceGateTests` covers each threshold's
boundary with synthetic samples.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-W1 | Silence isn't uploaded | In Notepad, hold Right Ctrl for 2 s without speaking, in a quiet room, then release. Repeat with the microphone muted (hardware switch or Settings > System > Sound > Input volume 0). | Each time the log shows `RecordingSilent audio_ms=N peak=N rms=N`, no `TranscriptionCompleted`, and nothing is pasted, not even "Thank you." | **No.** It needs physical keys and a microphone. |
| M-W2 | Quiet speech still goes through | Hold Right Ctrl and say one sentence softly, as if someone nearby were asleep. Then say one at your normal level. | Both are pasted. Record both `RecordingStopped` lines' `peak` and `rms`, and M-W1's, in the pull request. | **No.** It needs physical keys and a microphone. |
| M-W3 | Pause after speaking | Hold Right Ctrl, say "hello", stay silent for 20 s, then release. | "Hello" is pasted. | **No.** It needs physical keys and a microphone. |

**Status after P11:** M-W1 to M-W3 have **not been run**.

## API key storage (P12): M-K1

Since P12, the API key can be stored encrypted with DPAPI instead of being
set in every terminal; `docs/settings.md` describes how. A non-blank
`NAAD_GROQ_API_KEY` still overrides the stored key. `SecretStoreTests` runs
real DPAPI against temporary files: round trip, no plaintext in the file,
garbage, truncated or foreign ciphertext, a locked file, the override's
precedence, a failed save keeping the previous key, and log lines without
the key.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-K1 | Stored key end to end | 1. In a new PowerShell window without `NAAD_GROQ_API_KEY`, store your key as `docs/settings.md` shows. 2. Open `%LOCALAPPDATA%\Naad\api-key.dpapi` in Notepad. 3. Start Naad and dictate one sentence into Notepad. 4. Exit, replace the file's contents with `junk`, start Naad. 5. Store the key again. | 1. `$LASTEXITCODE` is 0. 2. The file is unreadable binary; your key isn't in it. 3. The log shows `ApiKeyLoaded`, and the text is pasted. 4. A **Naad** dialog says the stored key can't be read, the log shows `ERROR ApiKeyUnreadable exception=System.Security.Cryptography.CryptographicException`, and Naad exits. 5. Naad works again. No step shows the key on screen or in `naad.log`. | Partly. The P12 harness ran the Release exe with a synthetic key: storing through a pipe (exit 0, 262 bytes, no plaintext as ASCII or UTF-16), refusing without a pipe (exit 1), starting from the stored key (`ApiKeyLoaded`, `AppStarted`), the override (`ApiKeyFromEnvironment`), no key (`ApiKeyMissing`, dialog), and a garbage file (`ApiKeyUnreadable`, dialog). The log never held the synthetic keys. Dictation with a real key wasn't run. |

**Status after P12:** M-K1 has **not been run** with a real key; the
harness parts above passed on 2026-10-28.

Since P17, a missing or unreadable key no longer stops Naad: instead of the
dialog in step 4, Naad starts and opens its settings window, where the key
can be entered again (M-U1).

**Real key, 2026-10-28 (after P16).** Storing a real Groq key through the
documented PowerShell pipe produced a key that every request rejected
before sending it ("Request headers must contain only ASCII characters").
PowerShell starts piped text with a UTF-8 byte-order mark, and Naad read
stdin with the console's code page, so the key gained three stray
characters (U+00EF U+00BB U+00BF). Since the fix, `--set-api-key` reads
stdin as UTF-8 and drops the mark, and Naad accepts only visible-ASCII
keys from the pipe, the store and `NAAD_GROQ_API_KEY`. A key stored before
the fix is reported as unreadable, so Naad asks for it to be stored again.
Re-stored with the fixed build, the real key started Naad (`ApiKeyLoaded`,
`AppStarted`) and authenticated with Groq. The file held no plaintext,
and the log never held the key.

## Transcription provider (P13): M-G1 to M-G3

Since P13, the provider's base URL and model are settings
(`transcriptionBaseUrl`, `transcriptionModel`; see `docs/settings.md`),
and every expected failure comes back as a typed result instead of an
exception. What the log shows:

| Cause | Log event |
|---|---|
| 401 or 403 (wrong or revoked key), 413, 429, 5xx, other 4xx | `WARN TranscriptionFailed http_status=N` |
| No response: DNS, connection, TLS | `WARN TranscriptionFailed network_error=N` (an `HttpRequestError` value) |
| No answer within 20 s | `WARN TranscriptionTimedOut` |
| A success status with an unexpected body | `WARN TranscriptionMalformedResponse` |
| The whole transcript is a known Whisper filler, such as "Thanks for watching!" | `INFO TranscriptionFiltered chars=N`, then `TranscriptionEmpty` |
| Exit during the request | `INFO DictationCancelledByShutdown` |

Nothing is pasted in any of these cases. Response bodies are never read
for an error status. `OpenAiCompatibleTranscriberTests` covers each
status, the network, the timeout, cancellation, empty and malformed
bodies, and the multipart request, all with a fake HTTP handler.
`FillerFilterTests` checks that real speech such as "Thank you." or
"Hello world. Thanks for watching!" is never filtered: the filter drops a
transcript only when all of it is one known filler phrase. P16 adds
notifications for these failures; until then they are only logged.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-G1 | One live Groq call | With a real key stored (M-K1) and default settings, dictate "testing one two three" into Notepad. | The text is pasted. The log shows `TranscriptionCompleted request_ms=N chars=N`. Record `request_ms`. | **No.** It needs a real key, physical keys and a microphone. |
| M-G2 | Wrong key | In a new terminal, set `NAAD_GROQ_API_KEY` to `wrong-key-for-testing`, start Naad and dictate a sentence. | The log shows `WARN TranscriptionFailed http_status=401`. Nothing is pasted, Naad keeps running, and the next dictation is accepted. | **No.** It needs physical keys and a microphone. |
| M-G3 | Offline | Turn on airplane mode, dictate a sentence, then turn it off and dictate again. | First `WARN TranscriptionFailed network_error=N` (or `TranscriptionTimedOut`), and nothing is pasted. The second dictation works. | **No.** It changes the network and needs physical keys. |

**Status after P13:** M-G1 to M-G3 have **not been run**.

**Live harness, 2026-10-28 (after P16).** A local, uncommitted explicit test
used the stored real key and Naad's own code against Groq, with speech
synthesized by Windows text-to-speech instead of a microphone:

| Case | Result |
|---|---|
| Two sentences through SilenceGate, Wav and the transcriber | Correct text, `request_ms` about 340 to 610 |
| The same speech at about -31 dBFS (peak 930) | Accepted by the gate, correct text |
| A whole dictation through DictationController (fake microphone, clipboard and focus) | Recording, Transcribing, Inserting, Idle; pasted once; clipboard restored |
| A wrong key | `Unauthorized`, 401 |
| An unknown model; a wrong path | `Rejected`, 404 each |
| An unknown host; a 5 ms timeout; cancelling in flight | `Network` (name resolution); `Timeout`; cancellation thrown |
| 3 s of silence uploaded on purpose, bypassing the gate | Whisper answered "Thank you.", the invention SilenceGate exists to stop; the gate marks it Silent |
| A 27 MB upload | Groq dropped the connection rather than answering 413, so Naad reports `Network`. Naad's 5-minute limit keeps uploads near 9.6 MB. |

This covers the provider end to end except the microphone and the real
keyboard, clipboard and paste; M-G1 itself still needs a person.

## Clipboard insertion (P14): M-I1 to M-I6

Since P14, Naad pastes through the clipboard without losing what was on it:

1. Wait until no modifier (Shift, Ctrl, Alt, Windows) is physically held,
   for up to 1 s. If one still is, nothing is pasted and the clipboard
   isn't touched (`PasteSkippedModifiersHeld`).
2. Copy every clipboard format that is a plain memory block: text, RTF,
   HTML, files copied in Explorer, images (as DIB) and app-specific
   registered formats. GDI-handle and private formats (enhanced
   metafiles, for example) can't be copied and are counted instead
   (`ClipboardFormatsSkipped formats=N`); Office and browsers also offer
   their content in formats that are copied.
3. Write the transcript, marked so Windows leaves it out of clipboard
   history (Win+V), cloud clipboard and clipboard monitors.
4. Send Ctrl+V (`Pasted`).
5. After 500 ms, which gives the app time to read the clipboard, put the
   original back, but only if nothing else wrote to the clipboard since
   Naad did. If you copied something meanwhile, your copy stays
   (`ClipboardRestoreSkippedChanged`).

Opening the clipboard is retried 10 times, 20 ms apart, if another app
holds it. If it still can't be opened, nothing is pasted
(`ClipboardUnavailable`). If Windows rejects the Ctrl+V input, the
original is put back (`PasteRejected`). `TextInserterTests` covers each
of these against a fake clipboard. `WindowsClipboardTests` runs the real
clipboard and is explicit: it never runs in CI. Run it by hand with the
command in its comment.

Until P15, a transcript that wasn't pasted (focus changed, a modifier
held, or the input rejected) was left on the clipboard. Since P15 it is
kept by Naad instead and the clipboard isn't touched; see M-F1 to M-F5.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-I1 | Notepad keeps your clipboard | Copy the word `alpha`. In Notepad, dictate a sentence. Then press Ctrl+V. | The sentence is pasted, then Ctrl+V pastes `alpha`. The log shows `Pasted` and no `Clipboard*` warning. | **No.** It needs physical keys and a microphone. |
| M-I2 | Chrome | Repeat M-I1 in a Chrome text box. | Same as M-I1. | **No.** |
| M-I3 | VS Code | Repeat M-I1 in a VS Code editor. | Same as M-I1. Note if VS Code ever pastes `alpha` instead of the sentence: the 500 ms restore delay would be too short. | **No.** |
| M-I4 | Your newer copy wins | Dictate a long sentence into Notepad. The moment the text appears, select a word and press Ctrl+C (within half a second). Then press Ctrl+V. | Ctrl+V pastes the word you copied, not the original clipboard. The log shows `ClipboardRestoreSkippedChanged`. Retry if you weren't fast enough (the log then shows no such line). | **No.** It needs fast physical input. |
| M-I5 | Not in clipboard history | Turn on Settings > System > Clipboard > Clipboard history. Copy `alpha`, then dictate a sentence into Notepad. Press Win+V. | The history shows `alpha` once, and doesn't show the dictated sentence. | **No.** It needs the Win+V UI. |
| M-I6 | Images and files come back | 1. Copy a screenshot region (Win+Shift+S), dictate into Notepad, then paste into Paint. 2. Copy a file in Explorer, dictate, then paste into a folder. | 1. The screenshot is pasted into Paint. 2. The file is copied into the folder. | **No.** |

**Status after P14:** M-I1 to M-I6 have **not been run**. On 2026-10-28
`WindowsClipboardTests` passed against the real clipboard: the transcript
was written with the history markers, and all 9 formats on the clipboard
at the time came back byte for byte. No contents were printed.

## Focus guard (P15): M-F1 to M-F5

Since P15, Naad remembers the window you were in when you pressed the
shortcut: its handle, thread and process, so a reused handle doesn't
count. Right before any text is sent, after waiting for the modifier keys,
it checks that this window still has focus, still exists, and doesn't run
at a higher integrity level (as administrator), where Windows silently
drops Naad's input. If any check fails, Naad doesn't paste, doesn't switch
windows and doesn't touch the clipboard. It keeps the transcript in
memory and shows a notification titled **Naad kept your text**.

| Cause | Log event |
|---|---|
| Another window, or none, has focus | `WARN PasteSkippedFocusChanged` |
| The window was closed | `WARN PasteSkippedTargetClosed` |
| No window had focus when you pressed the shortcut | `WARN PasteSkippedNoTarget` |
| The window runs as administrator | `WARN PasteSkippedTargetElevated` |
| A modifier was held, the input was rejected, or the clipboard and typing both failed | `PasteSkippedModifiersHeld`, `PasteRejected`, `ClipboardUnavailable` or `TypingIncomplete` |

To use the text, click the notification or choose **Copy last
transcript** in the tray menu, then press Ctrl+V where you want it.
Naad never pastes it itself: opening the menu takes focus away from your
window. The copy stays out of clipboard history (`TranscriptCopied
chars=N`). Only the latest kept transcript is held, in memory only, and
it is gone when Naad exits. The menu item is greyed out until a
transcript has been kept.

If the clipboard can't be opened or written at all, Naad types the text
as Unicode keys instead (`InsertedByTyping chars=N`). That works for any
script and for emoji, and line breaks become Enter. Typing only happens
when nothing was pasted, so text is never inserted twice. If Windows
accepts only part of the keys, the transcript is kept as well
(`TypingIncomplete`).

`FocusGuardTests` covers the decision for every case, including a reused
handle and an unknown integrity level, which never blocks. It also reads
this process's real integrity level. `TextInserterTests` checks that
focus is checked after the modifier wait and before Ctrl+V or typing, and
when typing is used. `UnicodeTypingTests` covers the keys built for
accents, Devanagari, CJK, emoji and line breaks.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-F1 | Switching windows never pastes | In Notepad, dictate a long sentence. Release Right Ctrl and at once Alt+Tab to Chrome, before the text appears. Then click the notification, go back to Notepad and press Ctrl+V. | Nothing is pasted into Chrome or Notepad automatically. `WARN PasteSkippedFocusChanged` appears, then the notification. After the click, `TranscriptCopied` appears and Ctrl+V pastes the sentence. | **No.** It needs physical keys and a microphone. |
| M-F2 | Closed window | Open a second Notepad window, dictate a long sentence into it, and close it (Ctrl+W or the X, choose Don't save) before the text appears. | `WARN PasteSkippedTargetClosed` and the notification. Nothing is pasted anywhere. Copy last transcript works. | **No.** |
| M-F3 | Administrator terminal | Open Windows Terminal or PowerShell with **Run as administrator**. Click in it and dictate a sentence. | `WARN PasteSkippedTargetElevated` and a notification saying Naad can't type into administrator apps. Nothing is typed. Copy last transcript, then Ctrl+V in the terminal, pastes it. | **No.** It needs an elevated window and physical keys. |
| M-F4 | Unicode text | Dictate a sentence with an accented name, such as "Café Müller", into Notepad, and paste a kept transcript with emoji. Then, to check typing, hold the clipboard open with another app if you can, or skip this part and note it. | The accents survive in the paste. If typing ran, `InsertedByTyping` appears and the text matches. | **No.** Forcing the clipboard to be unavailable needs a helper app. |
| M-F5 | Copy last transcript menu | Before any kept transcript, open the tray menu. Then do M-F1 and open it again. Choose the item twice. | First the item is greyed out. Afterwards it is enabled, and choosing it twice copies the same text both times. Nothing is ever pasted by the menu itself. | **No.** It needs the tray menu. |

**Status after P15:** M-F1 to M-F5 have **not been run**. On 2026-10-28 the
explicit `FocusGuardTests.Check_CurrentForegroundWindow_Inserts` passed
against the real desktop: the focused window was identified and accepted,
and a mismatched process id was reported as closed.

M-S4's last expectation (Ctrl+V pastes the transcript) applied before
P15; since P15, M-F1 replaces it.

## MVP integration (P16): M-M1 to M-M8

Since P16, `DictationController` receives everything it uses (microphone,
transcriber, inserter, focus checks, clock) through its constructor and
holds no Win32 code. `DictationControllerTests` runs whole dictations on a
single-threaded stand-in for the UI thread, with fakes only for the
microphone, the transcription service, the clipboard, input and focus. It
covers a normal dictation (one paste, clipboard restored, back to Idle,
and a second dictation), silent and too-short recordings (never
uploaded), short holds and cancels, microphone and recorder failures,
each transcription failure, an empty transcript, every focus decision,
focus lost during the modifier wait, a rejected paste, a busy clipboard
(typed instead), a press while transcribing (ignored, one paste), and
exit during transcription (cancelled, nothing inserted).

The tray icon shows the state: a red dot while listening, an amber dot
while transcribing, and the plain icon when idle; the tooltip says the
same. A soft tone plays once the microphone is open and once it is closed
again (`playSounds`, on by default). Notifications explain microphone errors,
transcription failures, silent recordings and kept transcripts.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-M1 | Tray state | Hold Right Ctrl for 3 s while watching the icon, speak, release. | Red dot while holding, amber dot after release, plain icon once the text is pasted. The tooltip changes with it. | **No.** It needs physical keys and a microphone. |
| M-M2 | Sounds | Dictate once with `"playSounds": true`, then once with `false` (restart Naad in between). Each time, release Right Ctrl and at once type a few letters in Notepad. | With `true`, a short high tone once Naad is listening, a moment after the press (up to about 1.5 s on a laptop whose speakers have gone idle), and a lower one after the release; neither appears in the pasted text. `mic_open_ms` stays well under a second, and the letters typed after the release appear at once. With `false`, no tones. | **No.** |
| M-M3 | Transcription error notification | Repeat M-G2 (wrong key). | A notification titled **Naad couldn't transcribe** says the key was rejected and how to store one. It shows no key and no server text. | **No.** |
| M-M4 | Nothing heard | Mute the microphone, hold Right Ctrl for 2 s, release. | A notification says Naad didn't hear anything. Nothing is uploaded or pasted (M-W1's log). | **No.** |
| M-M5 | Exit while recording | Hold Right Ctrl, and while holding, right-click the tray icon and choose Exit with the mouse. Then release. | Naad exits. Nothing is pasted, the microphone indicator disappears, and the log ends with `AppStopped`. (The click may cancel the dictation first; that is fine.) | **No.** |
| M-M6 | Exit while transcribing | Dictate a long sentence, release, and at once choose tray > Exit. | Naad exits. Nothing is pasted after Exit, and your clipboard is what it was. | **No.** |
| M-M7 | Ten dictations in a row | Dictate ten short sentences into Notepad, one after another, as fast as feels natural. | Each sentence appears exactly once, in order. No `DictationFailed`. The icon ends plain. | **No.** |
| M-M8 | End to end in three apps | Run M-I1 to M-I3 (Notepad, Chrome, VS Code) with M-H1 in one sitting. | Text appears once in each app, and the clipboard comes back each time. | **No.** |

**Status after P16:** M-M1 to M-M8 have **not been run**. A smoke run of
the Release exe passed (start, `AppStarted`, no errors); the state icons
and tones are created at startup, so that run also covered their creation.

## Settings window (P17): M-U1 to M-U6

Since P17, **Settings...** in the tray menu opens a window for every setting
and the API key. Saving applies the change at once, without a restart; if a
dictation is running, the change waits until it ends (`SettingsApplyDeferred`,
then `SettingsApplied`). The key is write-only: the window says where the key
comes from (stored, missing, unreadable, or `NAAD_GROQ_API_KEY`) and never
shows it. Without a usable key Naad still starts and opens the window, and
pressing the shortcut shows **Naad needs an API key** instead of recording.
Unit tests cover the field checks and every message (`SettingsInputTests`),
shortcut capture (`ChordCaptureTests`, and `ChordMatcherTests` for capture
never raising dictation events), the key test against a fake server
(`OpenAiCompatibleTranscriberTests`), and the controller without a
transcriber (`DictationControllerTests`).

Before these checks, copy `%APPDATA%\Naad\settings.json` somewhere safe, and
restore it afterwards.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-U1 | No key at startup | 1. Exit Naad. In a new PowerShell window, run `$env:NAAD_GROQ_API_KEY = 'not a key'` and start Naad from it. 2. Hold Right Ctrl in Notepad for 2 s. 3. Close Settings, exit Naad, close that PowerShell window. | 1. Naad starts and opens **Naad settings** at once; the API key line says the variable holds characters a key can't have. The log shows `ApiKeyInvalid`, `AppStarted`, `SettingsWindowOpened`. 2. A **Naad needs an API key** notification; the microphone indicator never appears; the log shows `DictationNeedsApiKey`. Clicking the notification brings Settings back. | Partly: the startup half (window opens, log events) can be checked by starting the exe and reading its window and log. |
| M-U2 | Key: save and test | 1. In Settings, paste a wrong key and click **Test key**. 2. Paste your real key, click **Test key**. 3. Click **Save**, then dictate a sentence into Notepad. 4. Open Settings again. 5. Restart Naad and dictate again. | 1. "The provider rejected the key." 2. "The provider accepted the key." 3. The window closes; the log shows `ApiKeyStored`, `ApiKeyLoaded`, `SettingsSaved`, `SettingsApplied`; the text is pasted without a restart. 4. The key box is empty and the status says a key is stored. 5. It still works. The key never appears on screen, in `naad.log` or in settings.json. | **No.** It needs a real key and a voice. |
| M-U3 | Shortcut capture | 1. Click **Change...**, hold Left Ctrl, press Space, release. 2. Click **Save**. Hold Left Ctrl+Space in Notepad and dictate. 3. Click **Change...** again, press Esc. 4. Click **Change...**, then click another app's window and type a few letters there. 5. Click **Change...**, hold Right Ctrl and speak. | 1. The shortcut reads "Left Ctrl+Space"; no space is typed anywhere. 2. Dictation starts with the new shortcut, and the tray tooltip names it; Right Ctrl alone no longer dictates. 3. The old shortcut is shown again; the window stays open. 4. Capture ends as the window loses focus; the letters appear in the other app. 5. Nothing is recorded: no tone, no red dot, no microphone indicator; releasing captures "Right Ctrl". | **No.** Physical keys. |
| M-U4 | Other settings apply live | Change, one at a time and saving each: the microphone (to another device), the language (`en`), the model (`whisper-large-v3`), **Play a tone** off, **Write a diagnostic log** off then on. Dictate after each. | Each change works on the next dictation without a restart: the other device is used, transcripts come back, the tones stop, and `naad.log` stops growing while logging is off. After a restart every change is still there, and settings.json holds them. | **No.** |
| M-U5 | Change during a dictation | Open Settings and leave it open. Click in Notepad, hold Right Ctrl and speak; while holding, click the settings window, change the language and click **Save**. Release. | The click cancels the dictation (as in M-M5) and nothing is pasted. If a dictation is still running when **Save** is clicked, the log shows `SettingsApplyDeferred` and then `SettingsApplied` once it ends; otherwise `SettingsApplied` at once. The next dictation uses the new language. | **No.** |
| M-U6 | Bad values are refused | Type `http://example.com/v1` as the base URL and click **Save**; then `whisper large` as the model; then `english` as the language; then a key with a space in it. | Each time the window stays open and names the field to fix; settings.json and the stored key are unchanged. | **No.** The rules are unit tested in `SettingsInputTests`. |

**Status after P17:** M-U1 to M-U6 have **not been run** by a person.

## Recording overlay (P18): M-O1 to M-O3

Since P18, a small bar near the bottom of the screen shows **Listening**
with a level meter while you hold the shortcut, then **Transcribing**, and
disappears when Naad is idle. It appears on the monitor of the window you
are dictating into. It never activates, takes no keyboard input, lets
clicks through, and isn't in Alt+Tab or the taskbar: if it ever became the
foreground window, the focus guard would refuse to paste. The meter maps
loudness from -60 dBFS (empty) to full scale on a decibel scale and falls
smoothly (`LevelMeterTests`). `RecordingOverlayTests` checks the window
styles, and its explicit test shows the overlay over the real foreground
window and checks that window keeps the foreground.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-O1 | Never takes focus | Dictate a sentence into Notepad, then Chrome, then VS Code, watching the overlay each time. Once, while holding the shortcut, click on the overlay itself. | The overlay shows **Listening** while you hold and **Transcribing** after release, then disappears. The text is pasted in all three apps. The app's title bar stays active throughout. Clicking the overlay clicks the window under it (and, as any click, cancels that dictation). The log shows no `PasteSkippedFocusChanged`. | Partly: the explicit `RecordingOverlayTests` test showed the overlay over the real foreground window on 2026-10-29 and the foreground window never changed. Dictation into the three apps: NOT RUN. |
| M-O2 | Level meter | Hold the shortcut and stay silent for 2 s, then speak normally, then loudly, then stop. | The bar stays near empty in silence, moves with your voice, nearly fills when loud, and sinks smoothly within about half a second after you stop. | **No.** It needs a microphone. |
| M-O3 | Monitor and Alt+Tab | With two monitors, dictate into a window on each. While holding the shortcut once, press Alt+Tab, hold Alt and look at the switcher, then release everything. | The overlay appears near the bottom of the monitor holding the window you dictate into. The Alt+Tab switcher and the taskbar never list it. (Alt+Tab cancels that dictation, as any other key does.) | **No.** |

**Status after P18:** M-O1 to M-O3 have **not been run** by a person.

## Robustness (P19): M-R1 to M-R6

Since P19:

- The microphone is opened afresh for each recording, so a microphone
  plugged in while Naad runs is used as soon as Windows makes it the
  default (or it is the one chosen in Settings). One unplugged
  mid-recording no longer loses the dictation: what was heard until then
  is transcribed, and the log shows `MicrophoneDisconnected`.
- The keyboard hook is installed afresh after sleep and when the session
  is unlocked (`HookReinstalled`). Every 2 seconds, while keys are held,
  Naad asks Windows which of them are really still down, and forgets key
  releases it missed, as happens behind Ctrl+Alt+Del or a UAC prompt
  (`ShortcutKeysResynced`).
- A transcription request that never reached the server (no DNS, refused
  connection, failed TLS handshake) is sent once more after 1 second,
  within the same 20-second limit (`TranscriptionRetried`). A request the
  server may have received is never sent twice.
- "Paste last transcript" is **Copy last transcript**, built in P15: a
  paste from the tray menu would go to the wrong window.

`ChordMatcherTests` covers the key resync and `RetryPolicyTests` the retry
policy, including the transcriber against a fake server.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-R1 | Microphone unplugged while recording | With a USB or Bluetooth headset as the default microphone, hold Right Ctrl in Notepad, speak for 2 s, unplug the headset (or switch it off), keep speaking 1 s, release. Then dictate again. | The words spoken before unplugging are pasted, or, if too little was heard, the usual nothing-heard notification appears. The log shows `MicrophoneDisconnected mm_result=N`. The next dictation records from the microphone Windows now uses, or shows "No microphone was found". Naad never needs a restart. | **No.** It needs a removable microphone. |
| M-R2 | Microphone plugged in while running | Start Naad with only the built-in microphone. Plug in a headset and make it the default input in Settings > System > Sound. Dictate. | The headset is used (its mute switch silences the dictation) without restarting Naad. | **No.** |
| M-R3 | Sleep and resume | Put the PC to sleep (Start > Power > Sleep) for at least a minute, wake it, sign in, then dictate into Notepad at once. | The dictation works. The log shows `HookReinstalled` after waking. | **No.** It needs a real sleep. |
| M-R4 | Lock and unlock | Press Win+L, sign in again, then dictate. | The dictation works. The log shows `HookReinstalled` after signing in. | **No.** |
| M-R5 | Key release lost to the secure desktop | Hold Right Ctrl in Notepad and, while holding it, press Ctrl+Alt+Del with the other hand. Release everything on the blue screen, click **Cancel**, and wait 3 s. Then dictate normally. | Within about 2 seconds of returning, the dictation ends (it is transcribed, or dropped as too short or silent) instead of recording for five minutes; the tray icon leaves the red dot. The log shows `ShortcutKeysResynced`. The next dictation works with a single press. | **No.** Physical keys. |
| M-R6 | Network loss | 1. Turn off Wi-Fi (or unplug the network), dictate a sentence. 2. Turn it back on and dictate again. 3. Dictate a long sentence and turn the network off right after releasing. | 1. After about a second, **Naad couldn't transcribe** says it couldn't reach the service; the log shows `TranscriptionRetried` once, then `TranscriptionFailed`. 2. Works at once. 3. A notification, never a double paste; if the request had already reached the server, there is no `TranscriptionRetried`. | **No.** |

**Status after P19:** M-R1 to M-R6 have **not been run** by a person.

## Quality features (P21): M-Q1 to M-Q5

Since P21, Settings has three more options:

- **Vocabulary** (`vocabulary`): names and terms you use, sent with each
  recording as Whisper's prompt so they come back spelled right. It goes
  to the transcription provider but never into the log.
- **Hands-free** (`handsFree`): press the shortcut once to start and again
  to stop, instead of holding it. The other rules stay: another key during
  the starting press cancels, a second press within 300 ms is too short,
  and recording stops by itself after 5 minutes.
- **Start Naad when I sign in**: a `Naad` value in
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The registry is
  the only record of it; it isn't in settings.json.

Language choice, also planned here, was built in P17. Unit tests cover the
new settings (`SettingsStoreTests`, `SettingsInputTests`), the prompt field
(`OpenAiCompatibleTranscriberTests`), hands-free dictation
(`DictationControllerTests`), the tooltips (`TrayIconTests`) and the Run
entry against a throwaway registry key (`StartupEntryTests`).

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-Q1 | Vocabulary | 1. Dictate a sentence with an unusual name or term (a product, a colleague's name) and note how it is spelled. 2. Add it to **Vocabulary** in Settings, save, and dictate the same sentence. 3. Open `naad.log`. | 2. The term comes back spelled as in the vocabulary (Whisper treats the prompt as a hint, so try twice before calling it a failure). 3. The log never contains the vocabulary. | **No.** It needs a voice. |
| M-Q2 | Hands-free dictation | Tick **Hands-free**, save. In Notepad, press and release Right Ctrl, speak two sentences with your hands off the keyboard, then press Right Ctrl again. | Recording starts on the first press (tone, red dot) and keeps going after release; the tooltip says to press Right Ctrl again. The second press transcribes and pastes. | **No.** Physical keys and a voice. |
| M-Q3 | Hands-free edge cases | With **Hands-free** on: 1. Press Right Ctrl twice quickly. 2. Press Right Ctrl+C to copy something. 3. Start a dictation and wait 5 minutes. 4. Untick **Hands-free**, save, and dictate by holding. | 1. Nothing is recorded or pasted (too short). 2. The copy works and no dictation starts. 3. Recording stops by itself at 5 minutes and is transcribed. 4. Holding works again; release ends the dictation. | **No.** |
| M-Q4 | Start at login | 1. Tick **Start Naad when I sign in**, save. 2. Sign out and in again (or restart). 3. Run `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Naad`. | 2. Naad's tray icon appears after sign-in without starting it by hand. 3. The value is the quoted path of the Naad.exe you ticked it from. The log shows `StartAtLoginEnabled`. | **No.** It needs a sign-in. |
| M-Q5 | Stop starting at login | 1. Untick **Start Naad when I sign in**, save. 2. Sign out and in again. 3. Run the `reg query` from M-Q4. | 2. Naad doesn't start. 3. "The system was unable to find the specified registry key or value." The log shows `StartAtLoginDisabled`. Other programs' Run entries are untouched. | **No.** |

**Status after P21:** M-Q1 to M-Q5 have **not been run** by a person.

## Transcription on this PC (P20): M-LW1 to M-LW4

The roadmap names these M-W1 to M-W4; they are M-LW here because P11's
recording gates already use M-W1 to M-W3.

Since P20, **Transcribe with** in Settings chooses between the cloud
service and Whisper on this PC. **Download model** fetches whisper.cpp's
`ggml-base.bin` (148 MB) from Hugging Face once, checks its SHA-256, and
keeps it in `%LOCALAPPDATA%\Naad\models`. With **This PC** chosen, audio
never leaves the machine. Naad never falls back from one provider to the
other: without the model, the shortcut asks to finish setup and records
nothing. Unit tests cover provider selection (`ProviderSelectionTests`),
the download and checksum against a fake server (`LocalModelTests`), the
setting (`SettingsStoreTests`) and a missing model
(`LocalWhisperTranscriberTests`).

**Measured, 2026-10-29,** by the explicit test in
`LocalWhisperTranscriberTests`, on the maintainer's PC (12 logical
processors), with 9.8 s of synthetic English speech:

| Run | Wall time | CPU time | Average load |
|---|---|---|---|
| First dictation (loads the model) | 1.99 s | 8.9 s | 37% of 12 logical processors |
| Later dictations | 1.49 s | 8.9 s | 50% of 12 logical processors |

Whisper uses half the logical processors (at most 8) while it runs, and
the model takes about 150 MB of memory once loaded. The real download
passed its checksum on the same day.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-LW1 | Download the model | In Settings, choose **This PC**, click **Download model**, wait, then **Save**. Open `%LOCALAPPDATA%\Naad\models`. | The status counts up to 100% and says the model is downloaded. `ggml-base.bin` is 147,951,465 bytes; no `.download` file is left. The log shows `LocalModelDownloaded download_ms=N`. | Partly: the explicit test downloaded the real file through the same code and its checksum matched (2026-10-29). Settings UI: NOT RUN. |
| M-LW2 | Dictate offline | Turn off Wi-Fi (or unplug the network). Dictate two sentences into Notepad. | Both are pasted. The first takes a moment longer while the model loads. No network error appears. | **No.** It needs a voice; the explicit test transcribed synthetic speech with no network code involved. |
| M-LW3 | No silent fallback | Choose **This PC** and save, then exit Naad, delete `ggml-base.bin`, start Naad and hold the shortcut. Then choose **Cloud** with a valid key and dictate. | Without the model, Naad opens Settings at start, and the shortcut shows **Naad isn't set up yet**; nothing is recorded or uploaded. With **Cloud**, dictation works again. | **No.** |
| M-LW4 | Interrupted or damaged download | 1. Start **Download model** and close the settings window half-way. 2. Start it again and turn off the network half-way. | 1. and 2. No `ggml-base.bin` or `.download` file is left; Settings says the download failed and offers it again. | **No.** The checksum rejection is unit tested. |

**Status after P20:** M-LW1 to M-LW4 have **not been run** by a person.

## Packaging (P22): M-P1 to M-P4

Since P22, pushing a version tag such as `v0.2.0` runs
`.github/workflows/release.yml`: it tests, publishes a self-contained
win-x64 build stamped with the tag's version, packs it with Velopack
(`vpk pack`, package id `Naad.App`) and publishes a GitHub Release with
`Naad.App-win-Setup.exe`, a portable zip and the update feed. The
installer is per user: it installs into `%LOCALAPPDATA%\Naad.App`, which is
deliberately not `%LOCALAPPDATA%\Naad`, where Naad keeps its user data.
Uninstalling runs Naad's hook, which removes the start-at-sign-in entry.

**Dry run, 2026-10-29:** the workflow's publish and pack commands, run
locally with version 0.0.1, produced `Naad.App-win-Setup.exe` (64 MB), the
portable zip, the full package and `releases.win.json`; Naad.exe reported
FileVersion 0.0.1.0, and the Whisper native libraries were included. The
installer wasn't run on the maintainer's PC, and no tag was pushed, so the
workflow itself hasn't run.

Run these on a clean Windows 11 virtual machine, with a release built by
the workflow.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-P1 | Install | Download `Naad.App-win-Setup.exe` from the release and run it; follow the SmartScreen steps in the README. | It installs without an administrator prompt and starts Naad: the tray icon appears and Settings opens to ask for a key. Settings > Apps lists **Naad** with the release's version. A Start menu shortcut exists. | **No.** It needs a clean VM. |
| M-P2 | Run and dictate | Enter a key in Settings, then dictate into Notepad. Restart the VM; start Naad from the Start menu. | Dictation works, as in M-M8. After the restart, Naad starts from the shortcut with its settings and key. | **No.** |
| M-P3 | Uninstall | Tick **Start Naad when I sign in**, save, then uninstall from Settings > Apps. Check `%LOCALAPPDATA%\Naad.App`, the Start menu and `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Naad`. | Naad exits and is removed. `%LOCALAPPDATA%\Naad.App` and the Start menu shortcut are gone, and the Run value no longer exists. | **No.** |
| M-P4 | User data after uninstall | After M-P3, check `%APPDATA%\Naad` and `%LOCALAPPDATA%\Naad`, then reinstall. Then delete both folders. | Both folders are still there after uninstalling, and a reinstall picks up the settings and key. Once deleted, nothing of Naad is left. | **No.** |

**Status after P22:** M-P1 to M-P4 have **not been run**: they need a clean
VM and a published release.

## Auto-update (P23): M-P5 and M-P6

Since P23, an installed Naad (one set up by `Naad.App-win-Setup.exe`)
asks GitHub Releases for a newer version a minute after start, then once a
day, and when **Check for updates** in the tray menu is clicked. A build
run from source never checks, and the menu item isn't shown. When an update
exists, a notification offers it; nothing is downloaded until it is
clicked. Clicking during a dictation asks to click again afterwards. The
update downloads, Naad exits the normal way, and Velopack installs the new
version and starts it. Settings, the key and the model live outside the
install folder, so they are kept. `AppUpdaterTests` checks that a copy
that isn't installed does nothing; `TrayIconTests` the offer's text.

These need two published releases, N and N+1, built by the release
workflow, and a clean Windows 11 virtual machine.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-P5 | Update N to N+1 | 1. Install release N (M-P1), enter a key, change the shortcut and tick **Hands-free** in Settings. 2. Publish N+1. 3. Choose **Check for updates** in the tray menu. 4. Click the notification. | 3. **Naad update** offers N+1; nothing has been downloaded yet. The log shows `UpdateAvailable major=… minor=… patch=…`. 4. Naad closes, updates and starts again within a minute or so. Settings > Apps shows N+1. The shortcut, **Hands-free** and the key still work without being entered again. | **No.** It needs two releases. |
| M-P6 | Up to date, offline and mid-dictation | 1. On N+1, choose **Check for updates**. 2. Disconnect the network and check again. 3. Reinstall N, start a hands-free dictation, and click an update notification while it records. | 1. "Naad is up to date." (`UpdateNotAvailable`). 2. A notification says the check failed (`UpdateCheckFailed`); Naad keeps working. 3. The dictation isn't interrupted; the notification asks to click again once it is done, and doing so updates. | **No.** |

**Status after P23:** M-P5 and M-P6 have **not been run**: they need
published releases and a clean VM.

## Local first (P24): M-LF1 to M-LF5

Since P24, Naad transcribes on this PC unless the user chooses the cloud,
so the checks above that dictate through Groq (M-S2 to M-S5, M-G1 to M-G3
and others) need **Cloud** chosen in Settings first. A settings file from
before P24 (version 1) that never chose a provider keeps **Cloud**.
`SettingsStoreTests` covers the default and the migration.

| ID | Check | Steps | Expected | Automated? |
|---|---|---|---|---|
| M-LF1 | First start, no key | Exit Naad. Rename `%APPDATA%\Naad` and `%LOCALAPPDATA%\Naad\models`, and clear `NAAD_GROQ_API_KEY`. Start Naad. | Settings opens with **This PC** chosen and **Download model** offered. Holding the shortcut shows **Naad isn't set up yet** and records nothing. After **Download model** and **Save**, dictating into Notepad pastes the words. No API key was asked for. | **No.** The default is unit tested. |
| M-LF2 | Older settings keep the cloud | Exit Naad. Write `{ "version": 1, "dictationShortcut": "RightCtrl" }` to `%APPDATA%\Naad\settings.json`, with a stored key. Start Naad and dictate. | Naad transcribes through the cloud, as before. settings.json now says `"version": 2` and `"transcriptionProvider": "Cloud"`; a `settings.json.v1-….bak` holds the original. The log shows `SettingsMigrated from_version=1`. | **No.** The migration is unit tested. |

| M-LF3 | Graphics card | On a PC with a discrete graphics card, dictate twice into Notepad with **This PC** chosen, then open the log. | The log shows `LocalModelLoaded vulkan=1 discrete_gpu=1 gpu_device=N`. Both dictations are pasted; the second takes about a second. | Partly: the explicit test loaded Vulkan and picked the RTX 3050 (`gpu_device=1`) on the maintainer's PC (2026-10-30). Dictation by voice: NOT RUN. |
| M-LF4 | No Vulkan driver | On a PC or VM without a Vulkan driver (`C:\Windows\System32\vulkan-1.dll` missing), dictate into Notepad with **This PC** chosen. | The words are pasted after about two seconds. The log shows `LocalModelLoaded vulkan=0`. | **No.** It needs a PC without Vulkan. |
| M-LF5 | Choose the accurate model | In Settings, choose **Large v3 Turbo** under **Model on this PC**. Click **Download model**, wait, then **Save**. Dictate a sentence with a technical term (such as "Kubernetes") into Notepad, twice. Switch back to **Base** and save. | The status first asks for 574 MB; the model list is disabled while it downloads. After the download, `ggml-large-v3-turbo-q5_0.bin` (574,041,195 bytes) sits next to `ggml-base.bin`. On a PC with a discrete graphics card, the second dictation takes about 1.5 s and spells the term right. Switching back to **Base** needs no download. The log shows `LocalModelDownloaded model=1`. | Partly: on 2026-10-30, Naad's transcriber ran the turbo file (its SHA-256 checked by hand) on the RTX 3050: 1.3 s for 15.4 s of speech, after a 23 s first run, with "Kubernetes" right. Settings and voice: NOT RUN. |

**Measured, 2026-10-30,** with Whisper.net 1.9.1 on the maintainer's PC
(i5-13420H with 12 logical processors, Intel UHD Graphics, RTX 3050 6 GB
Laptop), for Windows text-to-speech clips of 3.3 s and 15.4 s, with the
model already loaded:

| Model | Processor (6 threads) | Intel UHD (integrated) | RTX 3050 (discrete) |
|---|---|---|---|
| base | 1.8 to 2.3 s | 0.55 to 0.95 s | 0.4 to 0.8 s |
| large-v3-turbo (q5_0) | 30 to 32 s | 4.5 to 16.5 s | 1.3 to 1.7 s |

Loading the model took 1.3 to 2.3 s (base) and about 4 s (turbo) on the
RTX 3050. The first run of each model on the RTX 3050 took 22 s (base) and
58 s (turbo) more while the driver compiled shaders; later processes reused
them. Both models spelled the sentences the same way except one term:
base wrote "Kubernetes" as "Cabernet's", turbo got it right.

**Status after P24:** M-LF1 to M-LF5 have **not been run** by a person.

## v0.1.0 checklist

`v0.1.0` is tagged only after every check below has passed on Windows 11
in Notepad, Chrome and VS Code, and the maintainer approves. Status as of
2026-10-28. "Partly" means an automated harness covered part of it and the
rest still needs a person.

| Area | Checks | Status |
|---|---|---|
| Vertical slice | M-S1 to M-S5 | M-S1 passed (P3). M-S2 to M-S5: reported passing in P3 without numbers; rerun with the current build. |
| Tray and lifecycle | M-T1 to M-T6 | M-T1 to M-T4 passed (P4 harness). M-T5, M-T6: NOT RUN. |
| Log | M-L1 | Partly (P5 harness). Dictation part: NOT RUN. |
| Settings | M-C1 to M-C4 | Passed (P6 harness). |
| Shortcut | M-SC1, M-SC2 | M-SC1 partly (P7 harness). M-SC2: NOT RUN. |
| Keyboard hook | M-H1 to M-H5 | M-H4 partly (P8 harness). Others: NOT RUN. |
| Dictation state | M-D1, M-D2 | NOT RUN. |
| Microphone | M-A1 to M-A3 | NOT RUN. |
| Recording gates | M-W1 to M-W3 | NOT RUN. |
| API key | M-K1 | Partly: the harness stored and used a real key (see the P12 section). Notepad steps: NOT RUN. |
| Provider | M-G1 to M-G3 | Partly: the live harness passed (see the P13 section). Dictation by voice: NOT RUN. |
| Clipboard | M-I1 to M-I6 | NOT RUN. Real-clipboard harness passed (P14). |
| Focus guard | M-F1 to M-F5 | NOT RUN. Real-desktop focus check passed (P15). |
| MVP | M-M1 to M-M8 | NOT RUN. |
