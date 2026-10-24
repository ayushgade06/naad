# Naad style guide

This file is the source of truth for how Naad code, tests and Git history
are written. When this file and any other note disagree, this file wins.
Change it in its own commit, with the reason in the commit body.

The guiding rule: **the smallest design that is reliable, readable and
testable.** Code nobody wrote can't break.

## 1. Project layout

This is the target layout. Each folder is created only when its first file
is added; do not create empty folders or placeholder files.

```text
naad/
  Naad.slnx
  Directory.Build.props        shared compiler settings
  src/Naad/                    the app (the only production project)
    Program.cs                 entry point; builds every object by hand
    Audio/  Dictation/  Hotkey/  Insertion/  Settings/  Transcription/  Tray/
    Native/                    every P/Invoke declaration, nowhere else
  tests/Naad.Tests/            mirrors src/Naad folder by folder
  docs/
```

- Organize folders **by feature**, not by layer. Do not create `Services/`,
  `Helpers/`, `Utils/`, `Managers/` or `Models/` folders.
- Keep one production project. Add a class library only when a second
  project needs to use the code.
- Keep one top-level type per file, and name the file after it.
- Aim for files under 300 lines. A file over 500 lines must be split by
  responsibility before more code is added to it.
- No god object. The orchestrator (`DictationController`) coordinates other
  classes and contains no Win32, HTTP or UI code itself.

## 2. Separate pure logic from side effects

Anything that can be decided without the OS must live in a class that never
calls Win32, the clipboard, the microphone, the network or the UI. Examples:

- the dictation state machine
- shortcut chord matching
- WAV encoding and the silence check
- transcription response parsing and error mapping
- the clipboard restore decision

These classes get unit tests. The thin classes that do the side effects stay
small enough to check by hand with `docs/manual-tests.md`.

## 3. C# conventions

These rules are enforced by `.editorconfig` and the build settings, which
treat warnings as errors.

- **Language:** latest C# for .NET 10. Nullable is on, implicit usings are on.
- **Namespaces:** file-scoped. `using` directives go outside the namespace,
  with `System` first.
- **Visibility:** types are `internal sealed` unless there is a reason
  otherwise. Every member has an explicit access modifier.
- **Data:** `record` or `record struct` for immutable data. Use primary
  constructors when they make the class shorter and clearer.
- **Naming:**
  - `PascalCase` for types, members, constants and `static readonly` fields
  - `_camelCase` for private instance fields
  - `camelCase` for locals and parameters
  - `I` prefix for interfaces
  - async methods end in `Async`
- **Name things by their job:** `AudioRecorder.StopAsync()`, not
  `AudioHelper.DoStop()`. Do not use `Manager`, `Helper`, `Util` or `Base`
  in names.
- **`var`:** only when the type is obvious from the right-hand side.
- **Braces:** always, even around single-line bodies.
- **Line length:** at most 120 characters.

## 4. Design rules

- **Interfaces only at real seams.** Add an interface only when there are
  two or more implementations (`ITranscriber`), or when a test needs to
  replace an OS side effect. Never add one "for later".
- **No frameworks the app does not need.** No dependency-injection
  container, MediatR, Rx, AutoMapper or logging framework. Wire objects
  together by hand in `Program.cs`.
- **No speculative code.** No configuration for a value that never changes,
  no extension points without a second user, no commented-out code.
- **Constants:** every magic number becomes a named `const` with a comment
  saying why it has that value:

  ```csharp
  // Groq rejects uploads above 25 MB; 16 kHz mono PCM16 reaches that after ~13 min.
  private const int MaxRecordingSeconds = 300;
  ```

- **Known limits:** when a deliberate simplification has a known limit,
  mark it with a `ponytail:` comment that names the limit and how to lift
  it:

  ```csharp
  // ponytail: restores text formats only; add bitmap/file-drop formats if users report loss.
  ```

## 5. Async, threading and errors

- Use `async`/`await` all the way through. Never call `.Result`, `.Wait()` or
  `Thread.Sleep` on the UI thread.
- Every network or long-running call takes a `CancellationToken` and has a
  timeout.
- Code that must run on the UI thread says so in a comment: hooks,
  clipboard, tray.
- The low-level keyboard hook callback only queues the event and returns.
  Windows removes hooks whose callback is slow.
- **Expected failures are values.** No microphone, an empty recording, the
  network being down, HTTP 429, or focus moving to another window are
  returned as typed results (for example `TranscriptionError`), not thrown.
