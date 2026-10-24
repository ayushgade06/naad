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
   window open: it shows what Naad is doing. Press Ctrl+C there to quit.

## Vertical slice: M-S1 to M-S5

| ID | Check | Steps | Expected |
|---|---|---|---|
| M-S1 | Missing key | Run Naad in a new window where `NAAD_GROQ_API_KEY` is not set. | It prints how to set the key, exits with code 1 (`$LASTEXITCODE`), and prints no key. |
| M-S2 | Dictate into Notepad | 1. Open Notepad and click in it. 2. Hold **Right Ctrl**, say "hello world this is a test", release. 3. Repeat 3 times. | The spoken text appears at the cursor each time. The console shows `Recording (microphone opened in N ms)...`, `Recorded`, `Transcribed`, `Pasted` and `Release to text: N ms`. Record the three latencies. |
| M-S3 | Ctrl shortcuts keep working | 1. In Notepad, use **Left** Ctrl+C and Ctrl+V. 2. Select text and press **Right** Ctrl+C, then paste with Ctrl+V. 3. Tap Right Ctrl as quickly as possible, 5 times, waiting 3 s between taps. | 1. Copy and paste work, and nothing is recorded. 2. Copy works, the console shows `Cancelled`, and nothing is inserted. 3. Recording starts on key-down, so each tap prints `Recording (microphone opened in N ms)...` and then, on release, `Ignored: held for N ms.` Nothing is inserted. A tap must never leave a recording running; the next press always starts a new one. |
| M-S4 | Focus change blocks the paste | In Notepad, hold Right Ctrl and speak. **While still holding it**, click the console window with the mouse, then release. | The console shows `Focus changed while transcribing: not pasted`. No text is inserted anywhere. Pressing Ctrl+V in Notepad afterwards pastes the transcript. |
| M-S5 | Microphone blocked | 1. In Settings > Privacy & security > Microphone, turn off **Let desktop apps access your microphone**. 2. Hold Right Ctrl, speak, release. 3. Turn the setting back on. | Naad does not crash. Note which happened: `Microphone unavailable`, or a recording of silence. With silence, Whisper may invent a phrase such as "Thank you." and Naad will paste it; note that too. P10 and P11 must handle the silent case. |

## Known limits of the slice

These are fixed in later phases:

- The clipboard is overwritten by every dictation. P14 restores it.
- Silence is not detected. P11 adds that.
- Pasting into an administrator window fails silently. P15 handles it.
- Holding Right Ctrl while clicking or scrolling with the mouse (for
  example Ctrl+click) still counts as dictation, because only keyboard keys
  cancel. P8 handles it.
