using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenScribe.App.Services;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.App.ViewModels;

/// <summary>
/// ViewModel for the Export screen — selects sessions and generates documents.
/// </summary>
public partial class ExportViewModel : ObservableObject
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ProcessingPipeline _pipeline;
    private readonly ILogger<ExportViewModel> _logger;

    [ObservableProperty]
    private CaptureSession? _selectedSession;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private bool _includeScreenshots = true;

    [ObservableProperty]
    private bool _useAnnotatedScreenshots = true;

    [ObservableProperty]
    private bool _includeNotes = true;

    [ObservableProperty]
    private string _organizationName = string.Empty;

    [ObservableProperty]
    private string _authorName = string.Empty;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<CaptureSession> AvailableSessions { get; } = [];

    public ExportViewModel(
        ISessionRepository sessionRepository,
        ProcessingPipeline pipeline,
        ILogger<ExportViewModel> logger)
    {
        _sessionRepository = sessionRepository;
        _pipeline = pipeline;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadSessionsAsync()
    {
        var sessions = await _sessionRepository.GetAllAsync();
        AvailableSessions.Clear();
        foreach (var s in sessions.Where(s => s.Status >= SessionStatus.Captured && s.Status != SessionStatus.Error))
        {
            AvailableSessions.Add(s);
        }
    }

    [RelayCommand]
    public async Task ExportAsync()
    {
        if (SelectedSession is null)
        {
            StatusMessage = "Please select a session.";
            return;
        }

        try
        {
            IsExporting = true;
            StatusMessage = "Exporting...";

            _pipeline.ProgressChanged += OnProgress;

            var settings = new ExportSettings
            {
                OutputPath = string.IsNullOrEmpty(OutputPath)
                    ? Path.Combine(
                        SelectedSession.ArtifactPath,
                        $"{SelectedSession.Name}.docx")
                    : OutputPath,
                IncludeScreenshots = IncludeScreenshots,
                UseAnnotatedScreenshots = UseAnnotatedScreenshots,
                IncludeNotes = IncludeNotes,
                OrganizationName = string.IsNullOrEmpty(OrganizationName) ? null : OrganizationName,
                AuthorName = string.IsNullOrEmpty(AuthorName) ? null : AuthorName
            };

            var docPath = await _pipeline.ProcessSessionAsync(SelectedSession.Id, settings);
            StatusMessage = $"Document saved to: {docPath}";
            OutputPath = docPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed");
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            _pipeline.ProgressChanged -= OnProgress;
            IsExporting = false;
        }
    }

    private void OnProgress(object? sender, (int Current, int Total, string Message) e)
    {
        ProgressValue = e.Total > 0 ? (double)e.Current / e.Total * 100 : 0;
        StatusMessage = e.Message;
    }
}
