using Microsoft.Extensions.DependencyInjection;
using OpenScribe.Core.Interfaces;
using OpenScribe.DocGen.Services;

namespace OpenScribe.DocGen;

/// <summary>
/// Extension methods for registering document generation services.
/// </summary>
public static class DocGenServiceExtensions
{
    public static IServiceCollection AddOpenScribeDocGen(this IServiceCollection services)
    {
        services.AddSingleton<IScreenshotAnnotator, ScreenshotAnnotator>();
        services.AddSingleton<IDocxBuilder, DocxBuilder>();

        return services;
    }
}
