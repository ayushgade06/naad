# Naad roadmap

This plan goes from an empty repository to Naad 1.0. Work happens one phase
at a time, and each phase follows this cycle:

1. Before starting, confirm the phase's scope.
2. Implement it on a feature branch.
3. Report the files changed, the proposed commit messages and the
   verification results.
4. **Commit only after the maintainer explicitly approves.**
5. Open a pull request and merge it without squashing.
6. The next phase starts only after that.

Each phase lists:

- **Goal:** what the phase achieves.
- **In:** what the phase includes.
- **Out:** what it leaves for later.
- **Files:** what it creates or changes.
- **Needs:** prerequisites and earlier phases.
- **Done when:** acceptance criteria.
- **Verify:** the exact checks to run.

`docs/manual-tests.md` is created in P3, and every later phase adds its
manual checks to it.

## Decisions already made

| Topic | Decision |
|---|---|
| Stack | C# on .NET 10 LTS, WinForms tray app, NAudio, Win32 through P/Invoke, xUnit |
| Speech-to-text | OpenAI-compatible HTTP API first, with Groq as the default. Local Whisper comes in P20. |
| Shortcut | Hold Right Ctrl by default. The implementation must support configurable chords. |
| Focus change | Never paste into a different window. Keep the transcript and notify the user. |
| OS | Windows 11 first; Windows 10 22H2 where practical |
| Signing | Unsigned for the first release; SmartScreen warnings are accepted |
| License | MIT. Suki's notices are kept in `THIRD-PARTY-NOTICES.md` for any adapted code. |
| Workflow | Feature branches and pull requests, never squash-merged. The maintainer approves every commit. |

## Milestones

| Milestone | After phase | Meaning |
|---|---|---|
| M0: Foundation | P2 | The repository, its rules and CI are in place |
| **M1: Vertical slice** | **P3** | Hold Right Ctrl → speak → text appears in Notepad. This is a go/no-go check of the stack. |
| M2: MVP `v0.1.0` | P16 | Hardened end-to-end dictation in Notepad, Chrome and VS Code |
| M3: Usable app | P19 | Settings window, overlay, robustness work |
| M4: `v1.0.0` | P24 | Local Whisper option, installer, auto-update, documentation |

---

## Foundation

### P1: Repository foundation

- **Goal:** set up the repository's legal files, rules and Git conventions
  before any code exists.
- **In:**
  - `README.md`, `LICENSE` (MIT), `THIRD-PARTY-NOTICES.md` (Suki)
  - `.gitignore`, `.gitattributes`, `.editorconfig`
  - `CLAUDE.md`, `docs/STYLE.md`, `docs/ROADMAP.md`
  - `git init` on `main` and adding the `origin` remote
- **Out:** application code, the .NET SDK, solution files, CI.
- **Needs:** none.
- **Done when:**
  - Every listed file exists and contains no placeholder text.
  - No file outside `D:\whisprflow\naad` is modified (Suki is untouched).
  - Nothing is committed until approved.
- **Verify:**
  - `git status` lists exactly the P1 files.
  - `git check-ignore` confirms sample secret, log and build paths are
    ignored.
  - Every file uses LF line endings and UTF-8.
  - Every relative link in the Markdown files resolves.
  - `git -C ../Suki status` is clean.

### P2: Solution skeleton and CI

- **Goal:** a .NET solution that builds, tests and formats the same way
  locally and in CI.
- **In:**
  - Check for the .NET 10 SDK. **If it is missing, stop and ask before
    installing anything.**
  - `global.json`: pin SDK 10.0, allow newer feature bands.
  - `Directory.Build.props`:
    - `net10.0-windows`
    - nullable on
    - `TreatWarningsAsErrors`
    - `EnforceCodeStyleInBuild`
    - `AnalysisLevel`
  - `Naad.slnx`.
  - `src/Naad/Naad.csproj` with a `Program.cs` that only starts and exits.
  - `tests/Naad.Tests` (xUnit) referencing the app project.
  - `.github/workflows/ci.yml` on `windows-latest`: restore, build,
    `dotnet format --verify-no-changes`, test. Actions pinned to commit
    SHAs, `permissions: contents: read`.
  - Update `CLAUDE.md` with the real build and test commands.
