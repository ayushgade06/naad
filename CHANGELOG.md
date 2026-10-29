# Changelog

All notable changes to Naad are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Settings window (tray menu > **Settings...**) for the API key, the
  shortcut, the microphone, the language, the transcription base URL and
  model, sounds and the log file. Changes apply at once, without a restart.
- **Test key** asks the provider whether it accepts a key. The window never
  shows a stored key.
- Shortcut capture: press the new shortcut instead of typing key names.
- Recording overlay: a small bar near the bottom of the screen shows
  **Listening** with a level meter, then **Transcribing**. It never takes
  focus, lets clicks through and isn't in Alt+Tab.
- `language` setting: the language you speak, sent with each recording.
- `microphone` setting: record from a named device instead of the Windows
  default. A device that isn't connected falls back to the default.

- Robustness: a microphone unplugged mid-dictation keeps what was heard;
  the shortcut keeps working after sleep, unlock and Ctrl+Alt+Del; a
  transcription request that never reached the server is retried once.

### Changed

- Without a usable API key, Naad starts and opens Settings instead of
  exiting. Pressing the shortcut without a key shows a notification and
  records nothing.

## [0.1.0] - Unreleased

The first milestone, the MVP: hold-to-talk dictation that works end to end
and fails safely. It has not been tagged or published yet; its manual
checks in `docs/manual-tests.md` are still being run.

### Added

- Tray app with a single instance and an orderly exit. The icon shows a red
  dot while listening and an amber one while transcribing.
- Hold a shortcut to dictate: Right Ctrl by default, or any chord of up to
  four keys (`dictationShortcut`). Another key or a mouse click while
  holding cancels, so shortcuts such as Right Ctrl+C keep working.
  Recording stops by itself after 5 minutes.
- Speech-to-text through any OpenAI-compatible API, Groq by default
  (`transcriptionBaseUrl`, `transcriptionModel`), with a 20-second timeout.
  Failures show a notification that says what went wrong.
- Silence and clips under 300 ms are never uploaded. A transcript that is
  only a known Whisper invention, such as "Thanks for watching!", is
  dropped.
- Pasting restores your clipboard afterwards, unless you copied something
  new meanwhile, and keeps transcripts out of clipboard history. If the
  clipboard can't be used, the text is typed as Unicode keys instead.
- Focus guard: text only goes into the window you dictated into, never into
  another one or an app running as administrator. Otherwise Naad keeps the
  transcript and offers **Copy last transcript**.
- Soft start and stop tones (`playSounds`), synthesized, with no sound files.
- The API key is stored encrypted with Windows DPAPI
  (`Naad.exe --set-api-key`); `NAAD_GROQ_API_KEY` overrides it for
  development.
- A diagnostic log that holds event names and numbers only: never audio,
  transcripts, clipboard contents, window titles or keys.
- `settings.json` with versioning, backups, repair of invalid values and
  atomic writes.

### Known limitations

- No installer, settings window or recording overlay yet (P17, P18, P22).
- A kept transcript is copied, not pasted, by the tray menu: opening the
  menu moves focus away from your window.
- Clipboard formats that are GDI handles or private to an app (for
  example enhanced metafiles) aren't restored; their number is logged.
- The clipboard is restored after a fixed 500 ms. An app that reads the
  clipboard later than that would paste your previous clipboard instead.
- No retries for transient network errors (P19), and no local, offline
  transcription yet (P20).
