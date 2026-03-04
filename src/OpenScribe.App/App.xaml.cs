using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.UI.Xaml;
using OpenScribe.AI;
using OpenScribe.AI.Services;
using OpenScribe.App.Services;
using OpenScribe.App.ViewModels;
using OpenScribe.Capture;
using OpenScribe.Core.Configuration;
using OpenScribe.Data;
using OpenScribe.DocGen;
using OpenScribe.Processing;
using Serilog;

namespace OpenScribe.App;

/// <summary>
/// Application entry point. Configures DI, logging, and launches the main window.
/// </summary>
public partial class App : Application
{
    public static IHost Host { get; private set; } = null!;

    /// <summary>
    /// The main application window. Used to bring it to the foreground
    /// after the capture overlay closes.
    /// </summary>
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Build host with DI
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    // User settings override (writable location safe from Defender)
                    var userSettingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OpenScribe", "usersettings.json");
                    if (File.Exists(userSettingsPath))
                    {
                        config.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: true);
                    }
                })
                .UseSerilog((context, loggerConfig) =>
                {
                    loggerConfig
                        .ReadFrom.Configuration(context.Configuration)
                        .WriteTo.File(
                            Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "OpenScribe", "logs", "openscribe-.log"),
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 14);
                })
                .ConfigureServices((context, services) =>
                {
                    // Bind configuration sections
                    services.Configure<OpenScribeSettings>(
                        context.Configuration.GetSection(OpenScribeSettings.SectionName));
                    services.Configure<AzureOpenAISettings>(
                        context.Configuration.GetSection(AzureOpenAISettings.SectionName));
                    services.Configure<AzureSpeechSettings>(
                        context.Configuration.GetSection(AzureSpeechSettings.SectionName));

                    // Register service layers
                    services.AddOpenScribeData();
                    services.AddOpenScribeCapture();
                    services.AddOpenScribeProcessing();
                    services.AddOpenScribeAI();
                    services.AddOpenScribeDocGen();

                    // App services
                    services.AddSingleton<NavigationService>();
                    services.AddSingleton<ProcessingPipeline>();

                    // ViewModels — CaptureViewModel is Singleton so it survives page navigation
                    services.AddTransient<DashboardViewModel>();
                    services.AddSingleton<CaptureViewModel>();
                    services.AddTransient<SessionReviewViewModel>();
                    services.AddTransient<StepEditorViewModel>();
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<ExportViewModel>();
                })
                .Build();

            // Initialize database
            await Host.Services.InitializeDatabaseAsync();

            // Launch main window
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
        catch (Exception ex)
        {
            // Surface startup errors visibly
            var errorLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenScribe", "startup-error.log");
            try { File.WriteAllText(errorLog, $"{DateTime.Now}\n{ex}\n"); } catch { }

            // Show error window so the app doesn't silently hang
            var errorWindow = new Window { Title = "OpenScribe — Startup Error" };
            errorWindow.Content = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = $"Startup failed:\n\n{ex.Message}\n\nSee: {errorLog}",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Margin = new Thickness(24),
                IsTextSelectionEnabled = true
            };
            errorWindow.Activate();
        }
    }
}
