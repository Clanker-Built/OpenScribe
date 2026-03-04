using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;
using OpenScribe.Capture.Services;

namespace OpenScribe.Capture.Tests;

public class CaptureSessionManagerTests
{
    private readonly Mock<IScreenRecorder> _screenRecorder = new();
    private readonly Mock<IInputHookManager> _inputHookManager = new();
    private readonly Mock<IAudioRecorder> _audioRecorder = new();
    private readonly Mock<IVideoRecorder> _videoRecorder = new();
    private readonly Mock<ISessionRepository> _sessionRepo = new();
    private readonly Mock<ILogger<CaptureSessionManager>> _logger = new();

    private CaptureSessionManager CreateManager()
    {
        var settings = Options.Create(new OpenScribeSettings
        {
            DataDirectory = Path.GetTempPath()
        });

        return new CaptureSessionManager(
            _screenRecorder.Object,
            _inputHookManager.Object,
            _audioRecorder.Object,
            _videoRecorder.Object,
            _sessionRepo.Object,
            settings,
            _logger.Object);
    }

    [Fact]
    public async Task StartSessionAsync_Creates_Session_With_Recording_Status()
    {
        CaptureSession? saved = null;
        _sessionRepo
            .Setup(r => r.CreateAsync(It.IsAny<CaptureSession>(), It.IsAny<CancellationToken>()))
            .Callback<CaptureSession, CancellationToken>((s, _) => saved = s)
            .ReturnsAsync((CaptureSession s, CancellationToken _) => s);

        _screenRecorder
            .Setup(r => r.StartRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var manager = CreateManager();
        var session = await manager.StartSessionAsync("Test Capture", recordAudio: false);

        session.Should().NotBeNull();
        session.Name.Should().Be("Test Capture");
        session.Status.Should().Be(SessionStatus.Recording);
        saved.Should().NotBeNull();
        _inputHookManager.Verify(h => h.Start(), Times.Once);
    }

    [Fact]
    public async Task StartSessionAsync_With_Audio_Starts_AudioRecorder()
    {
        _sessionRepo
            .Setup(r => r.CreateAsync(It.IsAny<CaptureSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CaptureSession s, CancellationToken _) => s);

        _screenRecorder
            .Setup(r => r.StartRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _audioRecorder
            .Setup(a => a.StartRecordingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var manager = CreateManager();
        await manager.StartSessionAsync("Test", recordAudio: true);

        _audioRecorder.Verify(a => a.StartRecordingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopSessionAsync_Stops_Hooks_And_Updates_Session()
    {
        _sessionRepo
            .Setup(r => r.CreateAsync(It.IsAny<CaptureSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CaptureSession s, CancellationToken _) => s);
        _sessionRepo
            .Setup(r => r.UpdateAsync(It.IsAny<CaptureSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _screenRecorder
            .Setup(r => r.StartRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _screenRecorder
            .Setup(r => r.StopRecordingAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var manager = CreateManager();
        await manager.StartSessionAsync("Test", recordAudio: false);
        var session = await manager.StopSessionAsync();

        session.Status.Should().Be(SessionStatus.Captured);
        _inputHookManager.Verify(h => h.Stop(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartSessionAsync_Throws_If_Already_Active()
    {
        _sessionRepo
            .Setup(r => r.CreateAsync(It.IsAny<CaptureSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CaptureSession s, CancellationToken _) => s);
        _screenRecorder
            .Setup(r => r.StartRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var manager = CreateManager();
        await manager.StartSessionAsync("First", recordAudio: false);

        var act = () => manager.StartSessionAsync("Second", recordAudio: false);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
