# Naad

Hold a key, speak, release: your words appear wherever your cursor is.

Naad is a Windows voice-typing app. It runs in the background. Hold a
shortcut (Right Ctrl by default), talk naturally, and let go. Naad
transcribes what you said and types it into the app you are using, such as
Notepad, Chrome or VS Code.

> **Status: 0.1.0 MVP, not yet released.** Dictation works end to end in
> development builds, and its manual checks are still being run. No release
> has been published yet, so build it from source for now. See
> [CHANGELOG.md](CHANGELOG.md) and the plan in [docs/ROADMAP.md](docs/ROADMAP.md).

## Install

Once a release is published, download `Naad.App-win-Setup.exe` from the
[Releases page](https://github.com/ayushgade06/naad/releases) and run it.
It installs Naad for your Windows account only, needs no administrator
rights, and starts it.

**Windows SmartScreen.** Naad isn't code-signed yet, so the first time you
run the installer Windows may say "Windows protected your PC". Click
**More info**, check that the publisher is unknown and the file is
`Naad.App-win-Setup.exe` from this repository's Releases page, then click
**Run anyway**. Only do this for files you downloaded from that page.

**Updates.** An installed Naad checks this repository's GitHub Releases a
minute after it starts and then once a day, and **Check for updates** in
its tray menu checks at once. When a newer version exists, a notification
asks first; click it, and Naad downloads the update, closes, installs it
and starts again. It never restarts during a dictation. Your settings and
key are kept.

**Uninstall** from Settings > Apps > Installed apps > Naad. It removes the
app and stops it starting at sign-in. Your data stays, in case you
reinstall; delete these folders to remove it too:

- `%APPDATA%\Naad`: settings.json and its backups.
- `%LOCALAPPDATA%\Naad`: the encrypted API key, logs and the local
  Whisper model.

## Try it from source

You need Windows 11 (or Windows 10 22H2), the .NET 10 SDK and a microphone.

1. Build it: `dotnet build -c Release` from the repository root.
2. Start `src\Naad\bin\Release\net10.0-windows\Naad.exe`. A teal icon
   appears in the notification area, and Settings opens.
3. Click **Download model**, wait for it to finish, then click **Save**.
   This happens once; after that Naad transcribes on your PC, offline.
4. Click into any text field, hold **Right Ctrl**, speak, and let go.

To use a cloud service instead, choose **Cloud** under **Transcribe with**
and store an API key, for example a free one from
https://console.groq.com/keys, as
[docs/settings.md](docs/settings.md#the-api-key) describes. Never paste a
key on a command line.

While you hold the key the icon shows a red dot; while Naad transcribes, an
amber one. If your text can't be pasted safely (you switched windows, or
the app runs as administrator), Naad keeps it and tells you. Choose
**Copy last transcript** in the tray menu, then paste it with Ctrl+V.
Settings such as the shortcut, the provider and the sounds are in
`%APPDATA%\Naad\settings.json`; see [docs/settings.md](docs/settings.md).

## What 0.1.0 does

- Hold-to-talk global shortcut, configurable, Right Ctrl by default.
- Speech-to-text on your PC with Whisper by default: free, offline, and
  your audio never leaves the machine. Or, if you prefer, through any
  OpenAI-compatible transcription API, such as
  [Groq](https://console.groq.com/keys).
- Pastes into the focused app and puts your clipboard back afterwards,
  unless you copied something new in the meantime. Transcripts stay out of
  Windows clipboard history.
- Never pastes into a different window: if focus changes while Naad is
  transcribing, the text is kept safe and Naad tells you.
- Doesn't upload silence or very short clips, and drops a few phrases
  Whisper is known to invent for noise.

AI rewriting, grammar correction and translation are not part of the first
release.

## Requirements

- Windows 11. Windows 10 22H2 is supported where practical.
- A microphone.
- 150 MB of disk space for the Whisper model, downloaded once from
  Settings; 574 MB for the more accurate model, which needs a graphics
  card.
- Only to transcribe in the cloud instead: an API key from Groq or another
  OpenAI-compatible provider.

## Privacy

These are design rules that every change must keep:

- Naad has no server and no account. Audio goes only to the transcription
  provider you choose, or nowhere when you transcribe on this PC.
- Naad contacts nothing else, except when you ask: Hugging Face for the
  local model when you click **Download model**, and, in an installed copy,
  GitHub once a day to see whether a new release exists. That check sends
  no data about you or your use.
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
