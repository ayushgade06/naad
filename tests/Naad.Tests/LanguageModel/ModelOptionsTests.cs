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
    [InlineData("openai/gpt-5-mini", true)]
    [InlineData("openai/o4-mini", true)]
    [InlineData("gpt-4o-mini", false)]
    [InlineData("openai/gpt-oss-20b", false)]
    [InlineData("llama3.2", false)]
    public void UsesMaxCompletionTokens_Model_OnlyForOpenAiReasoningModels(string model, bool expected)
    {
        Assert.Equal(expected, ModelOptions.UsesMaxCompletionTokens(model));
    }

    [Theory]
    [InlineData("claude-haiku-4-5", false)]
    [InlineData("o3-mini", true)]
    [InlineData("gpt-5-nano", true)]
    [InlineData("openai/gpt-5-mini", true)]
    [InlineData("gpt-4o-mini", false)]
    [InlineData("llama-3.1-8b-instant", false)]
    [InlineData("openai/gpt-oss-20b", false)]
    public void OmitsTemperature_Model_ForModelsKnownToRefuseIt(string model, bool expected) =>
        Assert.Equal(expected, ModelOptions.OmitsTemperature(model));

    [Theory]
    [InlineData("https://api.groq.com/openai/v1", "openai/gpt-oss-20b", true)]
    [InlineData("https://api.groq.com/openai/v1", "qwen/qwen3-32b", true)]
    [InlineData("https://openrouter.ai/api/v1", "openai/gpt-oss-120b", true)]
    [InlineData("https://api.groq.com/openai/v1", "llama-3.1-8b-instant", false)]
    [InlineData("https://api.cerebras.ai/v1", "gpt-oss-120b", false)]
    [InlineData("http://localhost:11434/v1", "qwen3:4b", false)]
    public void SendsIncludeReasoning_ServiceAndModel_OnlyWhereTheServiceTakesIt(
        string baseUrl, string model, bool expected) =>
        Assert.Equal(expected, ModelOptions.SendsIncludeReasoning(new(baseUrl), model));

    [Theory]
    [InlineData("llama-3.1-8b-instant", 1024, 1024)]
    [InlineData("openai/gpt-oss-20b", 1024, 4096)]
    [InlineData("o4-mini", 200, 4096)]
    [InlineData("gpt-5-mini", 2048, 4096)]
    [InlineData("gpt-5-mini", 8192, 8192)]
    public void MaxTokens_Model_LeavesReasoningModelsRoomToThink(string model, int wanted, int expected) =>
        Assert.Equal(expected, ModelOptions.MaxTokens(model, wanted));
}
