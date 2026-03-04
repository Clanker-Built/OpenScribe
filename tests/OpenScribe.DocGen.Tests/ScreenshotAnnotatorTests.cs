using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OpenScribe.Core.Interfaces;
using OpenScribe.DocGen.Services;
using SkiaSharp;

namespace OpenScribe.DocGen.Tests;

public class ScreenshotAnnotatorTests
{
    private readonly Mock<ILogger<ScreenshotAnnotator>> _logger = new();

    [Fact]
    public async Task AnnotateAsync_Creates_Output_File()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "openscribe_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a test image
            var inputPath = Path.Combine(tempDir, "input.png");
            var outputPath = Path.Combine(tempDir, "output.png");

            using (var bmp = new SKBitmap(800, 600))
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.White);
                using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.OpenWrite(inputPath);
                data.SaveTo(stream);
            }

            var annotator = new ScreenshotAnnotator(_logger.Object);

            await annotator.AnnotateAsync(
                inputPath, outputPath,
                stepNumber: 1,
                highlightX: 100, highlightY: 100,
                highlightWidth: 200, highlightHeight: 50);

            File.Exists(outputPath).Should().BeTrue("annotated file should be created");

            // Verify the output is a valid PNG and has the same dimensions
            using var output = SKBitmap.Decode(outputPath);
            output.Should().NotBeNull();
            output.Width.Should().Be(800);
            output.Height.Should().Be(600);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AnnotateAsync_Falls_Back_To_Copy_On_Invalid_Input()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "openscribe_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var inputPath = Path.Combine(tempDir, "notanimage.png");
            var outputPath = Path.Combine(tempDir, "output.png");

            // Write garbage file
            await File.WriteAllTextAsync(inputPath, "this is not an image");

            var annotator = new ScreenshotAnnotator(_logger.Object);

            // Should not throw - falls back to copy
            await annotator.AnnotateAsync(
                inputPath, outputPath,
                stepNumber: 1,
                highlightX: 10, highlightY: 10,
                highlightWidth: 50, highlightHeight: 50);

            File.Exists(outputPath).Should().BeTrue("fallback copy should create the output file");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
