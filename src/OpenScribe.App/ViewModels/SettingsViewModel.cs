using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Interfaces;

namespace OpenScribe.App.ViewModels;

/// <summary>
/// ViewModel for the Settings screen.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SettingsViewModel> _logger;

    // AI Provider
    [ObservableProperty] private int _selectedProviderIndex;
    [ObservableProperty] private string _azureEndpoint = string.Empty;
    [ObservableProperty] private string _deploymentName = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private bool _useEntraIdAuth = true;

    public string[] ProviderOptions { get; } = ["OpenAI (api.openai.com)", "Azure OpenAI"];

    partial void OnSelectedProviderIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOpenAI));
        OnPropertyChanged(nameof(IsAzureOpenAI));
    }

    public bool IsOpenAI => SelectedProviderIndex == 0;
    public bool IsAzureOpenAI => SelectedProviderIndex == 1;

    // Azure Speech
    [ObservableProperty] private string _speechKey = string.Empty;
    [ObservableProperty] private string _speechRegion = string.Empty;
    [ObservableProperty] private string _speechLanguage = "en-US";

    // General
    [ObservableProperty] private string _organizationName = string.Empty;
    [ObservableProperty] private string _defaultAuthor = string.Empty;
    [ObservableProperty] private double _cropRegionSize = 400;
    [ObservableProperty] private double _clickDebounceMs = 300;
    [ObservableProperty] private bool _recordAudioByDefault = true;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isTestingConnection;

    public string AppVersion { get; } = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

    public SettingsViewModel(
        IOptions<AzureOpenAISettings> aiSettings,
        IOptions<AzureSpeechSettings> speechSettings,
        IOptions<OpenScribeSettings> appSettings,
        IServiceProvider serviceProvider,
        ILogger<SettingsViewModel> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Load current values
        var ai = aiSettings.Value;
        SelectedProviderIndex = ai.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        AzureEndpoint = ai.Endpoint;
        DeploymentName = ai.DeploymentName;
        ApiKey = ai.ApiKey ?? string.Empty;
        UseEntraIdAuth = ai.UseEntraIdAuth;

        var speech = speechSettings.Value;
        SpeechKey = speech.SubscriptionKey;
        SpeechRegion = speech.Region;
        SpeechLanguage = speech.Language;

        var app = appSettings.Value;
        OrganizationName = app.OrganizationName ?? string.Empty;
        DefaultAuthor = app.DefaultAuthor ?? string.Empty;
        CropRegionSize = app.CropRegionSize;
        ClickDebounceMs = app.ClickDebounceMs;
        RecordAudioByDefault = app.RecordAudioByDefault;
    }

    [RelayCommand]
    public async Task TestAzureConnectionAsync()
    {
        try
        {
            IsTestingConnection = true;
            StatusMessage = "Testing connection...";

            var copilotClient = _serviceProvider.GetRequiredService<ICopilotClient>();
            var success = await copilotClient.TestConnectionAsync();
            StatusMessage = success
                ? "Connection successful!"
                : "Connection failed. Check your settings.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        try
        {
            // Save to user settings in LocalAppData (safe from Defender restrictions)
            var settingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenScribe");
            Directory.CreateDirectory(settingsDir);
            var settingsPath = Path.Combine(settingsDir, "usersettings.json");

            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();

            // AzureOpenAI section
            writer.WriteStartObject("AzureOpenAI");
            writer.WriteString("Provider", SelectedProviderIndex == 0 ? "OpenAI" : "AzureOpenAI");
            writer.WriteString("Endpoint", AzureEndpoint);
            writer.WriteString("DeploymentName", DeploymentName);
            writer.WriteString("ApiKey", ApiKey);
            writer.WriteBoolean("UseEntraIdAuth", UseEntraIdAuth);
            writer.WriteEndObject();

            // AzureSpeech section
            writer.WriteStartObject("AzureSpeech");
            writer.WriteString("SubscriptionKey", SpeechKey);
            writer.WriteString("Region", SpeechRegion);
            writer.WriteString("Language", SpeechLanguage);
            writer.WriteEndObject();

            // OpenScribe section
            writer.WriteStartObject("OpenScribe");
            writer.WriteString("OrganizationName", OrganizationName);
            writer.WriteString("DefaultAuthor", DefaultAuthor);
            writer.WriteNumber("CropRegionSize", (int)CropRegionSize);
            writer.WriteNumber("ClickDebounceMs", (int)ClickDebounceMs);
            writer.WriteBoolean("RecordAudioByDefault", RecordAudioByDefault);
            writer.WriteEndObject();

            writer.WriteEndObject();
            await writer.FlushAsync();

            await File.WriteAllBytesAsync(settingsPath, ms.ToArray());

            StatusMessage = $"Settings saved to {settingsPath}. Restart the app for changes to take effect.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}
