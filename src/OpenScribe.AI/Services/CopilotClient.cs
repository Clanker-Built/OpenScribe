using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenScribe.AI.Prompts;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.AI.Services;

/// <summary>
/// AI client that sends screenshots and metadata to GPT for vision-based
/// step-by-step analysis and document planning.
/// Supports both OpenAI (api.openai.com) and Azure OpenAI endpoints.
/// </summary>
public class CopilotClient : ICopilotClient
{
    private readonly ILogger<CopilotClient> _logger;
    private readonly AzureOpenAISettings _settings;
    private ChatClient? _chatClient;
    private ChatClient? _searchChatClient;
    private readonly object _initLock = new();
    private readonly object _searchInitLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CopilotClient(
        ILogger<CopilotClient> logger,
        IOptions<AzureOpenAISettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    private ChatClient GetChatClient()
    {
        if (_chatClient is not null)
            return _chatClient;

        lock (_initLock)
        {
            if (_chatClient is not null)
                return _chatClient;

            var isOpenAI = _settings.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);

            if (isOpenAI)
            {
                // Standard OpenAI API (api.openai.com)
                if (string.IsNullOrEmpty(_settings.ApiKey))
                    throw new InvalidOperationException(
                        "OpenAI API key is required. Set ApiKey in Settings.");

                var client = new OpenAIClient(new ApiKeyCredential(_settings.ApiKey));
                _chatClient = client.GetChatClient(_settings.DeploymentName);
                _logger.LogInformation("CopilotClient initialized with OpenAI: model={Model}",
                    _settings.DeploymentName);
            }
            else
            {
                // Azure OpenAI
                AzureOpenAIClient azureClient;

                if (_settings.UseEntraIdAuth)
                {
                    azureClient = new AzureOpenAIClient(
                        new Uri(_settings.Endpoint),
                        new DefaultAzureCredential());
                }
                else if (!string.IsNullOrEmpty(_settings.ApiKey))
                {
                    azureClient = new AzureOpenAIClient(
                        new Uri(_settings.Endpoint),
                        new ApiKeyCredential(_settings.ApiKey));
                }
                else
                {
                    throw new InvalidOperationException(
                        "Azure OpenAI authentication not configured. Set either UseEntraIdAuth=true or provide an ApiKey.");
                }

                _chatClient = azureClient.GetChatClient(_settings.DeploymentName);
                _logger.LogInformation("CopilotClient initialized with Azure OpenAI: {Endpoint} / {Deployment}",
                    _settings.Endpoint, _settings.DeploymentName);
            }

            return _chatClient;
        }
    }

    /// <summary>
    /// Gets a ChatClient for the web search model (OpenAI provider only).
    /// Returns null if search is not available (Azure provider or no search model configured).
    /// </summary>
    private ChatClient? GetSearchChatClient()
    {
        var isOpenAI = _settings.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);
        if (!isOpenAI || string.IsNullOrWhiteSpace(_settings.SearchModelName))
            return null;

        if (_searchChatClient is not null)
            return _searchChatClient;

