namespace OpenScribe.Core.Configuration;

/// <summary>
/// Configuration for Azure AI Speech service (voice transcription).
/// Maps to the "AzureSpeech" section in appsettings.json.
/// </summary>
public class AzureSpeechSettings
{
    public const string SectionName = "AzureSpeech";

    /// <summary>Azure Speech service subscription key.</summary>
    public string SubscriptionKey { get; set; } = string.Empty;

    /// <summary>Azure region (e.g., "eastus").</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Language for speech recognition (e.g., "en-US").</summary>
    public string Language { get; set; } = "en-US";
}
