using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;
using OpenScribe.Processing.Services;

namespace OpenScribe.Processing.Tests;

public class TimelineBuilderTests
{
    private readonly Mock<IOcrProcessor> _ocrProcessor = new();
    private readonly Mock<ITranscriptionService> _transcriptionService = new();
    private readonly Mock<ILogger<TimelineBuilder>> _logger = new();
    private readonly Mock<ILogger<RegionCropper>> _cropperLogger = new();

    private TimelineBuilder CreateBuilder()
    {
        var settings = Options.Create(new OpenScribeSettings { CropRegionSize = 400 });
        var cropperSettings = Options.Create(new OpenScribeSettings { CropRegionSize = 400 });
        var cropper = new RegionCropper(_cropperLogger.Object, cropperSettings);

        return new TimelineBuilder(
            _ocrProcessor.Object,
            _transcriptionService.Object,
            cropper,
            _logger.Object,
            settings);
    }

    [Fact]
    public async Task BuildTimelineAsync_Returns_Empty_For_Empty_Session()
    {
        _transcriptionService
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TranscriptSegment>());

        var builder = CreateBuilder();
        var session = new CaptureSession { Name = "Empty" };

        var result = await builder.BuildTimelineAsync(session);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildTimelineAsync_Orders_Steps_By_SequenceNumber()
    {
        _transcriptionService
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TranscriptSegment>());

        var builder = CreateBuilder();
        var session = new CaptureSession
        {
            Name = "Test",
            Steps =
            [
                new ProcessStep
                {
                    SequenceNumber = 3,
                    Timestamp = TimeSpan.FromSeconds(3),
                    ClickType = ClickType.LeftClick,
                    ScreenshotPath = "nonexistent_3.png"
                },
                new ProcessStep
                {
                    SequenceNumber = 1,
                    Timestamp = TimeSpan.FromSeconds(1),
                    ClickType = ClickType.LeftClick,
                    ScreenshotPath = "nonexistent_1.png"
                },
                new ProcessStep
                {
                    SequenceNumber = 2,
                    Timestamp = TimeSpan.FromSeconds(2),
                    ClickType = ClickType.LeftClick,
                    ScreenshotPath = "nonexistent_2.png"
                }
            ]
        };

        var result = await builder.BuildTimelineAsync(session);

        result.Should().HaveCount(3);
        result[0].SequenceNumber.Should().Be(1);
        result[1].SequenceNumber.Should().Be(2);
        result[2].SequenceNumber.Should().Be(3);
    }

    [Fact]
    public async Task BuildTimelineAsync_Assigns_Transcript_To_Correct_Step()
    {
        // No audio path means no transcription
        var builder = CreateBuilder();
        var session = new CaptureSession
        {
            Name = "NoAudio",
            AudioPath = null,
            Steps =
            [
                new ProcessStep
                {
                    SequenceNumber = 1,
                    Timestamp = TimeSpan.FromSeconds(1),
                    ClickType = ClickType.LeftClick,
                    ScreenshotPath = "nonexistent.png"
                }
            ]
        };

        var result = await builder.BuildTimelineAsync(session);

        result.Should().HaveCount(1);
        result[0].VoiceTranscript.Should().BeNull();
    }
}
