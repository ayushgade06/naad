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
| You dictate with **Cloud** chosen under **Transcribe with** | The chosen cloud service: `api.groq.com` by default, or `api.openai.com`, `generativelanguage.googleapis.com` (Gemini), `api.x.ai`, `api.deepgram.com`, or your custom base URL | The recording as a WAV file, the model name, the language if you set one, your vocabulary if you set one, and the service's API key in a header (`Authorization`; Gemini's `x-goog-api-key`) |
| You dictate with **Cloud** chosen under **AI runs on** and cleanup set to **AI**, or an output language set, or edit a selection | The chosen AI service: `api.groq.com` by default, or `api.openai.com`, `generativelanguage.googleapis.com`, `api.x.ai`, `api.anthropic.com`, `api.cerebras.ai`, or your custom base URL | The transcript, your vocabulary, your cleanup instructions and the model name, and the service's API key in the `Authorization` header. With **Context** on, also the app's name and up to 2,000 characters of text around the cursor; for an edit, the selected text. No audio |
| As above, with **send a screenshot** ticked | The same AI service | A screenshot of the window you dictate into (or the whole screen, if you chose that), at most 1,024 pixels on its longest side, to describe what's on screen. It is never saved |
| You click **Test** or **Test model** for the cleanup prompt with **Cloud** chosen | The same AI base URL | The sample text and the instructions. No audio |
| You click **Test key**, or **Save** with a new key | The chosen service | The key, to ask whether the service accepts it (a request listing its models). No audio or text |
| You click **Test** under **Transcription** | The transcription service | One second of generated silence. Nothing you said |
| You click **Download model** | `huggingface.co`: whisper.cpp's model files, or the language models on the **AI** tab | An ordinary download request. No data about you |
| An installed copy, a minute after it starts, then daily, and when you choose **Check for updates** | `github.com/ayushgade06/naad` releases | An ordinary request for the release list. No data about you or how you use Naad |

Deepgram takes the vocabulary as key terms in the request's address,
where the service's own logs may keep it; the other services get it in the
request body.

With **This PC** chosen, which is the default, dictation never touches the
network: the recording is transcribed by Whisper on your PC. A copy run
from source never checks for updates.

Naad never switches from this PC to the cloud by itself. If the model is
missing, it asks you to download it rather than upload your audio. The
other way round, a cloud choice uses the models on this PC while you are
offline, which keeps your audio and text on the PC.

## What Naad stores

| What | Where | Notes |
|---|---|---|
| Settings | `%APPDATA%\Naad\settings.json`, plus `.bak` copies made when the file is repaired or migrated | Plain JSON. It holds your vocabulary and the name of your chosen microphone, if you set them; nothing you said |
| API keys | `%LOCALAPPDATA%\Naad\keys\<service>.dpapi` (Naad 1.0: `api-key.dpapi`, moved there) | Encrypted with Windows DPAPI for your Windows account, one file per service. Only your account on this PC can read them. Naad never shows a key again, even in Settings. Keys never go into settings.json |
| Diagnostic log | `%LOCALAPPDATA%\Naad\logs\naad.log`, and `naad.1.log` after 1 MB | See below. Turn it off with **Write a diagnostic log** in Settings |
| Whisper and language models | `%LOCALAPPDATA%\Naad\models` | Checked against a SHA-256 before use |
| Dictation history | `%LOCALAPPDATA%\Naad\history`, a `.record` and an `.audio` file per dictation | Unless **History** is **Off**: the time, the app's name, the transcript before and after cleanup, and the recording, encrypted with DPAPI for your Windows account. Kept for the time you choose (30 days by default), then deleted. **Delete all** on the History page removes them |
| Snippets | `%LOCALAPPDATA%\Naad\snippets.dpapi` | Encrypted with DPAPI for your account |
| Rate limits | `%LOCALAPPDATA%\Naad\rate-limits.json` | Cloud model ids whose daily quota ran out, and when it resets. Nothing you said |
| Models that refuse a temperature | `%LOCALAPPDATA%\Naad\temperature-refused.json` | The cloud base URL and model id, and a date a week ahead. Nothing you said |
| Start at sign-in | A `Naad` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Only if you tick **Start Naad when I sign in** |
| The installed app | `%LOCALAPPDATA%\Naad.App` | Removed when you uninstall |

With **History** set to **Off**, audio is never written to disk: a
recording lives in memory until it has been transcribed, then it is
dropped. Otherwise it is kept encrypted with the dictation, as above.

Context (the app's name and the text near the cursor) and screenshots are
read for one dictation, kept in memory only, and never logged. Naad never
reads a password field or an app you excluded.

With **Live preview** on, the words heard so far are shown in the overlay
while you speak, kept in memory for that dictation only, and never logged.
They are transcribed on this PC; nothing extra is sent anywhere.

If a transcript can't be pasted, for example because you switched windows,
Naad keeps the latest one in memory so you can choose **Copy last
transcript**. Only one is kept, and it is gone when Naad exits.

## What the log contains

The log holds event names and numbers only: timings, byte counts, audio
levels, HTTP status codes, and exception type names. It never contains:

- audio or transcripts
- your vocabulary or snippets
- the text near the cursor, a selection, or a screenshot
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
its file in `%LOCALAPPDATA%\Naad\keys` only removes Naad's copy.
