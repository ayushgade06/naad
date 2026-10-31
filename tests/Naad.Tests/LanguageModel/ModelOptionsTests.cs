using Naad.LanguageModel;

namespace Naad.Tests.LanguageModel;

public sealed class ModelOptionsTests
{
    [Theory]
    [InlineData("openai/gpt-oss-20b", "low")]
    [InlineData("gpt-oss-120b", "low")]
    [InlineData("qwen/qwen3-32b", "none")]
    [InlineData("gpt-5-mini", "minimal")]
    [InlineData("openai/gpt-5-nano", "minimal")]
    [InlineData("o4-mini", "low")]
    [InlineData("grok-3-mini", "low")]
    [InlineData("llama-3.1-8b-instant", null)]
    [InlineData("gpt-4o-mini", null)]
    [InlineData("qwen2.5:1.5b", null)]
    public void ReasoningEffort_Model_MatchesWhatTheModelAccepts(string model, string? expected)
    {
        Assert.Equal(expected, ModelOptions.ReasoningEffort(model));
    }

    [Theory]
    [InlineData("gpt-5-mini", true)]
    [InlineData("o3", true)]
    [InlineData("gpt-4o-mini", false)]
    [InlineData("openai/gpt-oss-20b", false)]
    [InlineData("llama3.2", false)]
    public void UsesMaxCompletionTokens_Model_OnlyForOpenAiReasoningModels(string model, bool expected)
    {
        Assert.Equal(expected, ModelOptions.UsesMaxCompletionTokens(model));
    }
}
