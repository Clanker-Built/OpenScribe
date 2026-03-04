using FluentAssertions;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Models;

namespace OpenScribe.Core.Tests;

public class CaptureSessionTests
{
    [Fact]
    public void New_Session_Has_Default_Values()
    {
        var session = new CaptureSession();

        session.Id.Should().NotBeEmpty();
        session.Name.Should().BeEmpty();
        session.Status.Should().Be(SessionStatus.Created);
        session.Steps.Should().NotBeNull().And.BeEmpty();
        session.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Session_Tracks_Error_State()
    {
        var session = new CaptureSession
        {
            Status = SessionStatus.Error,
            ErrorMessage = "Audio device not found"
        };

        session.Status.Should().Be(SessionStatus.Error);
        session.ErrorMessage.Should().Be("Audio device not found");
    }

    [Fact]
    public void Session_Can_Hold_Steps()
    {
        var session = new CaptureSession { Name = "Test Process" };
        session.Steps.Add(new ProcessStep { SequenceNumber = 1, WindowTitle = "Step 1" });
        session.Steps.Add(new ProcessStep { SequenceNumber = 2, WindowTitle = "Step 2" });

        session.Steps.Should().HaveCount(2);
    }
}

public class ProcessStepTests
{
    [Fact]
    public void EffectiveInstruction_Returns_UserEdit_When_Present()
    {
        var step = new ProcessStep
        {
            AiGeneratedInstruction = "AI instruction",
            UserEditedInstruction = "User override"
        };

        step.EffectiveInstruction.Should().Be("User override");
    }

    [Fact]
    public void EffectiveInstruction_Returns_AiGenerated_When_No_UserEdit()
    {
        var step = new ProcessStep
        {
            AiGeneratedInstruction = "AI instruction",
            UserEditedInstruction = null
        };

        step.EffectiveInstruction.Should().Be("AI instruction");
    }

    [Fact]
    public void EffectiveInstruction_Returns_AiGenerated_When_UserEdit_IsWhitespace()
    {
        var step = new ProcessStep
        {
            AiGeneratedInstruction = "AI instruction",
            UserEditedInstruction = "   "
        };

        step.EffectiveInstruction.Should().Be("AI instruction");
    }

    [Fact]
    public void EffectiveInstruction_Returns_Empty_When_Nothing_Set()
    {
        var step = new ProcessStep();

        step.EffectiveInstruction.Should().BeEmpty();
    }

    [Fact]
    public void New_Step_Has_Default_Values()
    {
        var step = new ProcessStep();

        step.Id.Should().NotBeEmpty();
        step.IsExcluded.Should().BeFalse();
        step.ClickType.Should().Be(ClickType.LeftClick);
        step.ScreenshotPath.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ClickType.LeftClick)]
    [InlineData(ClickType.RightClick)]
    [InlineData(ClickType.MiddleClick)]
    [InlineData(ClickType.DoubleClick)]
    public void Step_Stores_ClickType(ClickType clickType)
    {
        var step = new ProcessStep { ClickType = clickType };
        step.ClickType.Should().Be(clickType);
    }
}

public class ClickEventTests
{
    [Fact]
    public void ClickEvent_Is_Record_Type()
    {
        var e1 = new ClickEvent
        {
            Timestamp = TimeSpan.FromSeconds(5),
            X = 100,
            Y = 200,
            ClickType = ClickType.LeftClick,
            WindowTitle = "Test"
        };

        var e2 = new ClickEvent
        {
            Timestamp = TimeSpan.FromSeconds(5),
            X = 100,
            Y = 200,
            ClickType = ClickType.LeftClick,
            WindowTitle = "Test"
        };

        e1.Should().Be(e2);
    }
}
