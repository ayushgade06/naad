# Settings

The easiest way to change settings is **Settings...** in Naad's tray menu,
which opens the Settings page of Naad's window. It has three tabs:
**Dictation**, **AI** and **General**.
Changes apply as soon as you click **Save**, without a restart; if you are
dictating at that moment, they apply when the dictation ends.

Naad keeps its settings in `%APPDATA%\Naad\settings.json`, for example
`C:\Users\<you>\AppData\Roaming\Naad\settings.json`. The file is created
with defaults the first time Naad starts. You can also edit it with any text
editor while Naad is closed; such changes apply the next time Naad starts.

```json
{
  "version": 4,
  "fileLogging": true,
  "playSounds": false,
  "errorSound": false,
  "soundVolume": 80,
  "theme": "System",
  "showTrayIcon": true,
  "dictationShortcut": "RightCtrl",
  "transcriptionProvider": "Local",
  "localModel": "Base",
  "transcriptionService": "Groq",
  "transcriptionBaseUrl": "https://api.groq.com/openai/v1",
  "transcriptionTimeoutSeconds": 20,
  "transcriptionModel": "whisper-large-v3-turbo",
  "language": null,
  "microphone": null,
  "vocabulary": [],
  "historyRetention": "Month",
  "pressEnterCommand": false,
  "pasteAgainShortcut": null,
  "overlayTimer": false,
  "context": true,
  "contextExcludedApps": [],
  "contextScreenshot": false,
  "screenshotSize": 1024,
  "screenshotWholeDesktop": false,
  "editMode": "Automatic",
  "livePreview": false,
  "handsFree": false,
  "languageModelProvider": "Local",
  "localLanguageModel": "Small",
  "languageModelService": "Groq",
  "languageModelBaseUrl": "https://api.groq.com/openai/v1",
  "languageModelTimeoutSeconds": 8,
  "languageModelName": "openai/gpt-oss-20b",
  "languageModelFallbackName": null,
  "cleanup": "Ai",
  "cleanupPrompt": null,
  "cleanupPromptVersion": null,
  "instructionGuard": true,
  "outputLanguage": null
}
```

