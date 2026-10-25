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
| M-S5 | Microphone blocked | 1. In Settings > Privacy & security > Microphone, turn off **Let desktop apps access your microphone**. 2. Hold Right Ctrl, speak, release. 3. Turn the setting back on. | Naad does not crash. Note which happened: `WARN MicrophoneUnavailable mm_result=N`, or a recording of silence. With silence, Whisper may invent a phrase such as "Thank you." and Naad will paste it; note that too. P10 and P11 must handle the silent case. |

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
| M-D1 | Five-minute limit | In Notepad, hold Right Ctrl for just over 5 minutes, speaking now and then, then release. | At 5 minutes the log shows `RecordingMaxDurationReached`, then `RecordingStopped held_ms=300000` (about), and the text is pasted without waiting for the release. The release does nothing. | **No.** It needs physical keys and a microphone. |
| M-D2 | Press again right after a cancel | Press Right Ctrl+C, then within a second hold Right Ctrl and dictate a sentence. | The log shows `DictationCancelledByKey`, then a new `RecordingStarted`, and the sentence is pasted. No `DictationFailed` or `MicrophoneUnavailable` appears. | **No.** It needs physical keys and a microphone. |

**Status after P9:** M-D1 and M-D2 have **not been run**. A smoke run of
the real exe (start, `AppStarted`, no errors) passed.

## Known limits of the slice

These are fixed in later phases:

- The clipboard is overwritten by every dictation. P14 restores it.
- Silence is not detected. P11 adds that.
- Pasting into an administrator window fails silently. P15 handles it.
