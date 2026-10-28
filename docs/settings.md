# Settings

Naad keeps its settings in `%APPDATA%\Naad\settings.json`, for example
`C:\Users\<you>\AppData\Roaming\Naad\settings.json`. The file is created
with defaults the first time Naad starts. You can edit it with any text
editor while Naad is closed; changes apply the next time Naad starts.

```json
{
  "version": 1,
  "fileLogging": true,
  "dictationShortcut": "RightCtrl"
}
```

| Setting | Type | Default | Meaning |
|---|---|---|---|
| `version` | integer | `1` | The file's schema version. Naad manages it; don't change it. |
| `fileLogging` | `true` or `false` | `true` | Write the diagnostic log to `%LOCALAPPDATA%\Naad\logs`. The log never contains what you said (see `docs/manual-tests.md`, M-L1). With `false`, Naad writes no log file at all. |
| `dictationShortcut` | text | `"RightCtrl"` | The keys you hold while dictating. See [The dictation shortcut](#the-dictation-shortcut). |

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

To store or replace the key, pipe it into Naad from PowerShell. The key is
read without being shown, and never lands in the shell's history:

```powershell
[Net.NetworkCredential]::new('', (Read-Host -AsSecureString 'API key')).Password |
  & "<path to>\Naad.exe" --set-api-key | Out-Null
```

From a source checkout, use `dotnet run --project src/Naad -- --set-api-key`
in place of the `& ... Naad.exe` part. `| Out-Null` makes PowerShell wait
for Naad to finish; `$LASTEXITCODE` is then `0` once the key is stored.
Naad accepts the key only from a pipe: `--set-api-key` without one shows how
to use it and changes nothing. The key takes effect the next time Naad
starts.

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
- **No key:** Naad shows a dialog explaining how to store one, logs
  `ApiKeyMissing` and exits.
- **Unreadable key** (damaged, or saved by another Windows user): Naad shows
  a dialog asking you to store it again, logs `ApiKeyUnreadable` with the
  exception type, and exits. It doesn't delete the file.

The key never appears in settings.json, the log, error dialogs or test
files. To remove it, delete `api-key.dpapi` while Naad is closed.

## What is deliberately not a setting

| Value | Why it is not in settings.json |
|---|---|
| API key | It is a secret, and settings.json is plain text. It is stored encrypted on its own; see [The API key](#the-api-key). It must never be added to this file. |
| Transcription endpoint and model | Fixed to Groq `whisper-large-v3-turbo` until P13 makes the provider configurable. |
| Timeouts, the 300 ms tap threshold, the 5-minute recording limit, log size | Internal tuning values, not user preferences. |

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
- Before Naad rewrites a file for any reason, it copies the original next to
  it (see below), so nothing is lost.
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

**Safe writes.** Naad writes the new content to `settings.json.tmp`, flushes
it to disk, and only then replaces `settings.json`. If that fails part-way,
the previous `settings.json` is still there.

**Privacy.** settings.json never holds API keys, transcripts, audio or
clipboard text, and a rewrite keeps only the known settings. Log events
about settings contain version numbers and exception type names only, never
values, file contents or paths.
