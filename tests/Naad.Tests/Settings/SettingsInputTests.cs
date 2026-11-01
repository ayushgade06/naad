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
        var chosen = AppSettings.Defaults with { PlaySounds = true };

        var ok = SettingsInput.TryBuild(chosen, " HI ", "", out var settings, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(chosen with { Language = "hi" }, settings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryBuild_BlankLanguage_MeansDetect(string language)
    {
        var chosen = AppSettings.Defaults with { Language = "en" };

        Assert.True(SettingsInput.TryBuild(chosen, language, "", out var settings, out _));
        Assert.Null(settings.Language);
    }

    [Theory]
    [InlineData("english")]
    [InlineData("en-US")]
    public void TryBuild_InvalidLanguage_NamesIt(string language)
    {
        var ok = SettingsInput.TryBuild(AppSettings.Defaults, language, "", out var settings, out var error);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.Contains("language", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_Vocabulary_IsTrimmedAndBlankMeansNone()
    {
        Assert.True(SettingsInput.TryBuild(AppSettings.Defaults, "", " Naad, Groq ", out var some, out _));
        Assert.True(SettingsInput.TryBuild(some, "", "   ", out var none, out _));

        Assert.Equal("Naad, Groq", some.Vocabulary);
        Assert.Null(none.Vocabulary);
    }

    [Fact]
    public void TryBuild_VocabularyTooLong_NamesIt()
    {
        var ok = SettingsInput.TryBuild(AppSettings.Defaults, "", new string('v', 501), out _, out var error);

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

        var texts = results.Select(r => SettingsInput.KeyStatus(r, "NAAD_GROQ_API_KEY")).ToList();

        Assert.All(texts, text => Assert.DoesNotContain(FakeKey, text, StringComparison.Ordinal));
        Assert.Equal(5, texts.Distinct().Count());
        Assert.StartsWith("Using the NAAD_GROQ_API_KEY", texts[0], StringComparison.Ordinal);
        Assert.StartsWith("No key is stored.", texts[2], StringComparison.Ordinal);
        Assert.Equal(texts[4], texts[5]);
    }

    [Theory]
    [InlineData(147_951_465, "148 MB")]
    [InlineData(574_041_195, "574 MB")]
    public void ModelStatus_NotDownloaded_NamesTheDownloadSize(long size, string expected)
    {
        Assert.Contains(expected, SettingsInput.ModelStatus(size, ready: false), StringComparison.Ordinal);
    }

    [Fact]
    public void ModelDownloadMessage_EveryOutcome_HasItsOwnText()
    {
        var texts = Enum.GetValues<ModelDownloadOutcome>()
            .Select(outcome => SettingsInput.ModelDownloadMessage(new(outcome, 503)))
            .ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.Contains("Downloaded", texts[0], StringComparison.Ordinal);
        Assert.Contains(texts, text => text.Contains("HTTP 503", StringComparison.Ordinal));
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

    [Theory]
    [InlineData(1_117_320_736, "1.1 GB")]
    [InlineData(2_497_281_120, "2.5 GB")]
    public void LanguageModelStatus_NotDownloaded_NamesTheSize(long size, string expected)
    {
        Assert.Contains(expected, SettingsInput.LanguageModelStatus(size, ready: false), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyStatus_Override_NamesItsVariable()
    {
        var text = SettingsInput.KeyStatus(
            new("fake-key", ApiKeySource.Environment, SecretStatus.Loaded), "NAAD_OPENAI_API_KEY");

        Assert.Contains("NAAD_OPENAI_API_KEY", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptTestMessage_EveryCase_HasItsOwnTextWithoutTheReply()
    {
        var sent = new Naad.LanguageModel.ChatPrompt("i", "t");
        Naad.Cleanup.PromptTest[] tests =
        [
            new("", null, 0, sent, null),
            new("", null, 5, sent, new(Naad.LanguageModel.CompletionError.Timeout)),
            new("secret reply", Naad.Cleanup.ReplyVerdict.Accepted, 5, sent, null),
            new("EMPTY", Naad.Cleanup.ReplyVerdict.Empty, 5, sent, null),
            new("Sure!", Naad.Cleanup.ReplyVerdict.Preamble, 5, sent, null),
        ];

        var texts = tests.Select(SettingsInput.PromptTestMessage).ToList();

        Assert.Equal(texts.Count, texts.Distinct().Count());
        Assert.DoesNotContain(texts, t => t.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void CooldownMessage_Daily_SaysWhenItResets()
    {
        var text = SettingsInput.CooldownMessage("m", new(DateTimeOffset.UtcNow.AddHours(3), Daily: true));

        Assert.StartsWith("Daily limit reached for m; it resets at", text, StringComparison.Ordinal);
        Assert.Null(SettingsInput.CooldownMessage("m", null));
    }

    [Theory]
    [InlineData("Accepted", null, null)]
    [InlineData("CannotVerify", null, null)]
    [InlineData("Failed", "Network", null)]
    [InlineData("Failed", "NoInternet", null)]
    [InlineData("Failed", "Timeout", null)]
    [InlineData("Failed", "Unauthorized", "rejected the key")]
    [InlineData("Failed", "Rejected", "Check the address")]
    [InlineData("Failed", "ServerError", null)]
    [InlineData("Failed", "MalformedResponse", "Check the address")]
    public void EndpointSaveError_Result_BlocksOnlyAWrongAddressOrKey(string outcome, string? error, string? expected)
    {
        var result = new Naad.Transcription.KeyTestResult(
            Enum.Parse<Naad.Transcription.KeyTestOutcome>(outcome),
            error is null ? null : new(Enum.Parse<Naad.Transcription.TranscriptionError>(error)));

        var message = SettingsInput.EndpointSaveError(result);

        if (expected is null)
        {
            Assert.Null(message);
        }
        else
        {
            Assert.Contains(expected, message, StringComparison.Ordinal);
        }
    }
}
