namespace OpenScribe.Core.Configuration;

/// <summary>
/// General application-level settings.
/// Maps to the "OpenScribe" section in appsettings.json.
/// </summary>
public class OpenScribeSettings
{
    public const string SectionName = "OpenScribe";

    /// <summary>Root folder for storing capture session artifacts.</summary>
    public string DataDirectory
    {
        get => string.IsNullOrEmpty(_dataDirectory) ? DefaultDataDirectory : _dataDirectory;
        set => _dataDirectory = value;
    }

    private string _dataDirectory = string.Empty;

    private static readonly string DefaultDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenScribe", "Sessions");

    /// <summary>Default organization name for document headers.</summary>
    public string? OrganizationName { get; set; }

    /// <summary>Default author name for document metadata.</summary>
    public string? DefaultAuthor { get; set; }

    /// <summary>Pixel region size around clicks to crop for OCR/analysis.</summary>
    public int CropRegionSize { get; set; } = 400;

    /// <summary>Minimum interval between captured clicks (ms) to debounce rapid clicks.</summary>
    public int ClickDebounceMs { get; set; } = 300;

    /// <summary>Whether to record audio by default when starting a session.</summary>
    public bool RecordAudioByDefault { get; set; } = true;

    /// <summary>Whether to record screen video in addition to click screenshots.</summary>
    public bool RecordVideoByDefault { get; set; } = false;

    /// <summary>Path to the default document template .docx.</summary>
    public string? DefaultTemplatePath { get; set; }

    /// <summary>Idle timeout (ms) before flushing accumulated keyboard input as a standalone step.</summary>
    public int KeyboardIdleTimeoutMs { get; set; } = 3000;

    /// <summary>Video recording frame rate (frames per second).</summary>
    public int VideoFrameRate { get; set; } = 10;

    /// <summary>JPEG quality for video frames (1-100).</summary>
    public int VideoQuality { get; set; } = 70;
}
