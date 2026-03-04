using Microsoft.Extensions.DependencyInjection;
using OpenScribe.Capture.Services;
using OpenScribe.Core.Interfaces;

namespace OpenScribe.Capture;

/// <summary>
/// Extension methods for registering capture services.
/// </summary>
public static class CaptureServiceExtensions
{
    public static IServiceCollection AddOpenScribeCapture(this IServiceCollection services)
    {
        services.AddSingleton<IInputHookManager, InputHookManager>();
        services.AddSingleton<IScreenRecorder, ScreenRecorder>();
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
        services.AddSingleton<IVideoRecorder, VideoRecorder>();
        services.AddSingleton<ICaptureSessionManager, CaptureSessionManager>();
        services.AddSingleton<ICaptureTargetEnumerator, CaptureTargetEnumerator>();

        return services;
    }
}