- **Out:** any app behaviour, NuGet packages beyond the test stack.
- **Needs:** P1 merged.
- **Done when:** `dotnet build`, `dotnet test` and
  `dotnet format --verify-no-changes` exit with 0 locally, and CI is green
  on the pull request.
- **Verify:**
  - Run the three commands and record their output.
  - Introduce a format violation deliberately and confirm CI fails.

## Vertical slice

### P3: Vertical slice (Right Ctrl → microphone → Groq → Notepad)

- **Goal:** prove the riskiest parts work together on real hardware before
  most of the app is built:
  - the low-level keyboard hook
  - NAudio capture
  - Groq transcription
  - clipboard plus `SendInput` paste
  - the focus check
- **In:**
  - The app runs as a console process (quit with Ctrl+C). It uses a
    WinForms message loop, because the hook needs one and the clipboard
    needs an STA thread.
  - `Native/User32.cs` holds the P/Invoke declarations.
  - A minimal `WH_KEYBOARD_LL` hook hard-coded to Right Ctrl, passed
    through to other apps. Auto-repeat and Naad's own injected keys are
    ignored.
  - NAudio `WaveIn` capture (named `WaveInEvent` before NAudio 3): default
    device, 16 kHz mono PCM16, held in memory.
  - A WAV encoder with unit tests.
  - A Groq `/audio/transcriptions` call:
    - model `whisper-large-v3-turbo`
    - key read from the `NAAD_GROQ_API_KEY` environment variable
    - 20-second timeout
  - Discard a recording if Right Ctrl was held for under 300 ms, or if
    another key was pressed while it was held (e.g. Right Ctrl+C). Taps and
    shortcuts must never send audio or paste text.
  - Remember the foreground window when the key is pressed. Before
    pasting, if the foreground window has changed, do not paste; leave the
    transcript on the clipboard and print a notice.
  - Wait for all modifier keys to be physically released, then set the
    clipboard text and send Ctrl+V.
  - Print only events, timings and byte counts.
  - Create `docs/manual-tests.md` with the slice checks.
  - Add NAudio and its dependencies to `THIRD-PARTY-NOTICES.md`.
- **Out:**
  - restoring the clipboard (the clipboard **is overwritten** in this
    phase)
  - tray icon, settings, overlay, retries
  - configurable chords
  - any change to the user's environment variables, which the maintainer
    sets
- **Needs:**
  - P2
  - a Groq API key set by the maintainer in their own shell
  - a working microphone
- **Done when:**
  - In Notepad, holding Right Ctrl, saying "hello world this is a test" and
    releasing inserts that text.
  - Other Ctrl shortcuts (Ctrl+C, Ctrl+V) still work while Naad runs.
  - Moving focus to another window before transcription finishes results
    in no paste.
  - Release-to-text latency is recorded.
- **Verify:**
  - `dotnet test` passes (WAV round-trip, RIFF size and empty-input tests).
  - Manual checks M-S1 to M-S5 in `docs/manual-tests.md` are run and
    their results recorded.
  - Write a short go/no-go note for each part of the stack in the pull
    request.

## Hardening the pieces into the MVP

Each phase below takes a rough part of the slice, replaces it with tested,
production-quality code, and keeps the slice working at every commit.

### P4: Tray shell and lifecycle

