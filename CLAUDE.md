# Naad: instructions for contributors and coding agents

Naad is a Windows tray app for hold-to-talk voice typing. You hold a
shortcut (Right Ctrl by default) and speak. When you release it, Naad
transcribes the audio with Whisper on this PC by default, or through an
OpenAI-compatible API such as Groq, and pastes the text into the focused
app.

**This repository is the source of truth.** The committed versions of this
file, [docs/STYLE.md](docs/STYLE.md) and [docs/ROADMAP.md](docs/ROADMAP.md)
override any notes, memories or earlier conversations.

## Read first

- [docs/STYLE.md](docs/STYLE.md): code style, design rules, privacy rules,
  tests, Git conventions and the definition of done. Follow it exactly.
- [docs/ROADMAP.md](docs/ROADMAP.md): the phase plan. Work on one phase
  only, and stay inside its **In** and **Out** lists.

## Workflow rules (non-negotiable)

- **One phase at a time.** Do not start the next phase, and do not add work
  from a later phase, without the maintainer's approval.
- **No commits without explicit approval.** Make the changes, run the
  checks, then report:
  - the files changed
  - the proposed commit messages, with the files each one contains
  - the verification results

  Then stop. Commit only after the maintainer approves. Pushing and opening
  a pull request each need approval too.
- Work on a feature branch (`<type>/<area>-<topic>`) and merge through a
  pull request. **Never squash-merge**, and never force-push to `main`.
- Each commit is one coherent change. Don't split one change into several
  commits, and don't bundle unrelated changes into one.
- Do not install SDKs, tools or packages without asking first. A NuGet
  package may be added only in the phase that plans it, and must be
  recorded in `THIRD-PARTY-NOTICES.md` in the same commit.
- Do not change GitHub repository settings, secrets or branch protection
  without approval.
- Keep a dirty working tree intact: never discard or overwrite changes you
  did not make.

## The Suki reference repository

- Suki (MIT, macOS, Swift) is checked out at `D:\whisprflow\Suki`. It is a
  **read-only reference**: never modify it, and never add it to this
  repository.
- Use it for design ideas, not code. Suki's patterns not to copy:
  - one huge state class
  - API keys stored in plain text
  - user content written to logs
  - pasting without checking focus
- If Naad code is adapted from Suki, list the file under "Adapted in Naad"
  in `THIRD-PARTY-NOTICES.md` in the same commit.

## Privacy (summary of docs/STYLE.md section 6)

- Never log, commit or put in test fixtures any of the following:
  - audio or transcripts
  - clipboard contents
  - window titles
  - API keys
  - `.env` contents
- Do not read `.env` files.
- Never add telemetry, crash uploads or any new network destination without
  approval.

## Commands

Run these from the repository root. They need the .NET 10 SDK, which
`global.json` pins. CI (`.github/workflows/ci.yml`) runs the same steps on
`windows-latest`.

```sh
dotnet build                          # warnings and code-style violations are errors
dotnet test                           # xunit.v3 on Microsoft.Testing.Platform
dotnet format --verify-no-changes     # formatting gate; `dotnet format` fixes it
```

A test run that finds zero tests fails with exit code 8. That is deliberate:
it catches tests that silently stop being discovered.

## Verification before reporting a phase

- Run every check listed in the phase's **Verify** line, and report the
  real output.
- Never claim a manual test passed unless it was run. Mark it as pending
  instead.
- Review the complete diff (`git status`, `git diff`, and the content of
  new files) for unrelated edits, secrets and user content.
