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

### Changed

- Settings is split into **Dictation** and **AI** tabs.

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
