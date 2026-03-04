using Microsoft.Extensions.DependencyInjection;
using OpenScribe.Core.Interfaces;
using OpenScribe.Processing.Services;

namespace OpenScribe.Processing;

/// <summary>
/// Extension methods for registering processing services.
/// </summary>
public static class ProcessingServiceExtensions
{
    public static IServiceCollection AddOpenScribeProcessing(this IServiceCollection services)
    {
        services.AddSingleton<IOcrProcessor, WindowsOcrProcessor>();
        services.AddSingleton<ITranscriptionService, AzureSpeechTranscriptionService>();
        services.AddSingleton<RegionCropper>();
        services.AddSingleton<ITimelineBuilder, TimelineBuilder>();

        return services;
    }
}
