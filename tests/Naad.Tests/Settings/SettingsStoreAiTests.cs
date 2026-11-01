using Naad.Cleanup;
using Naad.LanguageModel;
using Naad.Providers;
using Naad.Settings;

namespace Naad.Tests.Settings;

// The language model and cleanup settings; SettingsStoreTests covers the rest.
public sealed class SettingsStoreAiTests
{
    [Fact]
    public void Parse_LanguageModelSettings_ReturnsThem()
    {
        var result = SettingsStore.Parse("""
            { "version": 4, "languageModelProvider": "cloud", "localLanguageModel": "Large",
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
            { "version": 4, "cleanup": "basic", "cleanupPrompt": "Tidy it.\nKeep it short.",
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
            { "version": 4, "languageModelProvider": "Elsewhere", "localLanguageModel": 1,
              "languageModelBaseUrl": "http://llm.example.test/v1", "languageModelName": "two words",
              "languageModelFallbackName": "", "cleanup": "Max", "cleanupPrompt": "bell\u0007",
              "cleanupPromptVersion": "yesterday", "instructionGuard": "yes", "outputLanguage": "<b>" }
            """);

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(AppSettings.Defaults, result.Settings);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "OpenAI")]
    [InlineData("https://api.groq.com/openai/v1", "Groq")]
    [InlineData("http://localhost:8000/v1", "Custom")]
    public void Parse_Version2_PicksTheServiceFromTheBaseUrl(string url, string service)
    {
        var result = SettingsStore.Parse($$"""
            { "version": 2, "transcriptionBaseUrl": "{{url}}", "languageModelBaseUrl": "{{url}}" }
            """);

        Assert.Equal(SettingsStatus.Migrated, result.Status);
        Assert.Equal(service, result.Settings.TranscriptionService.ToString());
        Assert.Equal(service, result.Settings.LanguageModelService.ToString());
        Assert.Equal(new Uri(url), result.Settings.TranscriptionBaseUrl);
    }

    [Fact]
    public void Parse_Version3Vocabulary_BecomesTheDictionary()
    {
        var result = SettingsStore.Parse("""{ "version": 3, "vocabulary": "Naad, Groq;\nnaad, Priya" }""");

        Assert.Equal(SettingsStatus.Migrated, result.Status);
        Assert.Equal(["Naad", "Groq", "Priya"], result.Settings.Words);
        Assert.Equal("Naad, Groq, Priya", result.Settings.Vocabulary);
    }

    [Fact]
    public void Parse_Version3LongVocabularyWithoutCommas_KeepsEveryWord()
    {
        var words = string.Join(' ', Enumerable.Range(0, 30).Select(i => $"Word{i}"));

        var result = SettingsStore.Parse($$"""{ "version": 3, "vocabulary": "{{words}}" }""");

        Assert.Equal(30, result.Settings.Words.Count);
    }

    [Fact]
    public void Parse_NewFeatureSettings_ReturnThem()
    {
        var result = SettingsStore.Parse("""
            { "version": 4, "historyRetention": "Week", "pressEnterCommand": true,
              "pasteAgainShortcut": "RightAlt+V", "overlayTimer": true, "context": false,
              "contextExcludedApps": ["keepass"], "contextScreenshot": true, "screenshotSize": 640,
              "screenshotWholeDesktop": true, "editMode": "Manual" }
            """);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        var s = result.Settings;
        Assert.Equal(Naad.History.HistoryRetention.Week, s.HistoryRetention);
        Assert.True(s.PressEnterCommand && s.OverlayTimer && s.ContextScreenshot && s.ScreenshotWholeDesktop);
        Assert.False(s.Context);
        Assert.Equal("Right Alt+V", s.PasteAgainShortcut?.Describe());
        Assert.Equal(["keepass"], s.ContextExcludedApps);
        Assert.Equal(640, s.ScreenshotSize);
        Assert.Equal(Naad.Context.EditMode.Manual, s.EditMode);
    }

    [Fact]
    public void Parse_Version2WithoutUrls_UsesGroq()
    {
        var result = SettingsStore.Parse("""{ "version": 2 }""");

        Assert.Equal((CloudService.Groq, CloudService.Groq),
            (result.Settings.TranscriptionService, result.Settings.LanguageModelService));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("301")]
    [InlineData("\"20\"")]
    public void Parse_InvalidTimeout_FallsBackAsRepaired(string value)
    {
        var result = SettingsStore.Parse($$"""{ "version": 4, "transcriptionTimeoutSeconds": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(20, result.Settings.TranscriptionTimeoutSeconds);
    }
}