        lock (_searchInitLock)
        {
            if (_searchChatClient is not null)
                return _searchChatClient;

            if (string.IsNullOrEmpty(_settings.ApiKey))
                return null;

            var client = new OpenAIClient(new ApiKeyCredential(_settings.ApiKey));
            _searchChatClient = client.GetChatClient(_settings.SearchModelName);
            _logger.LogInformation("Search ChatClient initialized: model={Model}", _settings.SearchModelName);
            return _searchChatClient;
        }
    }

    public async Task<string?> ResearchProcessAsync(string processName, CancellationToken ct = default)
    {
        _logger.LogInformation("Researching process context for: {ProcessName}", processName);

        // Try web search first if available
        var searchClient = GetSearchChatClient();
        if (searchClient is not null)
        {
            try
            {
                var searchPrompt = PromptTemplates.ProcessResearchWebSearchPrompt
                    .Replace("{processName}", processName);

                // Search models don't support system messages — include context in the user prompt
                var searchMessages = new List<ChatMessage>
                {
                    new UserChatMessage($"{PromptTemplates.SystemPrompt}\n\n{searchPrompt}")
                };

                var searchOptions = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 800,
                    Temperature = 0.2f,
                    WebSearchOptions = new ChatWebSearchOptions()
                };

                var response = await searchClient.CompleteChatAsync(searchMessages, searchOptions, ct);
                var result = response.Value.Content[0].Text;
                _logger.LogInformation("Process research completed via web search ({Length} chars)", result.Length);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Web search research failed — falling back to standard model");
            }
        }

        // Fallback: use the regular chat client without web search
        var prompt = PromptTemplates.ProcessResearchPrompt
            .Replace("{processName}", processName);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(PromptTemplates.SystemPrompt),
            new UserChatMessage(prompt)
        };

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 800,
            Temperature = 0.2f
        };

        try
        {
            var response = await GetChatClient().CompleteChatAsync(messages, options, ct);
            var result = response.Value.Content[0].Text;
            _logger.LogInformation("Process research completed ({Length} chars)", result.Length);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Process research failed — continuing without context");
            return null;
        }
    }

    public async Task<AnalyzedStep> AnalyzeStepAsync(
        RawStep step,
        AnalyzedStep? previousStep,
        string? processContext = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Analyzing step {Seq} with AI vision", step.SequenceNumber);

        // Build the prompt with placeholders filled in
        var prompt = PromptTemplates.StepAnalysisPrompt
            .Replace("{processContext}", processContext ?? "(no additional context)")
            .Replace("{stepNumber}", step.SequenceNumber.ToString())
            .Replace("{clickX}", step.Click.X.ToString())
            .Replace("{clickY}", step.Click.Y.ToString())
            .Replace("{clickType}", step.Click.ClickType.ToString())
            .Replace("{windowTitle}", step.Click.WindowTitle)
            .Replace("{applicationName}", step.Click.ApplicationName ?? "Unknown")
            .Replace("{imageWidth}", step.ScreenshotWidth > 0 ? step.ScreenshotWidth.ToString() : "unknown")
            .Replace("{imageHeight}", step.ScreenshotHeight > 0 ? step.ScreenshotHeight.ToString() : "unknown")
            .Replace("{uiaControlType}", step.Click.UiaControlType ?? "Unknown")
            .Replace("{uiaElementName}", step.Click.UiaElementName ?? "Unknown")
            .Replace("{uiaAutomationId}", step.Click.UiaAutomationId ?? "Unknown")
            .Replace("{uiaClassName}", step.Click.UiaClassName ?? "Unknown")
            .Replace("{uiaElementBounds}", step.Click.UiaElementBounds ?? "Unknown")
            .Replace("{ocrText}", step.OcrText ?? "(none)")
            .Replace("{typedText}", step.TypedText ?? "(none)")
            .Replace("{voiceTranscript}", step.VoiceTranscript ?? "(none)")
            .Replace("{previousStepContext}", previousStep?.Instruction ?? "(this is the first step)");

        // Build message content with the screenshot image
        var contentParts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(prompt)
        };

        // Always send the full screenshot so AI sees the complete UI context
        var imagePath = step.ScreenshotPath;
        if (File.Exists(imagePath))
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
            var imageData = BinaryData.FromBytes(imageBytes);
            contentParts.Add(ChatMessageContentPart.CreateImagePart(imageData, "image/png"));
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(PromptTemplates.SystemPrompt),
            new UserChatMessage(contentParts)
        };

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _settings.MaxTokens,
            Temperature = _settings.Temperature
        };

        try
        {
            var response = await GetChatClient().CompleteChatAsync(messages, options, ct);
            var responseText = response.Value.Content[0].Text;

            // Parse JSON from the response (strip markdown code fences if present)
            var json = ExtractJson(responseText);
            var result = JsonSerializer.Deserialize<AnalyzedStep>(json, JsonOptions);

            if (result is null)
                throw new InvalidOperationException("AI returned null analysis result");

            _logger.LogDebug("Step {Seq} analyzed: {Title}", step.SequenceNumber, result.StepTitle);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to analyze step {Seq}", step.SequenceNumber);

            // Return a fallback step
            return new AnalyzedStep
            {
                StepTitle = $"Step {step.SequenceNumber}",
                Instruction = $"Click at position ({step.Click.X}, {step.Click.Y}) in {step.Click.WindowTitle}.",
                Notes = $"AI analysis failed: {ex.Message}"
            };
        }
    }

    public async Task<DocumentPlan> CreateDocumentPlanAsync(
        CaptureSession session,
        IReadOnlyList<AnalyzedStep> steps,
        IReadOnlyList<RawStep> rawSteps,
        string? processContext = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Creating document plan for session {Id} with {Count} steps",
            session.Id, steps.Count);

        // Build JSON summary of all steps for the editorial prompt
        var stepsSummary = steps.Select((s, i) => new
        {
            step = i + 1,
            title = s.StepTitle,
            instruction = s.Instruction,
            notes = s.Notes
        });

        // Build voice transcripts array for the editorial prompt
        var voiceTranscripts = rawSteps.Select((r, i) => new
        {
            step = i + 1,
            transcript = r.VoiceTranscript ?? "(no narration)"
        });

        var stepsJson = JsonSerializer.Serialize(stepsSummary, JsonOptions);
        var voiceTranscriptsJson = JsonSerializer.Serialize(voiceTranscripts, JsonOptions);
        var prompt = PromptTemplates.DocumentPlanPrompt
            .Replace("{processContext}", processContext ?? "(no additional context)")
            .Replace("{stepsJson}", stepsJson)
            .Replace("{voiceTranscriptsJson}", voiceTranscriptsJson);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(PromptTemplates.SystemPrompt),
            new UserChatMessage(prompt)
        };

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _settings.MaxTokens * 2, // Larger for full document plan
            Temperature = _settings.Temperature
        };

        try
        {
            var response = await GetChatClient().CompleteChatAsync(messages, options, ct);
            var responseText = response.Value.Content[0].Text;
            var json = ExtractJson(responseText);

            var plan = JsonSerializer.Deserialize<DocumentPlan>(json, JsonOptions);
            if (plan is null)
                throw new InvalidOperationException("AI returned null document plan");

            _logger.LogInformation("Document plan created: {Title}", plan.Title);
            return plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to create document plan");

            return new DocumentPlan
            {
                Title = session.Name,
                Introduction = $"This document describes the process: {session.Name}.",
                Summary = "Follow the steps above to complete the process.",
                RevisedInstructions = steps.Select(s => s.Instruction).ToList()
            };
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new UserChatMessage("Reply with exactly: OK")
        };

        var response = await GetChatClient().CompleteChatAsync(messages, cancellationToken: ct);
        return response.Value.Content.Count > 0;
    }

    /// <summary>
    /// Extract JSON from a response that may have markdown code fences.
    /// </summary>
    private static string ExtractJson(string text)
    {
        text = text.Trim();

        // Remove ```json ... ``` wrapper
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];

            if (text.EndsWith("```"))
                text = text[..^3];
        }

        return text.Trim();
    }
}