- **Goal:** a proper background app.
- **In:**
  - Switch to a `WinExe` WinForms message loop, with no console window.
  - An original Naad icon (`Assets/naad.ico`), used for the exe and the tray.
  - `Tray/TrayIcon.cs` with an Exit menu item.
  - `SingleInstanceGuard`: a named `Local\` mutex acquired before any hook or
    microphone work. A second launch exits with code 0. An abandoned mutex is
    taken over.
  - `StartupError`: fatal startup errors are shown in a dialog, with the API
    key redacted.
  - Clean shutdown in a fixed order: controller (stop triggers, cancel the
    transcription in flight), hook, recorder, HTTP client, tray, then mutex.
- **Out:** state icons (P16), settings (P17), start at login (P21).
- **Needs:** P3.
- **Done when:**
  - The app starts without a console window.
  - A second launch exits immediately without a second hook or icon.
  - Exit leaves no `Naad.exe` process and no tray icon behind.
  - Startup failures are visible and contain no secrets.
- **Verify:**
  - `dotnet test`: guard, redaction and icon tests.
  - UI Automation harness for M-T1 to M-T4.
  - Manual checks M-T5 and M-T6, which need physical keys.

### P5: Logging

- **Goal:** diagnostic logs that cannot leak user content.
- **In:**
  - `Log.cs` and the closed `LogEvent` enum. A line holds only an event,
    numeric fields with identifier names, and an exception's type name.
  - A file in `%LOCALAPPDATA%\Naad\logs`, rotated to one backup at 1 MB, and
    echoed to the console for `dotnet run`.
  - Handlers for unhandled exceptions on the UI thread, other threads and
    unobserved tasks. They replace WinForms' default exception dialog.
  - Lifecycle events, and the controller's console reports replaced by log
    events.
- **Out:** telemetry or remote reporting (never planned).
- **Needs:** P4.
- **Done when:**
  - Logs rotate at the size limit.
  - The API only accepts event names and numbers.
  - Tests prove that a transcript-like field name and an exception message
    are never written.
- **Verify:**
  - Unit tests for format, rotation and redaction.
  - Harness runs checking real log lines.
  - Manual check M-L1, which needs physical keys.

### P6: Settings persistence

- **Goal:** typed settings loaded from and saved to disk.
- **In:**
  - `Settings/AppSettings.cs` (a record: `version` and `fileLogging`) and
    `Settings/SettingsStore.cs`.
  - The file is `%APPDATA%\Naad\settings.json`. It is created with defaults
    on first run.
  - A schema version and an extendable migration table. A file without a
    version is treated as version 0.
  - Atomic writes (temp file, then replace), with a timestamped `.bak` of the
    original before any rewrite.
  - Recovery from missing, empty, malformed, wrong-typed, unreadable and
    newer-version files. A newer-version file is never modified.
  - Startup loads settings after the single-instance check. The log file
    starts only if `fileLogging` is on. Each outcome is logged as a fixed event.
  - `docs/settings.md` documents the format, the policy and excluded values.
- **Out:** the settings UI (P17), secrets (P12), the shortcut (P7), the
  provider and model (P13).
- **Needs:** P5.
- **Done when:**
  - Tests pass for: round-trip, missing file, missing and unknown fields,
    migration, corrupt and wrong-typed values, a future version, a failed
    write, logging failure, and that rewrites carry no stray content.
  - The real exe starts in every recovery case.
- **Verify:**
  - `dotnet test`.
  - P6 harness for M-C1 to M-C4 against the real exe.

### P7: Shortcut chord model and matcher (pure)

- **Goal:** configurable shortcuts, with Right Ctrl as the default.
- **In:**
  - `Hotkey/Chord.cs` and `Hotkey/ChordMatcher.cs`.
  - Left and right modifiers are distinct.
  - Emits a press when a chord becomes active and a release when it ends,
    or a cancel when another key goes down while it is held.
  - Ignores key auto-repeat.
  - The most specific matching chord wins; among equally long chords, the
    first configured.
  - Chords are serialized into settings as `dictationShortcut`
    (`"RightCtrl"` by default). An invalid value is repaired to the default.
  - The existing hook feeds the matcher, and the tray tooltip names the
    configured shortcut.
- **Out:** the UI for capturing a shortcut (P17); swallowing keys, the
  injected-input marker and the mouse hook (P8).
- **Needs:** P6.
- **Done when:** `[Theory]` tables cover:
  - Right Ctrl alone
  - Right Ctrl plus another key (must not trigger)
  - auto-repeat
  - releasing keys out of order
  - Left Ctrl not matching
- **Verify:**
  - `dotnet test`.
  - P7 harness: the real exe starts with the default, a custom and an
    invalid shortcut, and its tray tooltip names the shortcut in use.

### P8: Keyboard hook, hardened

- **Goal:** a reliable global hook driven by the matcher.
- **In:**
  - `Hotkey/KeyboardHook.cs`. The callback queues the event handlers. The
    matcher runs inside the callback, on the UI thread, because whether to
    swallow a key must be known before the callback returns; it does no I/O.
  - Pass-through or swallow per chord: a chord's non-modifier keys are
    swallowed, its modifiers always pass through. The matcher decides.
  - Ignore Naad's own injected input (`LLKHF_INJECTED` plus a marker in
    `dwExtraInfo`). Other programs' synthetic input counts as keys, except
    input injected by a lower-integrity process.
  - Cancel the recording when a mouse button or the wheel is used while
    the chord is held (`WH_MOUSE_LL`, installed only while it is held), so
    Ctrl+click and Ctrl+scroll are not treated as dictation.
- **Out:** reinstalling the hook after sleep (P19).
- **Needs:** P7.
- **Done when:**
  - Hold and release are detected correctly in Notepad, Chrome and VS Code.
  - No modifier key is left stuck.
  - Naad's own Ctrl+V does not trigger its own shortcut.
- **Verify:**
  - Manual checks M-H1 to M-H5; `dotnet test`.
  - P8 harness: the real exe skips injected input carrying Naad's marker.

### P9: Dictation state machine (pure)

- **Goal:** one clear owner for dictation state.
- **In:**
  - `Dictation/DictationSession.cs` with the states Idle, Recording,
    Transcribing and Inserting.
  - Holds shorter than the minimum are cancelled.
  - Recording stops automatically at the maximum duration (5 minutes).
  - Presses are ignored unless the state is Idle.
  - Job IDs prevent the same transcript being inserted twice.
  - `DictationController` carries out the session's actions and keeps no
    state flags of its own. A new recording waits until the previous one
    has closed the microphone.
- **Out:** toggle mode (P21).
- **Needs:** P7.
- **Done when:** a transition-table test covers every state and event pair.
- **Verify:**
  - `dotnet test`, including a check that the table lists every pair.
  - Smoke run of the real exe (start, `AppStarted`, no errors).

### P10: Audio capture, hardened

- **Goal:** microphone capture that fails safely.
- **In:**
  - `Audio/AudioRecorder.cs` and `Audio/MicrophoneFailure.cs`.
  - Device-open failures are returned as typed errors: not found, blocked,
    in use, or failed with a WinMM code.
  - Detect microphone access blocked by Windows privacy settings and link
    to `ms-settings:privacy-microphone`. The block is read from Windows'
    consent store before opening, because a blocked device may record
    silence instead of failing.
  - A tray notification explains each error; for a block, clicking it
    opens the privacy page.
  - The recording buffer is thread-safe: one buffer per recording, behind
    a lock.
  - Disposal is deterministic: Start, Stop and Dispose share one lock, and
    a stop the driver never confirms gives up after 2 seconds.
- **Out:** choosing a device (P17), hot-plug handling (P19).
- **Needs:** P9.
- **Done when:**
  - Recording works with the default microphone.
  - With no microphone or access blocked, a clear error is shown and
    nothing crashes.
- **Verify:**
  - Manual checks M-A1 to M-A3; `dotnet test`.
  - Smoke run of the real exe (start, `AppStarted`, no errors).

### P11: WAV encoding and recording gates

- **Goal:** do not upload junk audio.
- **In:**
  - `Audio/Wav.cs` replaces the slice's encoder.
  - `Audio/SilenceGate.cs` checks minimum duration and peak/RMS level.
- **Out:** voice-activity detection models.
- **Needs:** P10.
- **Done when:**
  - The WAV header matches a fixture.
  - Silent buffers and clips under 300 ms are rejected.
- **Verify:** `dotnet test`.

### P12: Secret store

- **Goal:** API keys encrypted at rest.
- **In:**
  - `Settings/SecretStore.cs` using DPAPI (CurrentUser scope).
  - The `NAAD_GROQ_API_KEY` environment variable can override the stored
    key during development.
- **Out:** the key entry UI (P17).
- **Needs:** P6.
- **Done when:**
  - Round-trip tests pass.
  - The stored file does not contain the key in plain text.
  - The key never appears in logs.
- **Verify:** `dotnet test`; manual check M-K1.

### P13: Transcription provider, hardened

- **Goal:** a tested OpenAI-compatible client.
- **In:**
  - `Transcription/ITranscriber.cs`,
    `Transcription/OpenAiCompatibleTranscriber.cs` and
    `Transcription/TranscriptionError.cs`.
  - A configurable base URL and model.
  - Timeout and cancellation.
  - HTTP status codes mapped to typed errors.
  - A filter for Whisper's filler hallucinations.
- **Out:** retries (P19), local Whisper (P20).
- **Needs:** P12.
- **Done when:** fake-handler tests cover 200, 401, 413, 429, 5xx,
  timeout and an empty result.
- **Verify:** `dotnet test`; manual check M-G1 (one live Groq call).

### P14: Clipboard insertion, hardened

- **Goal:** paste without losing the user's clipboard.
- **In:**
  - `Insertion/ClipboardSnapshot.cs` and `Insertion/TextInserter.cs`.
  - Snapshot the clipboard, write the transcript, send Ctrl+V, then
    restore the original only if the clipboard is unchanged.
  - Retry `OpenClipboard`.
  - Keep transcripts out of Windows clipboard history.
  - Wait for modifier keys to be released before pasting.
- **Out:** the focus guard (P15).
- **Needs:** P8.
- **Done when:**
  - Restore-policy tests pass.
  - Pasting works in Notepad, Chrome and VS Code, and the previous
    clipboard text comes back.
  - Transcripts do not appear in Win+V history.
- **Verify:** `dotnet test`; manual checks M-I1 to M-I6.

### P15: Focus guard and fallback

- **Goal:** never paste into the wrong window.
- **In:**
  - Compare the foreground window from when the shortcut was pressed with
    the one about to receive the paste.
  - If it changed, keep the transcript, show a notification, and offer
    "paste last transcript".
  - Detect an elevated target window.
  - Fall back to Unicode `SendInput` typing.
  - **As built:** the kept transcript is offered as **Copy last
    transcript** (tray menu, or click the notification), not "paste".
    Opening the tray menu moves focus to the taskbar, so a paste from it
    would either go to the wrong window or have to steal focus back,
    which this phase forbids. Copying puts it on the clipboard, out of
    clipboard history, for the user to paste with Ctrl+V. Unicode typing
    is used only when the clipboard can't be opened or written, so
    nothing was pasted and the text can't be inserted twice.
- **Out:** none.
- **Needs:** P14.
- **Done when:**
  - Switching windows during transcription never pastes.
  - Pasting into an administrator terminal shows a notification instead
    of silently losing the text.
- **Verify:** manual checks M-F1 to M-F4; unit tests for the decision
  logic.

### P16: MVP integration, `v0.1.0`

- **Goal:** the hardened pieces connected and released as the MVP.
- **In:**
  - `Dictation/DictationController.cs` orchestrates the pieces and contains
    no Win32 code.
  - The tray icon shows the current state.
  - Start and stop sounds (original or public-domain assets).
  - Notifications for errors.
  - `CHANGELOG.md` and the `v0.1.0` tag.
  - **As built:** the controller takes its collaborators (recorder,
    transcriber, inserter, focus checks, clock) through its constructor,
    so `DictationControllerTests` runs whole dictations without a
    keyboard, microphone, network or real clipboard. Tones are
    synthesized at startup and state dots are drawn over the icon, so
    there are no new asset files. The `v0.1.0` tag is created only once
    the manual checklist has passed and the maintainer says to.
- **Out:** installer (P22).
- **Needs:** P4–P15.
- **Done when:** the full manual checklist passes in Notepad, Chrome and
  VS Code on Windows 11.
- **Verify:** every manual check in `docs/manual-tests.md`;
  `dotnet test`; CI green.

## Making it a usable app

### P17: Settings window

- **Goal:** configure Naad without editing files.
- **In:**
  - API key field with a "test key" button.
  - Shortcut capture.
  - Microphone picker.
  - Base URL, model and language.
  - Changes apply immediately.
  - **As built:** language was moved here from P21, so it is built once.
    Saving applies every setting at once from `Program`, the composition
    root, or after the dictation running at that moment ends; the hook,
    recorder, controller, tray and log each gained one small method for
    it. The key is write-only: the window shows where it comes from, never
    the key. **Test key** sends `GET {baseUrl}/models` and reads only the
    status (404 or 405 means the provider can't check keys). Without a
    usable key Naad starts and opens the window instead of exiting, and
    the shortcut asks for a key instead of recording. Capturing a shortcut
    swallows every key, raises no dictation events, and ends on release,
    Esc, or when the window closes or loses focus.
- **Needs:** P16.
- **Done when:** every setting takes effect without a restart and stays
  after a restart.
- **Verify:** manual checks M-U1 to M-U6; tests for validation.

### P18: Recording overlay

- **Goal:** visible feedback while recording.
- **In:** a non-activating, always-on-top window showing a level meter and
  the current state.
  - **As built:** `Tray/RecordingOverlay.cs` uses `ShowWithoutActivation`
    and `WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW |
    WS_EX_TRANSPARENT`, and is slightly see-through so Windows treats it
    as layered and lets clicks through. It shows on the target window's
    monitor and hides when idle. `AudioRecorder.LevelMeasured` raises each
    buffer's RMS on the capture thread; `Program` marshals it to the UI
    thread, and the pure `Audio/LevelMeter.cs` maps it from -60 dBFS to
    full scale in decibels, with a decay of a full bar per half second.
    No setting turns it off yet.
- **Needs:** P16.
- **Done when:** the overlay never takes focus or keyboard input.
- **Verify:** manual checks M-O1 to M-O3; tests for meter normalization.

### P19: Robustness

- **Goal:** survive the real world.
- **In:**
  - Microphone hot-plug.
  - Reinstall the hook after sleep and resume, plus a hook health check.
  - Network loss, with one retry on connection failure.
  - A "paste last transcript" tray action.
  - **As built:** the microphone is opened afresh per recording, so
    hot-plug needs no device watcher; a device that fails mid-recording
    keeps the audio heard so far. The hook is reinstalled on resume and
    unlock, and the health check is a 2-second resync of held keys
    against `GetAsyncKeyState` (pure `ChordMatcher.Resync`), which also
    lifts P7's lost-key-up limit for keys that aren't swallowed. The pure
    `RetryPolicy` retries once, after 1 s, only when the request never
    reached the server, within the same 20-second limit. "Paste last
    transcript" stays **Copy last transcript** from P15: a paste from the
    tray menu would go to the wrong window.
- **Needs:** P16.
- **Done when:** every scenario in the robustness section of the manual
  checks passes.
- **Verify:** manual checks M-R1 to M-R6; tests for the retry policy.

## Toward 1.0

### P20: Local Whisper

- **Goal:** offline, private transcription as an option.
- **In:**
  - A second `ITranscriber`: Whisper.net.
  - Model download with a checksum check.
  - Switching between providers.
  - Add the model and the library to `THIRD-PARTY-NOTICES.md`.
  - **As built:** `transcriptionProvider` (`Cloud` or `Local`) and the
    pure `ProviderSelection`, which never falls back to the other
    provider, so choosing local transcription can't upload audio.
    `LocalModel` downloads whisper.cpp's multilingual `ggml-base.bin`
    (148 MB) only when the user asks, hashes it as it arrives and keeps it
    only if the SHA-256 matches. `LocalWhisperTranscriber` loads it lazily
    off the UI thread and frees it when the provider changes. Measured on
    the maintainer's PC: 9.8 s of speech took 1.5 s (2.0 s with the model
    load), using half the logical processors. Manual checks are M-LW1 to
    M-LW4, since P11 already uses M-W1 to M-W3.
- **Needs:** P17.
- **Done when:**
  - Dictation works with the network disconnected.
  - Latency and CPU use are measured and documented.
- **Verify:** manual checks M-W1 to M-W4; tests for provider selection.

### P21: Quality features

- **Goal:** small, high-value additions.
- **In:**
  - Custom vocabulary, sent through Whisper's `prompt` field.
  - Toggle (hands-free) mode.
  - Start at login (`HKCU\...\Run`).
  - **As built:** language choice moved to P17. `vocabulary` (up to 500
    characters, one line) is sent as `prompt` and never logged.
    `handsFree` maps the second press onto the release that ends a held
    dictation, so the short-hold check, cancels and the 5-minute limit
    work the same in both modes. Start at login lives only in the Run key
    (`Settings/StartupEntry.cs`), never in settings.json, and is offered
    in Settings.
- **Needs:** P17.
- **Done when:** each feature has its own tests and manual checks.
- **Verify:** `dotnet test`; manual checks M-Q1 to M-Q5.

### P22: Packaging

- **Goal:** an installable build.
- **In:**
  - Velopack per-user installer.
  - Version stamping.
  - Release workflow triggered by tags.
  - Unsigned (SmartScreen notes in the README).
  - **As built:** `release.yml` runs only on `v*.*.*` tags: test, publish
    self-contained win-x64 with `-p:Version` from the tag, `vpk pack` with
    package id `Naad.App`, and `vpk upload github` with the built-in
    token. The id isn't `Naad` because Velopack installs into
    `%LOCALAPPDATA%\<id>` and would land on Naad's user data. `Program`
    runs Velopack's hooks first; uninstalling removes the Run entry and
    leaves user data, which the README tells users how to delete.
    Packages are full only (no deltas yet). The workflow's commands were
    dry-run locally; the workflow itself runs on the first tag.
- **Needs:** P16.
- **Done when:** a clean Windows 11 VM can install, run and uninstall Naad
  with nothing left behind except user data, which the user chooses to
  keep or remove.
- **Verify:** manual checks M-P1 to M-P4 on a clean VM.

### P23: Auto-update

- **Goal:** users get fixes without reinstalling.
- **In:** Velopack updates from GitHub Releases, applied with the user's
  consent.
  - **As built:** `Updates/AppUpdater.cs` wraps Velopack's
    `UpdateManager` with `GithubSource` (stable releases only) and does
    nothing in a copy that wasn't installed. `Updates/UpdatePrompt.cs`
    checks a minute after start and daily, plus **Check for updates** in
    the tray (moved out of `Program`, which had passed 500 lines). An
    update is only downloaded when its notification is clicked, never
    during a dictation; Naad then exits normally and Velopack applies it
    and restarts. Logs carry the version as numbers only.
- **Needs:** P22.
- **Done when:** a release N installation updates to N+1 and keeps its
  settings.
- **Verify:** manual checks M-P5 and M-P6.

### P24: 1.0 hardening and documentation

- **Goal:** ship `v1.0.0`.
- **In:**
  - Privacy document, troubleshooting guide, architecture page.
  - A full manual pass on Windows 11 and Windows 10 22H2.
- **Needs:** P17–P23.
- **Done when:** every manual check passes and the documentation matches
  the app's behaviour.
- **Verify:** the full manual checklist; CI green.
