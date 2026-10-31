using Naad.Cleanup;
using Naad.LanguageModel;
using Naad.Settings;

namespace Naad.Tests.Settings;

// The language model and cleanup settings; SettingsStoreTests covers the rest.
public sealed class SettingsStoreAiTests
{
    [Fact]
    public void Parse_LanguageModelSettings_ReturnsThem()
    {
        var result = SettingsStore.Parse("""
            { "version": 2, "languageModelProvider": "cloud", "localLanguageModel": "Large",
              "languageModelBaseUrl": "http://localhost:11434/v1", "languageModelName": "qwen2.5:1.5b",
              "languageModelFallbackName": "llama-3.3-70b-versatile" }
            """);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(LanguageModelProvider.Cloud, result.Settings.LanguageModelProvider);
        Assert.Equal(LocalLanguageModelChoice.Large, result.Settings.LocalLanguageModel);
        Assert.Equal(new Uri("http://localhost:11434/v1"), result.Settings.LanguageModelBaseUrl);
        Assert.Equal("qwen2.5:1.5b", result.Settings.LanguageModelName);
        Assert.Equal("llama-3.3-70b-versatile", result.Settings.LanguageModelFallbackName);
    }

    [Fact]
    public void Parse_CleanupSettings_ReturnsThem()
    {
        var result = SettingsStore.Parse("""
            { "version": 2, "cleanup": "basic", "cleanupPrompt": "Tidy it.\nKeep it short.",
              "cleanupPromptVersion": "2026-10-01", "instructionGuard": false, "outputLanguage": "Portuguese (Brazil)" }
            """);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(CleanupLevel.Basic, result.Settings.Cleanup);
        Assert.Equal("Tidy it.\nKeep it short.", result.Settings.CleanupPrompt);
        Assert.Equal("2026-10-01", result.Settings.CleanupPromptVersion);
        Assert.False(result.Settings.InstructionGuard);
        Assert.Equal("Portuguese (Brazil)", result.Settings.OutputLanguage);
    }

    [Fact]
    public void Parse_InvalidAiSettings_FallBackToDefaultsAsRepaired()
    {
        var result = SettingsStore.Parse("""
            { "version": 2, "languageModelProvider": "Elsewhere", "localLanguageModel": 1,
              "languageModelBaseUrl": "http://llm.example.test/v1", "languageModelName": "two words",
              "languageModelFallbackName": "", "cleanup": "Max", "cleanupPrompt": "bell\u0007",
              "cleanupPromptVersion": "yesterday", "instructionGuard": "yes", "outputLanguage": "<b>" }
            """);

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(AppSettings.Defaults, result.Settings);
    }
}
