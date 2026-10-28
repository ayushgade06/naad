using Naad.Settings;

namespace Naad.Tests;

public sealed class StartupErrorTests
{
    // Deliberately not shaped like a real provider key, so secret scanners stay quiet.
    private const string FakeKey = "fake-api-key-0123456789";

    [Fact]
    public void Describe_MessageContainsSecret_RedactsEveryOccurrence()
    {
        var error = new InvalidOperationException($"bad header 'Bearer {FakeKey}' ({FakeKey})");

        var text = StartupError.Describe(error, FakeKey);

        Assert.DoesNotContain(FakeKey, text, StringComparison.Ordinal);
        Assert.Equal("InvalidOperationException: bad header 'Bearer [redacted]' ([redacted])", text);
    }

    [Fact]
    public void Describe_NoSecretInMessage_KeepsTypeAndMessage()
    {
        var text = StartupError.Describe(new System.ComponentModel.Win32Exception(5), FakeKey);

        Assert.StartsWith("Win32Exception: ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_NoSecret_ReturnsTextUnchanged(string? secret)
    {
        Assert.Equal("unchanged", StartupError.Redact("unchanged", secret));
    }

    [Fact]
    public void ApiKeyProblem_EachStatus_SaysHowToStoreAKey()
    {
        var missing = StartupError.ApiKeyProblem(SecretStatus.Missing);
        var unreadable = StartupError.ApiKeyProblem(SecretStatus.Unreadable);

        Assert.StartsWith("No API key is stored.", missing, StringComparison.Ordinal);
        Assert.StartsWith("The stored API key can't be read.", unreadable, StringComparison.Ordinal);
        Assert.Equal(unreadable, StartupError.ApiKeyProblem(SecretStatus.ReadFailed));
        Assert.All([missing, unreadable], text => Assert.Contains("--set-api-key", text, StringComparison.Ordinal));
    }

    [Fact]
    public void ApiKeyProblem_InvalidOverride_NamesTheVariable() =>
        Assert.StartsWith(
            SecretStore.OverrideVariable, StartupError.ApiKeyProblem(SecretStatus.Invalid), StringComparison.Ordinal);
}
