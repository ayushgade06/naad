# Settings

The easiest way to change settings is **Settings...** in Naad's tray menu.
Changes apply as soon as you click **Save**, without a restart; if you are
dictating at that moment, they apply when the dictation ends.

Naad keeps its settings in `%APPDATA%\Naad\settings.json`, for example
`C:\Users\<you>\AppData\Roaming\Naad\settings.json`. The file is created
with defaults the first time Naad starts. You can also edit it with any text
editor while Naad is closed; such changes apply the next time Naad starts.

```json
{
  "version": 1,
  "fileLogging": true,
  "playSounds": true,
  "dictationShortcut": "RightCtrl",
  "transcriptionBaseUrl": "https://api.groq.com/openai/v1",
  "transcriptionModel": "whisper-large-v3-turbo",
  "language": null,
  "microphone": null,
  "vocabulary": null,
  "handsFree": false
}
```

| Setting | Type | Default | Meaning |
|---|---|---|---|
| `version` | integer | `1` | The file's schema version. Naad manages it; don't change it. |
| `fileLogging` | `true` or `false` | `true` | Write the diagnostic log to `%LOCALAPPDATA%\Naad\logs`. The log never contains what you said (see `docs/manual-tests.md`, M-L1). With `false`, Naad writes no log file at all. |
| `playSounds` | `true` or `false` | `true` | Play a short, soft tone when Naad starts listening and when it stops. |
| `dictationShortcut` | text | `"RightCtrl"` | The keys you hold while dictating. See [The dictation shortcut](#the-dictation-shortcut). |
| `transcriptionBaseUrl` | text | `"https://api.groq.com/openai/v1"` | The root of any OpenAI-compatible API; Naad posts to `<root>/audio/transcriptions`. It must be `https://`, or `http://` only for a server on this PC (`localhost`, `127.0.0.1`), because the API key is sent with every request. A query string isn't allowed. |
| `transcriptionModel` | text | `"whisper-large-v3-turbo"` | The provider's model id, such as `whisper-1` for OpenAI. Up to 100 visible ASCII characters, no spaces. |
| `language` | text or `null` | `null` | The language you speak, as a lowercase [ISO 639-1](https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes) code such as `"en"` or `"hi"`; a few providers also take three-letter codes. Naad sends it with each recording, which makes transcripts faster and more accurate. `null` (or `""`) lets the provider detect the language. |
| `vocabulary` | text or `null` | `null` | Names and terms you use, such as `"Naad, Groq, Priya"`, sent with each recording as Whisper's prompt so they are spelled right. One line, up to 500 characters. It goes to the transcription provider but never into the log. `null` (or `""`) sends none. |
| `handsFree` | `true` or `false` | `false` | With `true`, press the shortcut once to start dictating and again to stop, instead of holding it. |
| `microphone` | text or `null` | `null` | The recording device's name, exactly as Windows lists it, for example `"Microphone (USB Audio Device)"`. `null` (or `""`) follows the Windows default recording device. If the named device isn't connected, Naad records from the default instead and logs `MicrophoneFallbackToDefault`. Windows shortens these names to 31 characters, so two devices whose names start the same can't be told apart; Naad then uses the first. |

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

## The API key

Naad needs an API key for the transcription provider (Groq by default; get
one at https://console.groq.com/keys). It is stored encrypted, apart from
settings.json, in `%LOCALAPPDATA%\Naad\api-key.dpapi`.

To store or replace the key, paste it into **New key** in Settings and click
**Save**. **Test key** first asks the provider whether it accepts the key
(Naad sends `GET <transcriptionBaseUrl>/models`); a provider that can't
list models can't check keys, and Naad says so. Settings never shows a
stored key, only where the key in use comes from.

You can also pipe the key into Naad from PowerShell. The key is
read without being shown, and never lands in the shell's history:

```powershell
[Net.NetworkCredential]::new('', (Read-Host -AsSecureString 'API key')).Password |
  & "<path to>\Naad.exe" --set-api-key | Out-Null
```

From a source checkout, use `dotnet run --project src/Naad -- --set-api-key`
in place of the `& ... Naad.exe` part. `| Out-Null` makes PowerShell wait
for Naad to finish; `$LASTEXITCODE` is then `0` once the key is stored.
Naad accepts the key only from a pipe: `--set-api-key` without one shows how
to use it and changes nothing. A key stored this way takes effect the next
time Naad starts.

How it is protected and found:

- **Encryption:** Windows DPAPI with the CurrentUser scope. Only your
  Windows account, on this PC, can decrypt the file. Another user, another
  PC, or a copy of the file on a backup can't. Resetting your Windows
  password from outside your account (for example by an administrator) can
  make the file unreadable; store the key again if that happens.
- **Development override:** if the `NAAD_GROQ_API_KEY` environment variable
  is set and not blank, Naad uses it and doesn't read the stored key. The
  log says which one was used (`ApiKeyFromEnvironment` or `ApiKeyLoaded`),
  never the key itself.
- **No key:** Naad logs `ApiKeyMissing`, starts anyway and opens Settings
  so you can add one. Pressing the shortcut without a key shows a
  notification instead of recording (`DictationNeedsApiKey`).
- **Unreadable key** (damaged, or saved by another Windows user): the same,
  with `ApiKeyUnreadable` and the exception type logged. Naad doesn't delete
  the file; saving a new key replaces it.

The key never appears in settings.json, the log, error dialogs or test
files. To remove it, delete `api-key.dpapi` while Naad is closed.

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
| API key | It is a secret, and settings.json is plain text. It is stored encrypted on its own; see [The API key](#the-api-key). It must never be added to this file. |
| Timeouts (20 s per transcription request), the 300 ms tap threshold, the 5-minute recording limit, log size | Internal tuning values, not user preferences. |

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
