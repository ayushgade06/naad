# Naad

Hold-to-talk voice typing for Windows. Hold a key, speak, release: your words
appear wherever your cursor is.

[![CI](https://github.com/ayushgade06/naad/actions/workflows/ci.yml/badge.svg)](https://github.com/ayushgade06/naad/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ayushgade06/naad)](https://github.com/ayushgade06/naad/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Features

- **Private by default.** Speech is transcribed on your PC with Whisper:
  free and offline, with no account. A graphics card is used when there is
  one.
- **Clean text.** Filler words, stutters and self-corrections are removed
  and punctuation fixed, by a small language model on your PC (or a cloud
  model, if you choose), or by instant rules. If the AI is slow or fails,
  your words are pasted as said.
- **Works in any app.** Text is pasted into the focused window, and your
  clipboard is restored afterwards.
- **Never types into the wrong place.** If the window changes before the
  text is ready, Naad keeps the text and tells you.
- **Cloud optional.** Any OpenAI-compatible transcription API, such as Groq,
  with the key encrypted by Windows.
- **Configurable.** Any shortcut, hands-free mode, a custom vocabulary, the
  language and the microphone.

## Install

Download `Naad.App-win-Setup.exe` from the
[latest release](https://github.com/ayushgade06/naad/releases/latest) and
run it. It installs for your account only, with no administrator rights, and
updates itself when you agree.

Naad isn't code-signed yet, so Windows SmartScreen may warn on first run.
Choose **More info**, then **Run anyway**.

## Usage

1. On first start, Settings opens. Click **Download model** (148 MB, once),
   then **Save**.
2. Click into any text field.
3. Hold **Right Ctrl**, speak, and release.

Everything else is in **Settings...** in the tray menu. See
[docs/settings.md](docs/settings.md) for every option.

## Requirements

- Windows 11 (Windows 10 22H2 should work, but is untested)
- A microphone
- 150 MB of disk for the speech model; 574 MB for the more accurate one,
  which needs a graphics card
- For AI cleanup on your PC, 1.1 GB of disk and memory (2.5 GB for the
  more accurate model, best with a graphics card)

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build -c Release
dotnet test
```

Run `src/Naad/bin/Release/net10.0-windows/Naad.exe`.

## Documentation

- [Settings](docs/settings.md)
- [Privacy](docs/privacy.md): what Naad sends, stores and logs
- [Troubleshooting](docs/troubleshooting.md)
- [Architecture](docs/architecture.md)
- [Changelog](CHANGELOG.md)

## Contributing

Issues and pull requests are welcome. Read [CLAUDE.md](CLAUDE.md) and
[docs/STYLE.md](docs/STYLE.md) first.

## License

[MIT](LICENSE). Third-party licenses are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
