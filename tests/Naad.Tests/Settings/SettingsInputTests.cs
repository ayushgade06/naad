using Naad.Settings;
using Naad.Transcription;

namespace Naad.Tests.Settings;

public sealed class SettingsInputTests
{
    // Deliberately not shaped like a real provider key, so secret scanners stay quiet.
    private const string FakeKey = "fake-api-key-0123456789";

    [Fact]
    public void TryBuild_ValidFields_AppliesThemTrimmed()
    {
        var chosen = AppSettings.Defaults with { PlaySounds = false };

        var ok = SettingsInput.TryBuild(
            chosen, " https://stt.example.test/v1 ", " whisper-1 ", " HI ", "", out var settings, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(
            chosen with
            {
                TranscriptionBaseUrl = new Uri("https://stt.example.test/v1"),
                TranscriptionModel = "whisper-1",
                Language = "hi",
            },
            settings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryBuild_BlankLanguage_MeansDetect(string language)
    {
        var chosen = AppSettings.Defaults with { Language = "en" };

        Assert.True(SettingsInput.TryBuild(
            chosen, "https://api.groq.com/openai/v1", "whisper-large-v3-turbo", language, "", out var settings, out _));
        Assert.Null(settings.Language);
    }

    [Theory]
    [InlineData("http://stt.example.test/v1", "m", "", "base URL")]
    [InlineData("https://stt.example.test/v1?key=1", "m", "", "base URL")]
    [InlineData("", "m", "", "base URL")]
    [InlineData("https://stt.example.test/v1", "", "", "model")]
    [InlineData("https://stt.example.test/v1", "whisper 1", "", "model")]
    [InlineData("https://stt.example.test/v1", "m", "english", "language")]
    [InlineData("https://stt.example.test/v1", "m", "en-US", "language")]
    public void TryBuild_InvalidField_NamesIt(string baseUrl, string model, string language, string field)
    {
        var ok = SettingsInput.TryBuild(
            AppSettings.Defaults, baseUrl, model, language, "", out var settings, out var error);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_Vocabulary_IsTrimmedAndBlankMeansNone()
    {
        const string Url = "https://api.groq.com/openai/v1";

        Assert.True(SettingsInput.TryBuild(AppSettings.Defaults, Url, "m", "", " Naad, Groq ", out var some, out _));
        Assert.True(SettingsInput.TryBuild(some, Url, "m", "", "   ", out var none, out _));

        Assert.Equal("Naad, Groq", some.Vocabulary);
        Assert.Null(none.Vocabulary);
    }

    [Fact]
    public void TryBuild_VocabularyTooLong_NamesIt()
    {
        var ok = SettingsInput.TryBuild(
            AppSettings.Defaults,
            "https://api.groq.com/openai/v1",
            "m",
            "",
            new string('v', 501),
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("vocabulary", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(FakeKey)]
    [InlineData(" " + FakeKey + " ")]
    public void KeyError_BlankOrValid_IsNull(string typed)
    {
        Assert.Null(SettingsInput.KeyError(typed));
    }

    [Theory]
    [InlineData("fake key")]
    [InlineData("﻿" + FakeKey)]
    public void KeyError_Invalid_ExplainsWithoutRepeatingIt(string typed)
    {
        var error = SettingsInput.KeyError(typed);

        Assert.NotNull(error);
        Assert.DoesNotContain("fake", error, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyStatus_EveryOutcome_NeverShowsTheKey()
    {
        ApiKeyResult[] results =
        [
            new(FakeKey, ApiKeySource.Environment, SecretStatus.Loaded),
            new(FakeKey, ApiKeySource.Stored, SecretStatus.Loaded),
            new(null, ApiKeySource.None, SecretStatus.Missing),
            new(null, ApiKeySource.None, SecretStatus.Invalid),
            new(null, ApiKeySource.None, SecretStatus.Unreadable),
            new(null, ApiKeySource.None, SecretStatus.ReadFailed),
        ];

        var texts = results.Select(SettingsInput.KeyStatus).ToList();

        Assert.All(texts, text => Assert.DoesNotContain(FakeKey, text, StringComparison.Ordinal));
        Assert.Equal(5, texts.Distinct().Count());
        Assert.StartsWith("Using the " + SecretStore.OverrideVariable, texts[0], StringComparison.Ordinal);
        Assert.StartsWith("No key is stored.", texts[2], StringComparison.Ordinal);
        Assert.Equal(texts[4], texts[5]);
    }

    [Fact]
    public void KeyTestMessage_EveryOutcomeAndError_HasItsOwnText()
    {
        KeyTestResult[] results =
        [
            new(KeyTestOutcome.Accepted),
            new(KeyTestOutcome.CannotVerify),
            .. Enum.GetValues<TranscriptionError>().Select(e => new KeyTestResult(KeyTestOutcome.Failed, new(e, 400))),
        ];

        var texts = results.Select(SettingsInput.KeyTestMessage).ToList();

        Assert.Equal("The provider accepted the key.", texts[0]);
        Assert.StartsWith("This provider can't check keys.", texts[1], StringComparison.Ordinal);
        Assert.Contains(texts, text => text == "The provider rejected the key.");
        Assert.Contains(texts, text => text.Contains("HTTP 400", StringComparison.Ordinal));
        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
    }
}
