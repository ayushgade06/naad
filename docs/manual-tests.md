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
| M-S4 | Focus change blocks the paste | In Notepad, hold Right Ctrl and speak. **While still holding it**, click the console window with the mouse, then release. | The log shows `WARN PasteSkippedFocusChanged`. No text is inserted anywhere. Pressing Ctrl+V in Notepad afterwards pastes the transcript. |
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
| M-T1 | Starts in the tray, no window | Launch Naad either way. Look in the notification area; Windows 11 may hide new icons behind the **^** (show hidden icons) button. | A teal icon with white sound-wave bars appears, with the tooltip "Naad: hold Right Ctrl to dictate". No console or other window opens. | Yes. The P4 UI Automation harness checks the process has no window and finds the icon by name. |
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

## Known limits of the slice

These are fixed in later phases:

- The clipboard is overwritten by every dictation. P14 restores it.
- Silence is not detected. P11 adds that.
- Pasting into an administrator window fails silently. P15 handles it.
- Holding Right Ctrl while clicking or scrolling with the mouse (for
  example Ctrl+click) still counts as dictation, because only keyboard keys
  cancel. P8 handles it.
