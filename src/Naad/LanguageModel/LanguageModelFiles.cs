using System.Diagnostics;
using Naad.Transcription;

namespace Naad.LanguageModel;

/// <summary>The language models Naad can run on this PC (see <see cref="LanguageModelFiles"/>).</summary>
internal enum LocalLanguageModelChoice
{
    Small,
    Large,
}

/// <summary>
/// The downloads behind each <see cref="LocalLanguageModelChoice"/>. Both are Apache-2.0, 4-bit
/// GGUF files; sizes and SHA-256 are those Hugging Face publishes.
/// </summary>
internal static class LanguageModelFiles
{
    /// <summary>
    /// Qwen2.5-1.5B-Instruct, Qwen's own 4-bit conversion: 1.1 GB. On the maintainer's laptop a
    /// cleanup took about 0.6 s on its graphics card and 2 s on its processor, so it suits any PC.
    /// </summary>
    public static LocalModel Small { get; } = LocalModel.InModelsFolder(
        "qwen2.5-1.5b-instruct-q4_k_m.gguf",
        new Uri("https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/"
            + "qwen2.5-1.5b-instruct-q4_k_m.gguf"),
        "6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e",
        1_117_320_736);

    /// <summary>
    /// Qwen3-4B-Instruct-2507 in Unsloth's 4-bit conversion: 2.5 GB, and noticeably better at
    /// keeping only the speaker's final wording. Offered for PCs with a graphics card.
    /// </summary>
    public static LocalModel Large { get; } = LocalModel.InModelsFolder(
        "Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
        new Uri("https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/main/"
            + "Qwen3-4B-Instruct-2507-Q4_K_M.gguf"),
        "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597",
        2_497_281_120);

    public static LocalModel For(LocalLanguageModelChoice choice) => choice switch
    {
        LocalLanguageModelChoice.Small => Small,
        LocalLanguageModelChoice.Large => Large,
        _ => throw new UnreachableException(),
    };
}
