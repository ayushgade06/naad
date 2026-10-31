# Third-party notices

Naad is licensed under the MIT License (see [LICENSE](LICENSE)). This file
lists third-party work that Naad builds on, adapts or credits.

Rules for keeping this file accurate:

- When code is adapted from a project listed here, add the Naad file path
  under that project's "Adapted in Naad" list, in the same commit.
- When a dependency is added (a NuGet package, an asset or a model), add a
  section for it in the same commit: name, version, license and source URL.

## LLamaSharp

- Packages: `LLamaSharp` 0.30.0, `LLamaSharp.Backend.Cpu` 0.30.0 and
  `LLamaSharp.Backend.Vulkan` 0.30.0
- License: MIT, © 2023 Rinne and the LLamaSharp contributors
- Source: https://github.com/SciSharp/LLamaSharp
- The backend packages ship native builds of llama.cpp and ggml (MIT,
  © The ggml authors, https://github.com/ggml-org/llama.cpp). The Vulkan
  build uses the Vulkan loader installed with the graphics driver; Naad
  doesn't ship the loader. Their Linux and macOS builds are left out of
  Naad's output.
- Used to run a language model on this PC (P26). Ships with Naad.

## Language models (downloaded, not shipped)

- `qwen2.5-1.5b-instruct-q4_k_m.gguf`, 1,117,320,736 bytes, SHA-256
  `6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e`, from
  https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF. Qwen2.5-1.5B-Instruct,
  © Alibaba Cloud (Qwen team), Apache-2.0; 4-bit conversion by the Qwen team.
- `Qwen3-4B-Instruct-2507-Q4_K_M.gguf`, 2,497,281,120 bytes, SHA-256
  `3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597`, from
  https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF.
  Qwen3-4B-Instruct-2507, © Alibaba Cloud (Qwen team), Apache-2.0; 4-bit
  conversion by Unsloth AI, Apache-2.0.
- Apache-2.0 allows redistribution and commercial use. Naad doesn't include
  the models; one is downloaded only when the user asks for it in Settings,
  and kept only if its checksum matches.

## NAudio

- Packages: `NAudio.WinMM` 3.3.0, with its dependencies `NAudio.Core` 3.3.0
  and `NAudio.Midi` 3.3.0
- License: MIT, © Mark Heath 2026
- Source: https://github.com/naudio/NAudio
- Used for microphone capture and WAV encoding. Ships with Naad.

## Velopack

- Package: `Velopack` 1.2.161, and the `vpk` 1.2.161 command-line tool used
  only by the release workflow
- License: MIT, © 2021 Caelan Sayler, © 2024 Velopack Ltd.
- Source: https://github.com/velopack/velopack
- Used for the installer, uninstaller and updates (P22, P23). Ships with
  Naad.

## Whisper.net

- Packages: `Whisper.net` 1.9.1, `Whisper.net.Runtime` 1.9.1 and
  `Whisper.net.Runtime.Vulkan` 1.9.1, with their
  dependencies `Whisper.net.Runtime.Metal` 1.9.1 (macOS only, unused on
  Windows) and `Microsoft.Extensions.AI.Abstractions` 10.2.0
- License: MIT, © 2024 sandrohanea (Microsoft.Extensions.AI.Abstractions:
  MIT, © Microsoft Corporation)
- Source: https://github.com/sandrohanea/whisper.net
- The runtime packages ship native builds of whisper.cpp and ggml
  (MIT, © The ggml authors, https://github.com/ggml-org/whisper.cpp). The
  Vulkan build is compiled against the Khronos Vulkan headers (Apache-2.0
  or MIT, https://github.com/KhronosGroup/Vulkan-Headers) and uses the
  Vulkan loader installed with the graphics driver; Naad doesn't ship the
  loader.
- Used for transcription on this PC (P20). Ships with Naad.

## Whisper models (downloaded, not shipped)

- Files from https://huggingface.co/ggerganov/whisper.cpp:
  - `ggml-base.bin`, 147,951,465 bytes, SHA-256
    `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe`
  - `ggml-large-v3-turbo-q5_0.bin`, 574,041,195 bytes, SHA-256
    `394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2`
- License: MIT. OpenAI's Whisper weights (https://github.com/openai/whisper,
  © 2022 OpenAI), converted to the ggml format (and, for large-v3-turbo,
  quantized to 5 bits) by the whisper.cpp project.
- Naad doesn't include them. A model is downloaded only when the user
  clicks **Download model** in Settings, and kept only if its checksum
  matches.

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