- **Bugs throw.** Use `ArgumentException`, `InvalidOperationException` and
  `UnreachableException` for broken assumptions.
- Never swallow an exception silently. Log its type and the event name.
- Dispose every OS handle: hooks, audio devices, mutexes. Use `using`
  statements or `IDisposable`.

## 6. Privacy and security (hard rules)

- **Never log** audio, transcripts, clipboard contents, window titles,
  process names of the target app, HTTP request or response bodies, or API
  keys. Logs contain event names, durations, byte counts, HTTP status codes
  and exception types only.
- Log only through `Log` with a `LogEvent` and numeric fields; never write
  diagnostics with `Console` or string messages. A new event is a new enum
  member.
- Audio stays in memory. If a temporary file is ever unavoidable, delete it
  in a `finally` block.
- API keys are stored with DPAPI (CurrentUser scope). For local
  development, an environment variable may override the stored key. Keys
  never go in `settings.json`, in tests, or in the repository.
- Tests use only invented, synthetic data.
- Any change that touches what is logged, what is stored, network requests,
  the clipboard or input injection must say so in the pull request.

## 7. Tests

- Use xUnit v3, run by Microsoft.Testing.Platform. Keep the test project in
  `tests/Naad.Tests`, with folders that mirror `src/Naad`.
- Test classes are `public sealed`, because xUnit requires public test
  classes. They reach internal production types through
  `InternalsVisibleTo`.
- Name tests `Method_Condition_ExpectedResult`, for example
  `Reduce_RightCtrlReleased_EmitsReleased`.
- Use Arrange / Act / Assert, with one behaviour per test. Prefer
  `[Theory]` tables for state machines and parsers.
- Never call live services. For HTTP, use a fake `HttpMessageHandler`.
- Every deterministic bug fix gets a regression test, in the same commit as
  the fix.
- Win32 behaviour (hooks, the microphone, `SendInput`, the clipboard, focus)
  is verified with numbered steps in `docs/manual-tests.md`. Do not build
  elaborate mocks of Win32. Never claim a manual test passed unless it was
  run.

## 8. Comments and documentation

- Comments explain **why**: Win32 quirks, timing values, the reason for a
  workaround. Never restate what the code does.
- Add XML doc comments only on members whose contract is not obvious from
  the name.
- When a change affects the user, the architecture or the manual tests,
  update `README.md`, `docs/ROADMAP.md` or `docs/manual-tests.md` in the
  same pull request.

## 9. Git conventions

### Branches

- Create every branch from `main`.
- Name it `<type>/<area>-<short-topic>`, for example
  `feat/hotkey-chord-matcher`, `fix/insert-clipboard-restore` or
  `docs/style-guide`.
- Use one branch per phase or sub-phase.

### Commits

- Follow [Conventional Commits](https://www.conventionalcommits.org/)
  with a required area:

  ```text
  <type>(<area>): <imperative summary, lowercase, no period>

  <body: why the change is needed, and anything a reviewer must know>
  ```

- **Types:** `feat`, `fix`, `refactor`, `test`, `docs`, `build`, `ci`,
  `chore`, `perf`.
- **Areas:** `audio`, `hotkey`, `stt`, `insert`, `dictation`, `tray`,
  `settings`, `secrets`, `log`, `build`, `ci`, `repo`, `docs`.
- Keep the subject line at 72 characters or fewer.
- Each commit is one logical change that builds and passes the tests on its
  own. Do not combine unrelated changes in one commit.
- A test goes in the same commit as the code it covers.
- Never split one change into fake steps.
- Create commits only after the maintainer has reviewed the changes and
  said to commit.

### Pull requests

- Open one pull request per branch. The description covers:
  - **Summary**
  - **Why**
  - **Changes**
  - **Verification:** commands run and their results, plus manual steps
  - **Risk:** privacy, permissions, input injection
- **Merge with a merge commit or rebase-merge. Never squash**, so the
  granular commits stay in the history.
- Never force-push to `main`.

## 10. Definition of done (for every phase)

1. The phase scope is fully implemented, with no unrelated edits.
2. `dotnet build` (warnings as errors), `dotnet test` and
   `dotnet format --verify-no-changes` pass locally and in CI.
3. New logic has tests, or the pull request explains why it can't.
4. The phase's manual checks in `docs/manual-tests.md` were run and their
   results recorded, or are clearly marked pending.
5. No secrets or user content were added. The privacy rules in section 6
   still hold.
6. Documentation is updated.
7. The maintainer reviewed the work and approved the commits.