| Setting | Type | Default | Meaning |
|---|---|---|---|
| `version` | integer | `4` | The file's schema version. Naad manages it; don't change it. |
| `fileLogging` | `true` or `false` | `true` | Write the diagnostic log to `%LOCALAPPDATA%\Naad\logs`. The log never contains what you said (see `docs/manual-tests.md`, M-L1). With `false`, Naad writes no log file at all. |
| `playSounds` | `true` or `false` | `false` | Play a short, soft tone once Naad is listening and once it has stopped. Off by default: on a PC whose speakers go idle, waking them takes a second or more, so the start tone comes that long after the press; speak after it. |
| `errorSound` | `true` or `false` | `false` | Play a low tone when a dictation fails. |
| `soundVolume` | 0 to 100 | `80` | How loud the tones are. **Preview** on the General tab plays one. |
| `theme` | `"System"`, `"Light"` or `"Dark"` | `"System"` | The look of Naad's window. `"System"` follows Windows' app mode. |
| `showTrayIcon` | `true` or `false` | `true` | Show Naad in the notification area. When hidden, start Naad again (from the Start menu) to open its window. |
| `dictationShortcut` | text | `"RightCtrl"` | The keys you hold while dictating. See [The dictation shortcut](#the-dictation-shortcut). |
| `transcriptionProvider` | `"Local"` or `"Cloud"` | `"Local"` | Where speech becomes text: `"Local"` transcribes on this PC with Whisper, offline and free, once the model is downloaded (see [Transcribing on this PC](#transcribing-on-this-pc)); `"Cloud"` uploads it to the service below. Naad never switches between them by itself. A version-1 file without this setting was using the cloud, so its migration keeps `"Cloud"`. |
| `localModel` | `"Base"` or `"Turbo"` | `"Base"` | The Whisper model used on this PC: `"Base"` (148 MB, fast on any PC) or `"Turbo"` (large-v3-turbo, 574 MB, more accurate, for PCs with a graphics card). Each needs its own download; see [Transcribing on this PC](#transcribing-on-this-pc). |
| `transcriptionService` | `"Groq"`, `"OpenAI"`, `"Gemini"`, `"XAI"`, `"Deepgram"` or `"Custom"` | `"Groq"` | The cloud service that transcribes with **Cloud** chosen. See [Cloud services](#cloud-services). |
| `transcriptionBaseUrl` | text | `"https://api.groq.com/openai/v1"` | Used only with `"Custom"`: the root of any OpenAI-compatible API; Naad posts to `<root>/audio/transcriptions`. It must be `https://`, or `http://` only for a server on this PC (`localhost`, `127.0.0.1`), because the API key is sent with every request. A query string or a user name and password aren't allowed. |
| `transcriptionModel` | text | `"whisper-large-v3-turbo"` | The service's model id, such as `whisper-1` for OpenAI or `nova-3` for Deepgram. Up to 100 visible ASCII characters, no spaces. |
| `transcriptionTimeoutSeconds` | 2 to 300 | `20` | How long one cloud transcription may take before Naad gives up and says the request timed out. Raise it for a slow server on this PC. |
| `language` | text or `null` | `null` | The language you speak, as a lowercase [ISO 639-1](https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes) code such as `"en"` or `"hi"`; a few providers also take three-letter codes. Naad sends it with each recording, which makes transcripts faster and more accurate. `null` (or `""`) lets the provider detect the language. |
| `vocabulary` | list of text | `[]` | Your dictionary: names and terms you use, such as `["Naad", "Groq", "Priya"]`, each up to 100 characters. Edit it on the **Dictionary** page. They are sent with each recording as Whisper's prompt (the first 500 characters' worth), and to the language model, so they are spelled right. They go to the services you chose but never into the log. |
| `historyRetention` | `"Off"`, `"Week"`, `"Month"` or `"Forever"` | `"Month"` | How long dictations are kept on the **History** page: not at all, 7 days, 30 days, or until you delete them. They are encrypted; see [History](#history). Choosing a shorter time deletes older ones at once. |
| `pressEnterCommand` | `true` or `false` | `false` | Saying "press enter" at the end of a dictation presses Enter after the text, for chat boxes. Only if the same window is still in front. |
| `pasteAgainShortcut` | text or `null` | `null` | Keys, written like `dictationShortcut` (for example `"RightAlt+V"`), that paste the last dictation again. `null` has none. |
| `overlayTimer` | `true` or `false` | `false` | Show the recording time on the overlay. |
| `context` | `true` or `false` | `true` | Read the app's name and up to 2,000 characters around the cursor when you dictate, so the language model can spell names you can see. Never from a password field. See [Context and Edit Mode](#context-and-edit-mode). |
| `contextExcludedApps` | list of text | `[]` | App names (the program's file name without `.exe`, such as `"keepass"`) Naad never reads context from. |
| `contextScreenshot` | `true` or `false` | `false` | With `context` on, AI cleanup and a **Cloud** language model, also send a screenshot of the window you dictate into, so the model can see what you're replying to. |
| `screenshotSize` | `512`, `640`, `768` or `1024` | `1024` | The longest side of the screenshot, in pixels. |
| `screenshotWholeDesktop` | `true` or `false` | `false` | Capture the whole screen when the window can't be captured on its own. |
| `editMode` | `"Off"`, `"Automatic"` or `"Manual"` | `"Automatic"` | What speaking over selected text does. See [Context and Edit Mode](#context-and-edit-mode). |
| `livePreview` | `true` or `false` | `false` | Transcribe each phrase while you speak and show the words in the overlay, so the text is ready sooner after you let go. Only with `transcriptionProvider` `"Local"` and Whisper running on a graphics card; it starts from the second dictation after Naad starts or settings are saved, once the model is loaded. On the processor alone it does nothing. The words are shown, never saved. |
| `handsFree` | `true` or `false` | `false` | With `true`, press the shortcut once to start dictating and again to stop, instead of holding it. |
| `microphone` | text or `null` | `null` | The recording device's name, exactly as Windows lists it, for example `"Microphone (USB Audio Device)"`. `null` (or `""`) follows the Windows default recording device. If the named device isn't connected, Naad records from the default instead and logs `MicrophoneFallbackToDefault`. Windows shortens these names to 31 characters, so two devices whose names start the same can't be told apart; Naad then uses the first. |
| `languageModelProvider` | `"Local"` or `"Cloud"` | `"Local"` | Where AI steps run: `"Local"` on a language model on this PC, offline and free, once it is downloaded; `"Cloud"` through the chat API below. Naad never switches between them by itself. See [The language model](#the-language-model). |
| `localLanguageModel` | `"Small"` or `"Large"` | `"Small"` | The language model on this PC: `"Small"` (Qwen2.5-1.5B-Instruct, 1.1 GB, fast on any PC) or `"Large"` (Qwen3-4B-Instruct-2507, 2.5 GB, more accurate, for PCs with a graphics card). |
| `languageModelService` | `"Groq"`, `"OpenAI"`, `"Gemini"`, `"XAI"`, `"Anthropic"`, `"Cerebras"` or `"Custom"` | `"Groq"` | The cloud service for AI steps with **Cloud** chosen. |
| `languageModelTimeoutSeconds` | 2 to 300 | `8` | How long cleanup waits for the language model before pasting the text as said. Raise it for a slow server on this PC. |
| `languageModelBaseUrl` | text | `"https://api.groq.com/openai/v1"` | Used only with `"Custom"`: the root of any OpenAI-compatible chat API; Naad posts to `<root>/chat/completions`. The same rules as `transcriptionBaseUrl`: `https://`, or `http://` only for a server on this PC such as Ollama (`http://localhost:11434/v1`) or LM Studio. |
| `languageModelName` | text | `"openai/gpt-oss-20b"` | The cloud provider's chat model id. Up to 100 visible ASCII characters, no spaces. |
| `languageModelFallbackName` | text or `null` | `null` | A second cloud model on the same endpoint, tried when the first is rate limited, gives a blank reply or answers the dictation instead of tidying it. |
| `cleanup` | `"Off"`, `"Basic"` or `"Ai"` | `"Ai"` | How a transcript is tidied before it is pasted. See [Cleanup](#cleanup). |
| `cleanupPrompt` | text or `null` | `null` | Your own instructions for the language model, up to 8,000 characters; `null` uses the default. Edit it with **Edit prompt...** on the **AI** tab. |
| `cleanupPromptVersion` | text or `null` | `null` | The date of the default prompt your instructions were based on, such as `"2026-10-31"`. Naad uses it to tell you when a newer default ships; don't change it. |
| `instructionGuard` | `true` or `false` | `true` | Paste the text as said when the language model seems to answer the dictation ("ask Claude what…") instead of tidying it. |
| `outputLanguage` | text or `null` | `null` | A language to write dictations in, such as `"German"`, translating them. With `cleanup` `"Off"`, a literal translation still runs. Needs a language model. |

Hand edits are forgiving. Property names may use any casing, and `//`
comments and trailing commas are accepted. Naad keeps the file as you wrote
it unless it has to repair or migrate it.

## The dictation shortcut

`dictationShortcut` is one to four key names joined by `+`, for example
`"RightCtrl"`, `"LeftCtrl+Space"` or `"RightAlt+F9"`. Names ignore case,
and spaces around `+` are allowed.

- **Modifiers** have their own names, because left and right are different
  keys: `LeftCtrl`, `RightCtrl`, `LeftShift`, `RightShift`, `LeftAlt`,
  `RightAlt`, `LeftWin`, `RightWin`.
- **Every other key** uses its name in .NET's
  [`Keys` enumeration](https://learn.microsoft.com/dotnet/api/system.windows.forms.keys),
  for example `Space`, `F9`, `A`, `D1` (the 1 above the letters), `NumPad1`,
  `Scroll` or `Pause`.
- **Refused**, because a keyboard hook never reports them: modifiers
  without a side (`ControlKey`, `ShiftKey`, `Menu`) and mouse buttons. A
  key listed twice, or more than four keys, is refused too.

An invalid value falls back to Right Ctrl, and the file is backed up and
repaired as described below. The tray icon's tooltip names the shortcut in
use, for example "Naad: hold Right Ctrl to dictate".

How the keys are matched:

- The shortcut starts when its last key goes down while its other keys are
  held, in any order. Keys you were already holding, such as Shift, don't
  stop it.
- Pressing any other key while holding the shortcut cancels the dictation,
  so shortcuts such as Right Ctrl+C keep working. Nothing is sent or
  pasted. Press the shortcut again to dictate.
- Releasing any of its keys ends the dictation.
- Holding a key down (auto-repeat) has no effect.

Modifiers in the shortcut (Ctrl, Shift, Alt, Windows) still reach the app
you are using, so Right Ctrl keeps working as Ctrl. Its other keys don't:
holding `LeftCtrl+Space` types no space, and with `F9` the app never sees
F9. That also means a shortcut such as a lone `V` stops you typing that
letter while Naad runs.

Clicking a mouse button or turning the wheel while holding the shortcut
cancels the dictation, so Ctrl+click and Ctrl+scroll keep working.

## Cloud services

Transcription (on the **Dictation** tab) and AI (on the **AI** tab) each
pick a **Cloud service** for when **Cloud** is chosen:

| Service | Transcription | AI | Keys |
|---|---|---|---|
| Groq | `whisper-large-v3-turbo`, `whisper-large-v3` | `openai/gpt-oss-20b` and others | console.groq.com/keys |
| OpenAI | `gpt-4o-mini-transcribe`, `gpt-4o-transcribe`, `whisper-1` | `gpt-4.1-mini` and others | platform.openai.com/api-keys |
| Gemini | `gemini-2.5-flash` (through Gemini's own API) | `gemini-2.5-flash-lite` and others | aistudio.google.com/apikey |
| xAI | `grok-stt` | `grok-3-mini` and others | console.x.ai |
| Deepgram | `nova-3`, `nova-2` (through Deepgram's own API) | — | console.deepgram.com |
| Anthropic | — | `claude-haiku-4-5` and others | console.anthropic.com |
| Cerebras | — | `gpt-oss-120b` and others | cloud.cerebras.ai |
| Custom | any OpenAI-compatible endpoint | any OpenAI-compatible endpoint | optional on this PC |

The model lists are suggestions: type any model id the service offers, or
click **Reset** for the suggested one. **Custom** takes a **Base URL**, such
as `http://localhost:11434/v1` for Ollama; a server on this PC needs no
key. Both tabs share one Custom key, so two different custom servers that
both need keys can't be used at once.

**Test** under **Transcription** sends one second of silence with the
saved settings and says whether the service answered.

When the PC is offline, or the service can't be reached, Naad transcribes
and cleans up on this PC instead, if those models are downloaded, using the
same recording; it says so once each time Naad runs. Without them, the
notification says there is no internet connection, that the request timed
out, or that the connection was lost.

## The API keys

Each cloud service has one API key, used by both tabs: a Groq key typed on
the **AI** tab also transcribes with Groq. Keys are stored encrypted, apart
from settings.json, in `%LOCALAPPDATA%\Naad\keys\<service>.dpapi`. Naad
1.0's single key, `%LOCALAPPDATA%\Naad\api-key.dpapi`, moves there the
first time it is needed, as the key of the service its base URL named.

To store or replace a key, choose the service, paste the key into **New
key** and click **Save**. Naad asks the service first, by listing its
models (Deepgram: its projects), and doesn't store a key the service
rejects. **Test key** asks the same without saving. A service that can't
list models can't check keys, and Naad says so. Settings never shows a
stored key, only where the key in use comes from.

You can also pipe the transcription service's key into Naad from
PowerShell. The key is read without being shown, and never lands in the
shell's history:

```powershell
[Net.NetworkCredential]::new('', (Read-Host -AsSecureString 'API key')).Password |
  & "<path to>\Naad.exe" --set-api-key | Out-Null
```

From a source checkout, use `dotnet run --project src/Naad -- --set-api-key`
in place of the `& ... Naad.exe` part. `| Out-Null` makes PowerShell wait
for Naad to finish; `$LASTEXITCODE` is then `0` once the key is stored.
Naad accepts the key only from a pipe: `--set-api-key` without one shows how
to use it and changes nothing. The key is stored for the
`transcriptionService` in settings.json, and takes effect the next time
Naad starts.

How keys are protected and found:

- **Encryption:** Windows DPAPI with the CurrentUser scope. Only your
  Windows account, on this PC, can decrypt the file. Another user, another
  PC, or a copy of the file on a backup can't. Resetting your Windows
  password from outside your account (for example by an administrator) can
  make the file unreadable; store the key again if that happens.
- **Development override:** if `NAAD_<SERVICE>_API_KEY` (for example
  `NAAD_GROQ_API_KEY` or `NAAD_OPENAI_API_KEY`) is set and not blank, Naad
  uses it for that service and doesn't read the stored key. The log says
  which one was used (`ApiKeyFromEnvironment` or `ApiKeyLoaded`), never the
  key itself.
- **No key:** Naad logs `ApiKeyMissing` and starts anyway. With **Cloud**
  chosen for transcription, it opens Settings so you can add one, and
  pressing the shortcut shows a notification instead of recording
  (`DictationNeedsApiKey`).
- **Unreadable key** (damaged, or saved by another Windows user): the same,
  with `ApiKeyUnreadable` and the exception type logged. Naad doesn't delete
  the file; saving a new key replaces it.

Keys never appear in settings.json, the log, error dialogs or test files.
To remove one, delete its file while Naad is closed.

## Transcribing on this PC

This is the default. The first time Naad starts, Settings opens with
**This PC** chosen under **Transcribe with**: click **Download model**,
then **Save**. Until the model is downloaded, the shortcut asks you to
finish setting up and records nothing.

**Model on this PC** chooses which of whisper.cpp's conversions of
OpenAI's Whisper models Naad uses:

| Choice | `localModel` | File | Download | Speed |
|---|---|---|---|---|
| **Base: fast on any PC** (default) | `"Base"` | `ggml-base.bin` | 148 MB | About 2 s a dictation on the processor; under 1 s on a graphics card. |
| **Large v3 Turbo: more accurate, needs a GPU** | `"Turbo"` | `ggml-large-v3-turbo-q5_0.bin` | 574 MB | About 1.5 s on a discrete graphics card; 5 s or more on an integrated one; about 30 s on the processor. |

Large v3 Turbo is the model Groq runs, so on a PC with a discrete graphics
card it is about as accurate as the cloud. Base is less accurate,
especially with quiet speech or unusual words, so **Vocabulary** helps
more there. The speeds were measured on one laptop (see
`docs/manual-tests.md`, P24).

Naad downloads the chosen model from Hugging Face once, checks its
SHA-256, and keeps it in `%LOCALAPPDATA%\Naad\models`. A damaged or
interrupted download is deleted and can be started again. After that,
dictation needs no network and no API key, and audio never leaves your PC.
Switching models keeps the other one on disk, so switching back needs no
download.

**Graphics card.** If the PC has a graphics card with a Vulkan driver, as
most from the last ten years do, Whisper runs on it, and prefers a
discrete card (NVIDIA, AMD or Intel Arc) over the one built into the
processor. Otherwise it runs on the processor, using half its cores. The
first dictation after starting Naad takes a second or a few longer while
the model loads. The very first one with each model on a graphics card,
and the first after a driver update, can take up to a minute while the
driver compiles Whisper's shaders; it keeps them for next time. The log's
`LocalModelLoaded` line says what was used: `vulkan=1` for the graphics
card, `discrete_gpu=1` when a discrete card was found.

To free the space, choose **Cloud** and delete the `models` folder, or
delete just the model you no longer use.

## The language model

Naad can run a small language model for AI steps such as cleaning up a
transcript. Settings has an **AI** tab for it.

- **On this PC** (the default): choose **Small** or **Large** under
  **Model on this PC** and click **Download model**. The download is
  checked against its SHA-256 and kept in `%LOCALAPPDATA%\Naad\models`.
  The model runs on the graphics card through Vulkan when there is one,
  and on the processor otherwise. It loads the first time it is needed,
  which takes a few seconds, and unloads after 10 minutes without use. The
  first run on a graphics card can take up to a minute while the driver
  prepares; after that, the small model answers in under a second.
- **In the cloud**: choose **Cloud** and a **Cloud service**, then the
  model and, under **New key**, the service's key (see
  [The API keys](#the-api-keys)). A server on this PC, such as Ollama,
  needs no key. When a model refuses a
  `temperature`, Naad sends the request again without it and remembers
  that for a week in `%LOCALAPPDATA%\Naad\temperature-refused.json`.

## Cleanup

Before Naad pastes a transcript, it tidies it at the level chosen under
**Cleanup** on the **AI** tab:

- **Off:** pasted exactly as transcribed.
- **Basic:** instant rules: hesitations (um, uh) and a word said twice are
  removed, and the text starts with a capital and ends with a full stop.
- **AI** (the default): the language model removes filler words, false
  starts and self-corrections ("at three, no, four" becomes "at four"),
  fixes punctuation and spells your vocabulary right. Until a language
  model is set up (see above), AI tidies like Basic.

The AI step never costs you a dictation. It gets 8 seconds; if it takes
longer, fails, or gives a reply that looks like a chat answer or is far
longer than what you said, Naad pastes your words as transcribed, and the
overlay shows **Pasted as said** for a moment. A dictation of only
hesitations pastes nothing. The overlay shows **Cleaning up** while the
model works, and a model on this PC starts loading when you start
speaking.

In the cloud, a rate-limited model is skipped until its limit resets, for
as long as the provider says; a used-up daily quota is remembered across
restarts in `%LOCALAPPDATA%\Naad\rate-limits.json`, and the **AI** tab
shows when it resets. Set a **Fallback model** to keep AI cleanup going
meanwhile.

**Edit prompt...** shows the instructions the model gets. Change them, or
**Reset to default**; **Test** sends the sample text to the saved model and
shows the reply, how long it took and the full prompt. When a newer default
prompt ships, the dialog says so. **Test model** on the tab does the same
with the sample text.

## History

With `historyRetention` other than `"Off"`, Naad keeps each dictation: the
time, the app's name, the transcript before and after cleanup, and the
recording. Each is encrypted with Windows DPAPI for your account, in
`%LOCALAPPDATA%\Naad\history`. The **History** page lists them by day with
a search box; each can be copied, pasted again, played, transcribed again
(**Retry**, useful after a failed one), exported as a ZIP or deleted.
**Delete all** removes every one. The **Home** page counts your words,
speaking speed, time saved against typing at 40 words a minute, and days in
a row. The tray menu's **Paste again** and **History** items paste or copy
recent ones.

## Dictionary and snippets

The **Dictionary** page holds `vocabulary`: add, rename, delete and search
words. **Add copied words to dictionary** in the tray menu adds the words on
the clipboard. Naad 1.1's single vocabulary line is split into words when
the file migrates to version 4.

**Snippets** are phrases that expand: say the trigger ("my address") and the
snippet's text is pasted in its place. Triggers match whole words, in any
case. Snippets are stored encrypted in `%LOCALAPPDATA%\Naad\snippets.dpapi`,
not in settings.json, because they often hold personal details.

## Keys during a dictation

- **Esc** while recording cancels the dictation; during cleanup it cancels
  that too, and nothing is pasted. Esc goes to the app as usual otherwise.
- **Space** while holding the shortcut keeps recording after you let go
  (hands-free for one dictation). Press the shortcut again to stop.
- The `pasteAgainShortcut` pastes the last dictation again.

## Context and Edit Mode

With `context` on, when you start a dictation Naad reads, through Windows'
accessibility interface, the app's name and the text around the cursor
(at most 2,000 characters, within 150 ms) and gives it to the language
model as hints for spelling names. It is never pasted and never logged.
Nothing is read from a password field or an app in `contextExcludedApps`.
With a language model on this PC it never leaves the PC; with **Cloud** it
goes to the AI service with the transcript.

**Edit Mode** turns speech into an instruction when text is selected:
select a paragraph, hold the shortcut and say "make this more formal", and
the language model rewrites the selection, which Naad pastes over it.

- `"Automatic"`: speaking over a selection edits it.
- `"Manual"`: hold **Shift** with the shortcut to edit. In apps that don't
  expose their selection, Naad copies it with Ctrl+C and then restores your
  clipboard.
- `"Off"`: dictation replaces a selection like typing does.

If the edit fails or the model is slow, nothing is pasted and the selection
stays as it was; the overlay says so.

## Starting Naad when you sign in

**Start Naad when I sign in** in Settings adds a `Naad` value to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` pointing at the
Naad.exe you ticked it from, and unticking removes it. It needs no
administrator rights. It isn't stored in settings.json: Windows reads the
Run key, so that key is the only record. If you move Naad.exe, open
Settings and save with the box ticked to point the entry at the new place.

## What is deliberately not a setting

| Value | Why it is not in settings.json |
|---|---|
| API keys | They are secrets, and settings.json is plain text. It is stored encrypted on its own; see [The API keys](#the-api-keys). It must never be added to this file. |
| The 10-minute idle unload of the language model, the 10-second key check, the 300 ms tap threshold, the 5-minute recording limit, log size | Internal tuning values, not user preferences. |

A setting is added only when the app actually reads it.

## Versioning and migration policy

- `version` changes only when an existing setting is renamed, removed, or
  changes meaning. Adding a new setting with a default does not change it:
  older files simply get the default.
- Each version bump adds one migration step that turns a version-N file into
  version N + 1. Naad applies the steps in order, so any older file reaches
  the current version.
- A file without `version` comes from before versioning and is treated as
  version 0.
- Version 3 (Naad 1.1) chose cloud services by name. Migrating a version-2
  file sets `transcriptionService` and `languageModelService` from the
  service each base URL belongs to, or `"Custom"`.
- Version 4 (Naad 2.0) made `vocabulary` a list. Migrating a version-3
  file splits its text at commas, semicolons and line breaks.
- Before Naad migrates or repairs a file, it copies the original next to it
  (see below), so nothing is lost.
- A file with a **newer** version than this build supports was written by a
  newer Naad. This build never modifies or replaces it. It runs with defaults
  in memory and logs `SettingsFutureVersion`.

## What happens when something is wrong

| Situation | What Naad does | Log event |
|---|---|---|
| File missing | Creates it with defaults. | `SettingsCreated` |
| Valid file | Uses it as is; doesn't rewrite it. | `SettingsLoaded` |
| Missing setting | Uses that setting's default. | `SettingsLoaded` |
| Unknown setting | Ignores it. It stays in the file unless the file has to be rewritten. | `SettingsLoaded` |
| No `version` (older file) | Migrates it, backs up the original as `v0`, then rewrites it. | `SettingsMigrated from_version=0` |
| `version` 1 (before transcribing on this PC was the default) | Migrates it: without `transcriptionProvider` it keeps `"Cloud"`, as version 1 meant. Backs up the original as `v1`, then rewrites it. | `SettingsMigrated from_version=1` |
| Wrong-typed or invalid value, e.g. `"fileLogging": "no"` or `"dictationShortcut": "Ctrl"` | Uses that setting's default, backs up the original as `invalid`, then rewrites it. | `SettingsRepaired` |
| Empty file, invalid JSON, or an invalid `version` | Uses defaults, backs up the original as `corrupt`, then writes defaults. | `SettingsCorrupt` |
| Newer `version` than supported | Uses defaults in memory. The file is left exactly as it is. | `SettingsFutureVersion` |
| File can't be read (locked, access denied) | Uses defaults in memory. Doesn't retry or write. | `SettingsReadFailed` |
| Backup or write fails | Keeps the previous file intact and uses the new values in memory. | `SettingsWriteFailed` |

Naad always starts in all of these cases.

**Backups** are named `settings.json.<reason>-<UTC time>.bak`, for example
`settings.json.corrupt-20261025T101530123Z.bak`. The reason is `v<N>`,
`invalid` or `corrupt`. Naad never deletes backups; remove them yourself
once you no longer need them.

**Saving.** When you change settings in Naad itself, Naad replaces the file
with your new settings and makes no backup: the file then holds exactly what
you chose. Comments and unknown settings in a hand-edited file are not kept.

**Safe writes.** Naad writes the new content to `settings.json.tmp`, flushes
it to disk, and only then replaces `settings.json`. If that fails part-way,
the previous `settings.json` is still there.

**Privacy.** settings.json never holds API keys, transcripts, audio or
clipboard text, and a rewrite keeps only the known settings. Log events
about settings contain version numbers and exception type names only, never
values, file contents or paths.
