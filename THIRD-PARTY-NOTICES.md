# Third-party notices

Naad is licensed under the MIT License (see [LICENSE](LICENSE)). This file
lists third-party work that Naad builds on, adapts or credits.

Rules for keeping this file accurate:

- When code is adapted from a project listed here, add the Naad file path
  under that project's "Adapted in Naad" list, in the same commit.
- When a dependency is added (a NuGet package, an asset or a model), add a
  section for it in the same commit: name, version, license and source URL.

## Suki

- Source: https://github.com/ansuman-shukla/Suki
- License: MIT
- Relationship: design reference. Naad is an independent Windows app
  inspired by Suki's macOS hold-to-talk dictation workflow. Suki is written
  in Swift for macOS, so Naad reuses ideas, not source files.
- Adapted in Naad: none yet.

Suki's license, reproduced as required for any adapted portions:

```text
MIT License

Copyright (c) 2026 Zach Latta
Copyright (c) 2026 Ansuman Shukla

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Suki's name, icons, sounds and artwork are not used in Naad.

## NAudio

- Packages: `NAudio.WinMM` 3.3.0, with its dependencies `NAudio.Core` 3.3.0
  and `NAudio.Midi` 3.3.0
- License: MIT, © Mark Heath 2026
- Source: https://github.com/naudio/NAudio
- Used for microphone capture and WAV encoding. Ships with Naad.

## Whisper.net

- Packages: `Whisper.net` 1.9.1 and `Whisper.net.Runtime` 1.9.1, with their
  dependencies `Whisper.net.Runtime.Metal` 1.9.1 (macOS only, unused on
  Windows) and `Microsoft.Extensions.AI.Abstractions` 10.2.0
- License: MIT, © 2024 sandrohanea (Microsoft.Extensions.AI.Abstractions:
  MIT, © Microsoft Corporation)
- Source: https://github.com/sandrohanea/whisper.net
- The runtime package ships native builds of whisper.cpp and ggml
  (MIT, © The ggml authors, https://github.com/ggml-org/whisper.cpp).
- Used for transcription on this PC (P20). Ships with Naad.

## Whisper base model (downloaded, not shipped)

- File: `ggml-base.bin` from https://huggingface.co/ggerganov/whisper.cpp,
  147,951,465 bytes, SHA-256
  `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe`
- License: MIT. OpenAI's Whisper weights (https://github.com/openai/whisper,
  © 2022 OpenAI), converted to the ggml format by the whisper.cpp project.
- Naad doesn't include it. It is downloaded only when the user clicks
  **Download model** in Settings, and kept only if its checksum matches.

## System.Numerics.Tensors

- Package: `System.Numerics.Tensors` 9.0.0, a dependency of NAudio
- License: MIT, © Microsoft Corporation
- Source: https://github.com/dotnet/runtime
- Ships with Naad.

## xUnit.net (test-only)

- Package: `xunit.v3` 4.0.2
- License: Apache-2.0
- Source: https://github.com/xunit/xunit
- Used only by `tests/Naad.Tests`; not shipped with Naad.
