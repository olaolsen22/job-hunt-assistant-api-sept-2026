namespace JobHunt.Api.Llm;

// Paid-tier prices per million tokens by model id, for the "would cost" estimate. No entry = no estimate.
public class LlmPricingOptions : Dictionary<string, ModelPrice>
{
    public const string Section = "Llm:Pricing";
}

public class ModelPrice
{
    public decimal InputPerMTok { get; set; }
    public decimal OutputPerMTok { get; set; }
}

// Provider-qualified prices avoid merging identically named models from different providers.
public class ProviderLlmPricingOptions : Dictionary<string, Dictionary<string, ModelPrice>>
{
    public const string Section = "Llm:ProviderPricing";
}
