using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Interfaces;

namespace OpenScribe.Processing.Services;

/// <summary>
/// Transcribes audio using Azure AI Speech Service.
/// Returns timestamped transcript segments.
/// </summary>
public class AzureSpeechTranscriptionService : ITranscriptionService
{
    private readonly ILogger<AzureSpeechTranscriptionService> _logger;
    private readonly AzureSpeechSettings _settings;

    public AzureSpeechTranscriptionService(
        ILogger<AzureSpeechTranscriptionService> logger,
        IOptions<AzureSpeechSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_settings.SubscriptionKey) || string.IsNullOrEmpty(_settings.Region))
        {
            _logger.LogWarning("Azure Speech settings not configured. Skipping transcription.");
            return [];
        }

        var segments = new List<TranscriptSegment>();

        try
        {
            var speechConfig = SpeechConfig.FromSubscription(_settings.SubscriptionKey, _settings.Region);
            speechConfig.SpeechRecognitionLanguage = _settings.Language;
            speechConfig.RequestWordLevelTimestamps();

            using var audioConfig = AudioConfig.FromWavFileInput(audioFilePath);
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            var tcs = new TaskCompletionSource<bool>();

            recognizer.Recognized += (_, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    segments.Add(new TranscriptSegment(
                        Start: e.Result.OffsetInTicks > 0
                            ? TimeSpan.FromTicks(e.Result.OffsetInTicks)
                            : TimeSpan.Zero,
                        End: TimeSpan.FromTicks(e.Result.OffsetInTicks + e.Result.Duration.Ticks),
                        Text: e.Result.Text,
                        Confidence: 0.9f // Azure Speech doesn't expose confidence directly in this API
                    ));
                }
            };

            recognizer.SessionStopped += (_, _) => tcs.TrySetResult(true);
            recognizer.Canceled += (_, e) =>
            {
                if (e.Reason == CancellationReason.Error)
                    _logger.LogError("Speech recognition error: {Error} - {Details}", e.ErrorCode, e.ErrorDetails);
                tcs.TrySetResult(false);
            };

            await recognizer.StartContinuousRecognitionAsync();

            // Wait for completion or cancellation
            using var registration = ct.Register(() => tcs.TrySetCanceled());
            await tcs.Task;

            await recognizer.StopContinuousRecognitionAsync();

            _logger.LogInformation("Transcription complete: {Count} segments from {File}",
                segments.Count, audioFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed for: {File}", audioFilePath);
        }

        return segments;
    }

    public async Task<string> GetTranscriptForRangeAsync(
        string audioFilePath,
        TimeSpan start,
        TimeSpan end,
        CancellationToken ct = default)
    {
        var allSegments = await TranscribeAsync(audioFilePath, ct);

        var rangeSegments = allSegments
            .Where(s => s.Start >= start && s.End <= end)
            .OrderBy(s => s.Start);

        return string.Join(" ", rangeSegments.Select(s => s.Text));
    }
}
