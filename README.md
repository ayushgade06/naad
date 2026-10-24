# Naad

Hold a key, speak, release: your words appear wherever your cursor is.

Naad is a Windows voice-typing app. It runs in the background. Hold a
shortcut (Right Ctrl by default), talk naturally, and let go. Naad
transcribes what you said and types it into the app you are using, such as
Notepad, Chrome or VS Code.

> **Status: early development.** Nothing is usable yet. The plan is in
> [docs/ROADMAP.md](docs/ROADMAP.md).

## Planned first release

- Hold-to-talk global shortcut, configurable, Right Ctrl by default.
- Speech-to-text through any OpenAI-compatible transcription API, with
  [Groq](https://console.groq.com/keys) (free tier) as the default.
  Local, offline transcription comes in a later release.
- Pastes into the focused app and puts your clipboard back afterwards.
- Never pastes into a different window: if focus changes while Naad is
  transcribing, the text is kept safe and Naad tells you.

AI rewriting, grammar correction and translation are not part of the first
release.

## Requirements

- Windows 11. Windows 10 22H2 is supported where practical.
- A microphone.
- An API key from Groq or another OpenAI-compatible provider.

## Privacy

These are design rules that every change must keep:

- Naad has no server and no account. Audio goes only to the transcription
  provider you choose.
- Audio is kept in memory and never written to disk.
- API keys are encrypted with Windows DPAPI for your user account.
- Logs never contain your audio, transcripts, clipboard contents or keys.

## Contributing

Read [CLAUDE.md](CLAUDE.md) for the working rules and
[docs/STYLE.md](docs/STYLE.md) for code and Git conventions.

## Credits

Naad is inspired by [Suki](https://github.com/ansuman-shukla/Suki), an
open-source macOS dictation app by Ansuman Shukla and Zach Latta, released
under the MIT License. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

[MIT](LICENSE)
