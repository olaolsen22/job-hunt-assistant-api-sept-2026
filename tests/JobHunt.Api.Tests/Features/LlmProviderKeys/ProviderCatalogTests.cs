using System.ComponentModel.DataAnnotations;
using JobHunt.Api.Llm;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class ProviderCatalogTests
{
    private static ProviderModelOption Model(string id = "model") => new()
    { Id = id, Label = "Model", MaxOutputTokens = 32768, ContextWindowTokens = 200000 };
    private static ProviderCatalogOptions Catalog() => new() { OpenAI = [Model()], Mistral = [Model()] };

    [Fact]
    public void Catalog_RejectsMalformedDuplicatedAndInadequateModels()
    {
        var valid = Catalog();
        Assert.Empty(valid.Validate(new ValidationContext(valid)));
        var catalogs = new[] { Catalog(), Catalog(), Catalog(), Catalog(), Catalog() };
        catalogs[0].OpenAI = [];
        catalogs[1].OpenAI.Add(Model());
        catalogs[2].Mistral[0].Label = " ";
        catalogs[3].OpenAI[0].MaxOutputTokens = 8192;
        catalogs[4].OpenAI[0].ContextWindowTokens = 32000;
        foreach (var invalid in catalogs) Assert.NotEmpty(invalid.Validate(new ValidationContext(invalid)));
    }

    [Fact]
    public void SelectionIds_SeparateProviders_AndRejectLegacyGeminiCollisions()
    {
        var catalog = Catalog();
        Assert.True(catalog.HasUniqueSelectionIds(new() { Models = [new() { Id = "model" }] }));
        Assert.False(catalog.HasUniqueSelectionIds(new() { Models = [new() { Id = "openai:model" }] }));
    }
}
