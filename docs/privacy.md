# Privacy

Naad has no server, no account and no telemetry. This page lists
everything Naad sends, stores and logs, so you can check it against the
code. Every change to Naad has to keep these rules (see `docs/STYLE.md`,
section 6).

## What leaves your PC

Naad connects to the network only in the cases below. All of them use
HTTPS.

| When | Destination | What is sent |
|---|---|---|
| You dictate with **Cloud** chosen under **Transcribe with** | The base URL in Settings, `https://api.groq.com/openai/v1` by default | The recording as a WAV file, the model name, the language if you set one, your vocabulary if you set one, and your API key in the `Authorization` header |
| You click **Test key** in Settings | The same base URL | The key you typed, to ask whether the provider accepts it. No audio |
| You click **Download model** | `huggingface.co`: whisper.cpp's model files, or the language models on the **AI** tab | An ordinary download request. No data about you |
| An installed copy, a minute after it starts, then daily, and when you choose **Check for updates** | `github.com/ayushgade06/naad` releases | An ordinary request for the release list. No data about you or how you use Naad |

With **This PC** chosen, which is the default, dictation never touches the
network: the recording is transcribed by Whisper on your PC. A copy run
from source never checks for updates.

Naad never switches from one provider to the other by itself. If the
model is missing, it asks you to download it rather than upload your audio.

## What Naad stores

| What | Where | Notes |
|---|---|---|
| Settings | `%APPDATA%\Naad\settings.json`, plus `.bak` copies made when the file is repaired or migrated | Plain JSON. It holds your vocabulary and the name of your chosen microphone, if you set them; nothing you said |
| API key | `%LOCALAPPDATA%\Naad\api-key.dpapi` | Encrypted with Windows DPAPI for your Windows account. Only your account on this PC can read it. Naad never shows it again, even in Settings. The key never goes into settings.json |
| Diagnostic log | `%LOCALAPPDATA%\Naad\logs\naad.log`, and `naad.1.log` after 1 MB | See below. Turn it off with **Write a diagnostic log** in Settings |
| Language model API key | `%LOCALAPPDATA%\Naad\llm-api-key.dpapi` | Encrypted like the API key, and kept apart from it |
| Whisper and language models | `%LOCALAPPDATA%\Naad\models` | Checked against a SHA-256 before use |
| Models that refuse a temperature | `%LOCALAPPDATA%\Naad\temperature-refused.json` | The cloud base URL and model id, and a date a week ahead. Nothing you said |
| Start at sign-in | A `Naad` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Only if you tick **Start Naad when I sign in** |
| The installed app | `%LOCALAPPDATA%\Naad.App` | Removed when you uninstall |

Audio is never written to disk. A recording lives in memory until it has
been transcribed, then it is dropped.

If a transcript can't be pasted, for example because you switched windows,
Naad keeps the latest one in memory so you can choose **Copy last
transcript**. Only one is kept, and it is gone when Naad exits.

## What the log contains

The log holds event names and numbers only: timings, byte counts, audio
levels, HTTP status codes, and exception type names. It never contains:

- audio or transcripts
- your vocabulary
- clipboard contents
- window titles or the names of the apps you dictate into
- API keys
- exception messages, which could quote any of the above

Unit tests check this. The log is safe to attach to a bug report.

## The clipboard

Naad pastes through the clipboard and then puts back what was on it. The
transcript it places there is marked so that Windows leaves it out of
clipboard history (Win+V), cloud clipboard, and clipboard monitors that
honour the marker. If you copy something yourself while Naad is pasting,
your copy wins and isn't overwritten.

## Removing everything

Uninstalling removes the app and the start-at-sign-in value, and keeps your
data in case you reinstall. To remove the data too, delete
`%APPDATA%\Naad` and `%LOCALAPPDATA%\Naad`.

To revoke an API key, delete it at your provider. Deleting
`api-key.dpapi` only removes Naad's copy.
