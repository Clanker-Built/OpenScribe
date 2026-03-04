using Microsoft.Extensions.DependencyInjection;
using OpenScribe.AI.Services;
using OpenScribe.Core.Interfaces;

namespace OpenScribe.AI;

/// <summary>
/// Extension methods for registering AI services.
/// </summary>
public static class AIServiceExtensions
{
    public static IServiceCollection AddOpenScribeAI(this IServiceCollection services)
    {
        services.AddSingleton<ICopilotClient, CopilotClient>();
        services.AddSingleton<StepAnalyzer>();

        return services;
    }
}
