using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenScribe.App.Services;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.App.ViewModels;

/// <summary>
/// ViewModel for the Session Review screen — displays captured steps,
/// allows reordering/editing, triggers AI processing, and shows the
/// generated document with one-click open.
/// </summary>
public partial class SessionReviewViewModel : ObservableObject
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ProcessingPipeline _pipeline;
    private readonly ILogger<SessionReviewViewModel> _logger;
    private readonly OpenScribeSettings _settings;

    [ObservableProperty]
    private CaptureSession? _session;

    [ObservableProperty]
    private ProcessStep? _selectedStep;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenDocumentFolderCommand))]
    private string? _generatedDocumentPath;

    public bool IsDocumentReady => !string.IsNullOrEmpty(GeneratedDocumentPath);

    public ObservableCollection<ProcessStep> Steps { get; } = [];

    public SessionReviewViewModel(
        ISessionRepository sessionRepository,
        ProcessingPipeline pipeline,
        IOptions<OpenScribeSettings> settings,
        ILogger<SessionReviewViewModel> logger)
    {
        _sessionRepository = sessionRepository;
        _pipeline = pipeline;
        _settings = settings.Value;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadSessionAsync(Guid sessionId)
    {
        try
        {
            Session = await _sessionRepository.GetByIdAsync(sessionId);
            if (Session is null)
            {
                StatusMessage = "Session not found.";
                return;
            }

            Steps.Clear();
            foreach (var step in Session.Steps.OrderBy(s => s.SequenceNumber))
            {
                Steps.Add(step);
            }

            // Restore document path if session was already exported
            if (!string.IsNullOrEmpty(Session.ExportedDocumentPath) && File.Exists(Session.ExportedDocumentPath))
            {
                GeneratedDocumentPath = Session.ExportedDocumentPath;
                OnPropertyChanged(nameof(IsDocumentReady));
            }
            else
            {
                GeneratedDocumentPath = null;
                OnPropertyChanged(nameof(IsDocumentReady));
            }

            StatusMessage = $"Loaded: {Session.Name} ({Steps.Count} steps)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load session {Id}", sessionId);
            StatusMessage = "Failed to load session.";
        }
    }

    /// <summary>
    /// Loads the most recently created session. Used as a fallback when
    /// the Review page is navigated to without a session ID parameter.
    /// </summary>
    [RelayCommand]
    public async Task LoadMostRecentSessionAsync()
    {
        try
        {
            var sessions = await _sessionRepository.GetAllAsync();
            var latest = sessions.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
            if (latest is not null)
            {
                await LoadSessionAsync(latest.Id);
            }
            else
            {
                StatusMessage = "No sessions found. Start a capture first.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load most recent session");
            StatusMessage = "Failed to load session.";
        }
    }

    [RelayCommand]
    public async Task ProcessWithAIAsync()
    {
        if (Session is null)
            return;

        try
        {
            IsProcessing = true;
            ProgressValue = 0;
            ProgressMessage = "Starting AI processing...";

            _pipeline.ProgressChanged += OnProgress;

            var exportSettings = new ExportSettings
            {
                OutputPath = Path.Combine(Session.ArtifactPath, $"{Session.Name}.docx"),
                IncludeScreenshots = true,
                UseAnnotatedScreenshots = true,
                IncludeNotes = true,
                IncludeTableOfContents = true,
                OrganizationName = _settings.OrganizationName,
                AuthorName = _settings.DefaultAuthor
            };

            var docPath = await _pipeline.ProcessSessionAsync(Session.Id, exportSettings);

            GeneratedDocumentPath = docPath;
            OnPropertyChanged(nameof(IsDocumentReady));

            StatusMessage = $"Document generated: {docPath}";
            ProgressMessage = "Complete!";
            ProgressValue = 100;

            // Reload to get updated AI content
            await LoadSessionAsync(Session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI processing failed for session {Id}", Session?.Id);
            StatusMessage = $"Processing failed: {ex.Message}";
        }
        finally
        {
            _pipeline.ProgressChanged -= OnProgress;
            IsProcessing = false;
        }
    }

    [RelayCommand]
    public async Task RemoveStepAsync(ProcessStep step)
    {
        await _sessionRepository.DeleteStepAsync(step.Id);
        Steps.Remove(step);

        // Renumber remaining steps
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].SequenceNumber = i + 1;
            await _sessionRepository.UpdateStepAsync(Steps[i]);
        }
    }

    [RelayCommand]
    public async Task ToggleStepExclusionAsync(ProcessStep step)
    {
        step.IsExcluded = !step.IsExcluded;
        await _sessionRepository.UpdateStepAsync(step);
    }

    private bool CanOpenDocument => !string.IsNullOrEmpty(GeneratedDocumentPath);

    [RelayCommand(CanExecute = nameof(CanOpenDocument))]
    private void OpenDocument()
    {
        if (GeneratedDocumentPath is null) return;
        Process.Start(new ProcessStartInfo(GeneratedDocumentPath) { UseShellExecute = true });
    }

    [RelayCommand(CanExecute = nameof(CanOpenDocument))]
    private void OpenDocumentFolder()
    {
        if (GeneratedDocumentPath is null) return;
        var folder = Path.GetDirectoryName(GeneratedDocumentPath);
        if (folder is not null)
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void OnProgress(object? sender, (int Current, int Total, string Message) e)
    {
        ProgressValue = e.Total > 0 ? (double)e.Current / e.Total * 100 : 0;
        ProgressMessage = e.Message;
    }
}
