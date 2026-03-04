using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenScribe.Core.Interfaces;

namespace OpenScribe.Data;

/// <summary>
/// Extension methods for registering data services.
/// </summary>
public static class DataServiceExtensions
{
    /// <summary>
    /// Register the OpenScribe data layer (SQLite + EF Core).
    /// </summary>
    public static IServiceCollection AddOpenScribeData(this IServiceCollection services, string? dbPath = null)
    {
        var effectivePath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenScribe", "openscribe.db");

        // Ensure directory exists
        var dir = Path.GetDirectoryName(effectivePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        services.AddDbContext<OpenScribeDbContext>(options =>
            options.UseSqlite($"Data Source={effectivePath}"),
            ServiceLifetime.Singleton, ServiceLifetime.Singleton);

        services.AddSingleton<ISessionRepository, SessionRepository>();

        return services;
    }

    /// <summary>
    /// Ensure the database is created and upgrade the schema if needed.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenScribeDbContext>();
        await db.Database.EnsureCreatedAsync();

        // Add columns that may be missing from older database versions.
        // SQLite ALTER TABLE ADD COLUMN is safe — it's a no-op if the column already exists
        // when wrapped in a try/catch (SQLite throws "duplicate column name" if it exists).
        var newColumns = new (string Column, string Type)[]
        {
            ("UiaControlType", "TEXT"),
            ("UiaElementName", "TEXT"),
            ("UiaAutomationId", "TEXT"),
            ("UiaClassName", "TEXT"),
            ("UiaElementBounds", "TEXT"),
            ("DpiScale", "REAL DEFAULT 1.0"),
            ("TypedText", "TEXT"),
            ("KeystrokeCount", "INTEGER DEFAULT 0"),
            ("DetailScreenshotPath", "TEXT"),
            ("AiCropRegion", "TEXT"),
        };

        foreach (var (column, type) in newColumns)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE Steps ADD COLUMN {column} {type}");
            }
            catch
            {
                // Column already exists — expected for new databases or re-runs
            }
        }

        // Add columns to Sessions table
        var sessionColumns = new (string Column, string Type)[]
        {
            ("WebResearchContext", "TEXT"),
        };

        foreach (var (column, type) in sessionColumns)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE Sessions ADD COLUMN {column} {type}");
            }
            catch
            {
                // Column already exists — expected for new databases or re-runs
            }
        }
    }
}
