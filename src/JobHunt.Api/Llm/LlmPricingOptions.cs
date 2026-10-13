namespace JobHunt.Api.Llm;

// Paid-tier prices per million tokens, keyed by model id (Llm:Pricing:{model}:InputPerMTok). The free tier costs
// $0, so these only feed the "would cost" estimate. A model without an entry has no estimate.
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
