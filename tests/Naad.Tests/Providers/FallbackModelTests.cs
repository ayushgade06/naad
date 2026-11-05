using Naad.Providers;

namespace Naad.Tests.Providers;

public sealed class FallbackModelTests
{
    [Fact]
    public void FallbackModel_DefaultModel_RetriesOnTheSuggestedSecond()
    {
        var models = CloudServices.For(CloudService.Groq).LanguageModels;

        Assert.Equal(models[1], CloudServices.FallbackModel(CloudService.Groq, models[0], chosen: null));
        Assert.Equal(models[0], CloudServices.FallbackModel(CloudService.Groq, models[1], chosen: null));
    }

    [Fact]
    public void FallbackModel_Chosen_WinsUnlessItIsTheMainModel()
    {
        Assert.Equal("synthetic-b", CloudServices.FallbackModel(CloudService.Groq, "synthetic-a", "synthetic-b"));
        Assert.Null(CloudServices.FallbackModel(CloudService.Groq, "synthetic-a", "synthetic-a"));
    }

    [Fact]
    public void FallbackModel_OtherModelOrCustom_HasNone()
    {
        Assert.Null(CloudServices.FallbackModel(CloudService.Groq, "synthetic-own", chosen: null));
        Assert.Null(CloudServices.FallbackModel(CloudService.Custom, "synthetic-own", chosen: null));
    }
}
