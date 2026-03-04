using FluentAssertions;
using OpenScribe.AI.Prompts;

namespace OpenScribe.AI.Tests;

public class PromptTemplateTests
{
    [Fact]
    public void SystemPrompt_Contains_TechnicalWriter_Persona()
    {
        PromptTemplates.SystemPrompt.Should().Contain("technical writer");
    }

    [Fact]
    public void StepAnalysisPrompt_Contains_All_Placeholders()
    {
        var prompt = PromptTemplates.StepAnalysisPrompt;

        prompt.Should().Contain("{stepNumber}");
        prompt.Should().Contain("{clickX}");
        prompt.Should().Contain("{clickY}");
        prompt.Should().Contain("{clickType}");
        prompt.Should().Contain("{windowTitle}");
        prompt.Should().Contain("{applicationName}");
        prompt.Should().Contain("{typedText}");
        prompt.Should().Contain("{ocrText}");
        prompt.Should().Contain("{voiceTranscript}");
        prompt.Should().Contain("{previousStepContext}");
    }

    [Fact]
    public void StepAnalysisPrompt_Requests_JSON_Response()
    {
        PromptTemplates.StepAnalysisPrompt.Should().Contain("stepTitle");
        PromptTemplates.StepAnalysisPrompt.Should().Contain("instruction");
        PromptTemplates.StepAnalysisPrompt.Should().Contain("notes");
    }

    [Fact]
    public void DocumentPlanPrompt_Contains_Placeholder()
    {
        PromptTemplates.DocumentPlanPrompt.Should().Contain("{stepsJson}");
    }

    [Fact]
    public void DocumentPlanPrompt_Requests_Title_Intro_Summary()
    {
        var prompt = PromptTemplates.DocumentPlanPrompt;

        prompt.Should().Contain("title");
        prompt.Should().Contain("introduction");
        prompt.Should().Contain("summary");
        prompt.Should().Contain("revisedInstructions");
    }

    [Fact]
    public async Task LoadTemplateAsync_Returns_BuiltIn_For_Known_Names()
    {
        var system = await PromptTemplates.LoadTemplateAsync("system");
        system.Should().Be(PromptTemplates.SystemPrompt);

        var stepAnalysis = await PromptTemplates.LoadTemplateAsync("step-analysis");
        stepAnalysis.Should().Be(PromptTemplates.StepAnalysisPrompt);

        var docPlan = await PromptTemplates.LoadTemplateAsync("document-plan");
        docPlan.Should().Be(PromptTemplates.DocumentPlanPrompt);
    }

    [Fact]
    public async Task LoadTemplateAsync_Throws_For_Unknown_Name()
    {
        var act = () => PromptTemplates.LoadTemplateAsync("nonexistent-template");
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown template*");
    }

    [Fact]
    public async Task LoadTemplateAsync_Loads_From_File_When_Available()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "openscribe_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var templateContent = "Custom system prompt from file";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "system.txt"), templateContent);

            var result = await PromptTemplates.LoadTemplateAsync("system", tempDir);
            result.Should().Be(templateContent);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
