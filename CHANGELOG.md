# Changelog

All notable changes to Naad are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **A language model on this PC**, for the AI steps that follow. Settings
  has a new **AI** tab: download Small (Qwen2.5-1.5B, 1.1 GB) or Large
  (Qwen3-4B, 2.5 GB), checked against its SHA-256. It runs on the graphics
  card through Vulkan or on the processor, loads on first use and unloads
  after 10 idle minutes.
- **Or a cloud chat API**: any OpenAI-compatible `/chat/completions`
  endpoint, such as Groq, OpenAI, OpenRouter, Ollama or LM Studio, with its
  own encrypted key.

- **Cleanup before pasting**, AI by default: filler words, stutters and
  self-corrections are removed and punctuation fixed by the language model,
  or by instant rules with **Basic**. A slow (over 8 s), failed or
  chat-like reply pastes your words as said, and the overlay says so.
- A cleanup prompt you can edit and test, a fallback cloud model, rate
  limits that are waited out instead of retried, an instruction guard,
  and an output language that translates dictations.

- **Cloud services by name**: Groq, OpenAI, Gemini, xAI and Deepgram for
  transcription; Groq, OpenAI, Gemini, xAI, Anthropic and Cerebras for AI;
  or any OpenAI-compatible endpoint. Suggested models with **Reset**, one
  key per service shared by both tabs, checked before it is saved.
- **Offline fallback**: with no connection, a cloud choice transcribes and
  cleans up on this PC if the models are downloaded, and says so once.
- **Test** for transcription, and timeouts you can raise for slow servers.

- **Naad's window**: Home, History, Dictionary, Snippets and Settings, in a
  light or dark theme that follows Windows by default. Open it from the tray
  menu or by double-clicking the icon.
- **A setup guide** on first start: microphone test, models, AI cleanup, an
  optional cloud key, your words, and a first dictation. Run it again from
  the tray.
- **A fuller tray menu**: Start/Stop dictating, the microphone, Open Naad
  and Re-run setup. The icon can be hidden; starting Naad again opens it.
- An error tone, a volume setting and **Preview**.

- **History**: every dictation kept encrypted for 7 days, 30 days (the
  default) or until deleted, or not at all. Search, copy, paste, play,
  retry, export as ZIP and delete; Home shows words, words per minute,
  time saved and a day streak.
- **Dictionary page** for your words, and **snippets** that expand a spoken
  trigger, stored encrypted. "Press enter" at the end presses Enter
  (off by default).
- **Esc** cancels a dictation; **Space** while holding the shortcut keeps
  recording hands-free; a **Paste again** shortcut and tray items for
  recent dictations; **Add copied words to dictionary**; an optional
  recording timer on the overlay.
- **Context**: the app's name and text near the cursor help the language
  model spell names. Never from password fields; apps can be excluded. With
  a cloud model, an optional screenshot (off by default).
- **Edit Mode**: speak an instruction over selected text and the model
  rewrites it. Automatic, Manual (hold Shift) or Off. A failed edit leaves
  the text alone.

- **Live preview**: with Whisper on a graphics card, each phrase is
  transcribed while you speak and shown in the overlay, so the text is
  ready sooner after you let go.
- **Live typing** (off by default) types each finished phrase into the app
  as you speak, and stops, keeping the full text, if the window changes.
  Live preview and typing can also stream to OpenAI's realtime
  transcription or Deepgram, falling back to an upload if streaming fails.
- **Meeting notes**: your microphone (You) and the PC's sound (Others),
  transcribed in pieces on your PC while the meeting runs, saved encrypted
  and copied when you stop. Naad offers notes when a call app starts using
  the microphone, and never records without your click. A watchdog reopens
  a microphone that goes silent or is unplugged.
- **Meetings page**: meetings by day with search, rename, Markdown export,
  delete, and a summary with decisions and action items from the language
  model.
- **Diagnostics page** with build details to copy into a bug report.
- An optional `is-recording` flag file for other tools.

### Changed

- Clicking an update offer opens a window with the notes of every newer
  release, and installs with a progress bar, **Cancel** and **Retry**. The
  overlay mentions an available update once after a dictation, and Naad
  never restarts for an update during meeting notes.
- The daily update check waits three days after it first sees a release,
  and **Skip version** in the tray stops it offering that version.
- Settings is split into **Dictation**, **AI** and **General** tabs, and
  opens inside Naad's window.
- API keys move to one encrypted file per service; Naad 1.0's key moves
  automatically. The development override is now `NAAD_<SERVICE>_API_KEY`
  (`NAAD_GROQ_API_KEY` still works for Groq). Settings version 3.
- Network errors say whether there is no internet, the request timed out or
  the connection was lost.
- The overlay shows **Cleaning up** while the language model works, and
  **Starting** until the microphone is open.
- The vocabulary becomes a list of words. Settings version 4.
- **Test models** on the **AI** tab tests the model and its fallback. A
  custom base URL is checked when it is saved, even without a key.
- With a dictation language set, AI cleanup is told the text is in it.

- **Shortcut roles**: hold to talk, a hands-free toggle and paste again
  each have their own shortcut, chosen from presets, captured, or off, in
  Settings or the tray's **Shortcuts** menu. Hold and toggle work side by
  side; pressing the toggle while holding keeps recording. Clashes are
  refused with both roles named, and the last custom keys are kept per
  role. The Manual Edit Mode key can be Shift or Ctrl. Settings
  version 5 moves the old shortcut and hands-free choice into the new roles.
- A start delay (0 to 500 ms) so a quick tap of hold to talk records
  nothing. The overlay
  shows **Starting** only when the microphone is slow to open, and its
  timer counts from when recording began.

