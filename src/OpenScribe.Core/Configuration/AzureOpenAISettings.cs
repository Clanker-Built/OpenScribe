namespace OpenScribe.Core.Configuration;

/// <summary>
/// Configuration for AI service connection (OpenAI or Azure OpenAI).
/// Maps to the "AzureOpenAI" section in appsettings.json.
/// </summary>
public class AzureOpenAISettings
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>AI provider: "OpenAI" for api.openai.com, "AzureOpenAI" for Azure endpoints.</summary>
    public string Provider { get; set; } = "AzureOpenAI";

    /// <summary>Azure OpenAI endpoint URL (e.g., https://myorg.openai.azure.com/). Not needed for OpenAI provider.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Model or deployment name (e.g., "gpt-5.2" for OpenAI, or your deployment name for Azure).</summary>
    public string DeploymentName { get; set; } = "gpt-5.2";

    /// <summary>API key for OpenAI or Azure OpenAI.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Whether to use Entra ID (DefaultAzureCredential) for Azure OpenAI auth.</summary>
    public bool UseEntraIdAuth { get; set; } = true;

    /// <summary>Max tokens for step analysis responses.</summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>Temperature for generation (0 = deterministic, 1 = creative).</summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>Search model for web-enabled research (e.g., "gpt-4o-search-preview"). Only used with OpenAI provider.</summary>
    public string SearchModelName { get; set; } = "gpt-4o-search-preview";
}
