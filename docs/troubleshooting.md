# Troubleshooting

Start with the log. It is at `%LOCALAPPDATA%\Naad\logs\naad.log`, and it
holds event names and numbers only, never what you said
([privacy.md](privacy.md)), so it is safe to attach to a bug report. To
watch it live while you try something:

```powershell
Get-Content "$env:LOCALAPPDATA\Naad\logs\naad.log" -Wait -Tail 20
```

The log events named below are what to look for.

## Installing and starting

**"Windows protected your PC" when running the installer.** Naad isn't
code-signed yet. Click **More info**, check that the file is
`Naad.App-win-Setup.exe` from this repository's Releases page, then click
**Run anyway**.

**I can't see the icon.** Windows 11 hides new tray icons behind the **^**
(show hidden icons) button. Drag the teal Naad icon onto the taskbar to keep
it visible.

**Naad starts and Settings opens.** It needs one thing before you can
dictate: the Whisper model (click **Download model**, then **Save**), or,
with **Cloud** chosen, an API key.

## Holding the shortcut does nothing

- **Your keyboard has no Right Ctrl.** Some laptops have a Copilot key in
  its place. In Settings, click **Change...** next to **Shortcut** and press
  the keys you want instead.
- **A notification says "Naad isn't set up yet".** Download the model in
  Settings, or store an API key if you transcribe in the cloud. The log
  shows `DictationNeedsApiKey`.
- **A quick tap.** A press shorter than 300 ms is ignored
  (`DictationIgnoredShortHold`). Hold the key while you speak.
- **Another key or a click while holding** cancels the dictation on purpose,
  so shortcuts such as Right Ctrl+C keep working (`DictationCancelledByKey`,
  `DictationCancelledByMouse`).
- **Hands-free is on.** Then you press once to start and once more to
  stop; the tooltip says so.

## Nothing is typed after I let go

- **A notification says "Naad kept your text".** The window changed before
  the text was ready, the window was closed, or it runs as administrator,
  where Windows doesn't let Naad type. Choose **Copy last transcript** in
  the tray menu, then press Ctrl+V where you want it. The log shows which:
  `PasteSkippedFocusChanged`, `PasteSkippedTargetClosed` or
  `PasteSkippedTargetElevated`.
- **A notification says Naad didn't hear anything.** The recording was
  silent (`RecordingSilent`). Check the microphone in Settings > System >
  Sound > Input, and that it isn't muted.
- **"Naad can't use the microphone".** The notification says why. Most
  often, Settings > Privacy & security > Microphone blocks desktop apps
  (`MicrophoneBlocked`); click the notification to open that page. Another
  app holding the microphone shows `MicrophoneInUse`.
- **"Naad couldn't transcribe"** comes with the reason: the key was
  rejected, the service is busy, or it couldn't be reached
  (`TranscriptionFailed`). Only the cloud can fail this way.
- **You still held Shift, Alt or Windows** a second after letting go of the
  shortcut. Naad doesn't paste then, because the paste would become a
  different shortcut (`PasteSkippedModifiersHeld`).

## The first dictation is slow

The first dictation after Naad starts loads the model, which takes a second
or two. On a PC with a graphics card, the very first dictation with each
model can take 20 to 60 seconds while the graphics driver prepares itself;
the driver keeps that work, so later dictations are fast.

**Large v3 Turbo is very slow.** Without a graphics card it takes about
30 seconds per sentence. Use **Base** on such PCs. The log line
`LocalModelLoaded` says whether the graphics card is used: `vulkan=1` means
yes.

## The words are wrong

- Add names and terms you use to **Vocabulary** in Settings. Whisper treats
  them as hints, so they help but don't guarantee a spelling.
- Set **Language** if you always speak the same one.
- On a PC with a graphics card, **Large v3 Turbo** is noticeably more
  accurate than **Base**.

## Sounds

The start and stop tones are off by default. Turn them on with **Play a
tone** in Settings. On some laptops the speakers go to sleep between sounds,
and waking them takes a second or more: the start tone then comes that long
after the press. Wait for it before speaking.

## Other apps

**My clipboard came back as something else.** Naad restores your clipboard
half a second after pasting, unless you copied something new meanwhile. An
app that reads the clipboard later than that could paste your previous
clipboard instead of the transcript; tell us which app.

**Typed text looks garbled in Notepad.** When the clipboard can't be
opened at all, Naad types the text key by key instead (`InsertedByTyping`).
Windows 11 Notepad drops or mangles text typed that fast. It only happens
while another app holds the clipboard; dictate again once it lets go.

## Settings and the key

- **settings.json was damaged.** Naad keeps the broken file as
  `settings.json.corrupt-<time>.bak` and starts with defaults
  (`SettingsCorrupt`). [settings.md](settings.md) describes every recovery
  case.
- **The stored key can't be read**, for example after copying the folder to
  another PC or Windows account (`ApiKeyUnreadable`). Enter it again in
  Settings.

## Reporting a bug

Open an issue on GitHub with what you did, what you expected, what
happened, your Windows version (`winver`), and the relevant lines of
`naad.log`. Never paste a transcript or an API key.