### Fixed

- Live typing types while the shortcut is held: the shortcut's Ctrl or
  Shift is let go for the app first, so the letters never become
  shortcuts. It used to wait until you let go.
- **Add snippet** adds an empty row, and a half-filled row is never saved;
  it used to save a "trigger phrase" placeholder.
- OpenAI reasoning models are sent no temperature, and a refusal in
  Anthropic's error format is recognised as well as OpenAI's. Reasoning models get room to
  think, and on Groq and OpenRouter don't send their reasoning back.
- **Copy last transcript** copies the newest dictation in the history and is
  enabled whenever there is one, not only after a paste failed.
- **Paste again** after Esc during cleanup, or after a failed edit, pastes
  that dictation's words, never the previous one's.
- A request that times out because the PC went offline says there is no
  internet and falls back to this PC, as does a dropped connection.
- Esc says **Cancelled** on the overlay. A context read that fails no
  longer loses the dictation, and tray notifications show even when the
  icon is hidden.

## [1.0.0] - 2026-10-31

The first release. Hold a shortcut, speak, let go: your words are typed into
the app you're using, transcribed by Whisper on your PC, free and offline.
The 0.1.0 milestone was never released on its own; its work is part of this
release.

### Added

- **Dictation.**
  - Hold a shortcut to dictate: Right Ctrl by default, or any chord of up
    to four keys, captured in Settings.
  - Another key or a mouse click while holding cancels, so shortcuts such
    as Right Ctrl+C keep working.
  - Hands-free mode presses once to start and once to stop.
  - Recording stops by itself after five minutes, and the text is pasted
    once you let go.
- **Transcription on this PC**, the default.
  - Whisper runs on the graphics card through Vulkan when there is one,
    preferring a discrete card, and on the processor otherwise.
  - Settings downloads the model once and checks its SHA-256. There are two
    models: Base (148 MB, fast on any PC) and Large v3 Turbo (574 MB, more
    accurate, needs a graphics card).
  - No account and no API key are needed, and your audio never leaves the
    PC.
- **Transcription in the cloud**, if you choose it.
  - Any OpenAI-compatible API, Groq by default, with a 20-second timeout and
    one retry for a request that never reached the server.
  - The key is encrypted with Windows DPAPI and never shown again.
- **A vocabulary** of names and terms, sent to Whisper as a hint, and an
  optional language.
- **Safe pasting.**
  - Text goes only into the window you dictated into, never into another
    one or an app running as administrator. Otherwise Naad keeps it and
    offers **Copy last transcript**.
  - Your clipboard is put back afterwards, unless you copied something new
    meanwhile, and transcripts stay out of clipboard history.
  - If the clipboard can't be used, the text is typed as Unicode keys
    instead.
- **Fewer inventions.** Silence and clips under 300 ms are never
  transcribed, and a transcript that is only a known Whisper invention
  ("Thanks for watching!") or a non-speech tag (`[BLANK_AUDIO]`) is dropped.
- **Tray app.**
  - Single instance with an orderly exit.
  - The icon shows a red dot while listening and an amber one while
    transcribing.
  - A small pill near the bottom of the screen shows voice bars that rise
    with your voice, then **Transcribing**. It never takes focus and lets
    clicks through.
  - Notifications say what went wrong: the microphone, the transcription,
    nothing heard, or a kept transcript.
  - Optional start and stop tones, off by default.
- **Settings window** for every setting and the key.
  - Changes apply without a restart.
  - **Test key** checks a key with the provider.
  - Start Naad when you sign in.
- **Settings file.** `settings.json` has versioning, backups, repair of
  invalid values and atomic writes. A file from a development build that
  never chose a provider keeps the cloud.
- **Robustness.**
  - A microphone unplugged mid-dictation keeps what was heard, and one
    plugged in is used at once.
  - The shortcut keeps working after sleep, unlock and Ctrl+Alt+Del.
- **Diagnostic log.** It holds event names and numbers only: never audio,
  transcripts, clipboard contents, window titles or keys.
- **Installer and updates.**
  - A per-user installer that needs no administrator rights.
  - Updates from GitHub Releases, installed only when you click the offer
    and never during a dictation.
  - Uninstalling keeps your settings, key and models.
- **Documentation.** [Privacy](docs/privacy.md),
  [troubleshooting](docs/troubleshooting.md) and
  [architecture](docs/architecture.md) pages.

### Known limitations

- **Not code-signed.** Windows SmartScreen warns on the first run; the
  README says what to do.
- **Tested on Windows 11 only,** with an automated manual pass. The checks
  it couldn't run, and why, are listed in `docs/manual-tests.md`.
- **The start tone and sleeping speakers.** With the tones on, the start
  tone can cost about a second of audio on a PC whose speakers have gone
  idle. Wait for it before speaking.
- **The tray menu copies, it doesn't paste.** A kept transcript is copied by
  the tray menu, because opening the menu moves focus away from your
  window.
- **Clipboard formats that aren't restored.** Formats that are GDI handles
  or private to an app, such as enhanced metafiles, aren't put back.
- **A fixed restore delay.** The clipboard is put back after a fixed
  500 ms. An app that reads it later than that would paste your previous
  clipboard instead.
- **The typing fallback in Notepad.** When the clipboard can't be opened,
  the typed fallback is mangled by Windows 11 Notepad.
- **A press right after a paste.** A press of the shortcut within about
  half a second after a paste is ignored.
- **Full updates only.** Each update downloads the whole package; there are
  no delta updates yet.
