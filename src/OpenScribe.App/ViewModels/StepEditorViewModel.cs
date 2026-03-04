using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.App.ViewModels;

/// <summary>
/// ViewModel for editing a single step's instruction and notes.
/// </summary>
public partial class StepEditorViewModel : ObservableObject
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<StepEditorViewModel> _logger;

    [ObservableProperty]
    private ProcessStep? _step;

    [ObservableProperty]
    private string _instruction = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public StepEditorViewModel(ISessionRepository sessionRepository, ILogger<StepEditorViewModel> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public void LoadStep(ProcessStep step)
    {
        Step = step;
        Instruction = step.UserEditedInstruction ?? step.AiGeneratedInstruction ?? string.Empty;
        Notes = step.UserNotes ?? step.AiGeneratedNotes ?? string.Empty;
        IsDirty = false;
    }

    partial void OnInstructionChanged(string value) => IsDirty = true;
    partial void OnNotesChanged(string value) => IsDirty = true;

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Step is null)
            return;

        Step.UserEditedInstruction = Instruction;
        Step.UserNotes = Notes;

        await _sessionRepository.UpdateStepAsync(Step);
        IsDirty = false;
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    public void RevertToAI()
    {
        if (Step is null)
            return;

        Instruction = Step.AiGeneratedInstruction ?? string.Empty;
        Notes = Step.AiGeneratedNotes ?? string.Empty;
        IsDirty = true;
        StatusMessage = "Reverted to AI-generated text.";
    }
}
