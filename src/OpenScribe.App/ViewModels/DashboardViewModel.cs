using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.App.ViewModels;

/// <summary>
/// ViewModel for the Dashboard / Home screen.
/// Shows recent sessions and provides quick actions.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<CaptureSession> RecentSessions { get; } = [];

    public DashboardViewModel(ISessionRepository sessionRepository, ILogger<DashboardViewModel> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadSessionsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading sessions...";

            var sessions = await _sessionRepository.GetAllAsync();

            RecentSessions.Clear();
            foreach (var session in sessions)
            {
                RecentSessions.Add(session);
            }

            StatusMessage = $"{sessions.Count} session(s) found";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sessions");
            StatusMessage = "Failed to load sessions";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSessionAsync(CaptureSession session)
    {
        try
        {
            await _sessionRepository.DeleteAsync(session.Id);
            RecentSessions.Remove(session);

            // Clean up artifacts
            if (!string.IsNullOrEmpty(session.ArtifactPath) && Directory.Exists(session.ArtifactPath))
            {
                Directory.Delete(session.ArtifactPath, recursive: true);
            }

            StatusMessage = $"Deleted: {session.Name}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete session {Id}", session.Id);
            StatusMessage = "Failed to delete session";
        }
    }
}
