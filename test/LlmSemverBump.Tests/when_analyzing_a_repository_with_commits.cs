using System.CommandLine;
using Moq;
using LlmSemverBump;
using Xunit;

namespace LlmSemverBump.Tests;

public class when_analyzing_a_repository_with_commits : IAsyncLifetime
{
    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.CreateAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "README.md"),
            "initial"
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"initial commit\"");
        await _repo.RunGitAsync("tag v1.0.0");

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "README.md"),
            "changed"
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"some change\"");
    }

    public ValueTask DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task it_outputs_a_patch_version_when_analyzer_returns_patch()
    {
        // Arrange
        var analyzer = new Mock<IClaudeCodeAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync(_repo.Path, "v1.0.0", null))
            .ReturnsAsync(new AnalysisResult(BumpLevel.Patch, "Bug fix only"));

        var command = new CommandFactory(analyzer.Object).Build();

        // Act
        var output = await StdoutCapture.CaptureAsync(() =>
            command.InvokeAsync([
                "--repo", _repo.Path,
                "--tag", "v1.0.0",
                "--output", "version-only"
            ])
        );

        // Assert
        Assert.Equal("1.0.1", output.Trim());
    }

    [Fact]
    public async Task it_outputs_a_minor_version_when_analyzer_returns_minor()
    {
        // Arrange
        var analyzer = new Mock<IClaudeCodeAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync(_repo.Path, "v1.0.0", null))
            .ReturnsAsync(new AnalysisResult(BumpLevel.Minor, "New feature added"));

        var command = new CommandFactory(analyzer.Object).Build();

        // Act
        var output = await StdoutCapture.CaptureAsync(() =>
            command.InvokeAsync([
                "--repo", _repo.Path,
                "--tag", "v1.0.0",
                "--output", "version-only"
            ])
        );

        // Assert
        Assert.Equal("1.1.0", output.Trim());
    }

    [Fact]
    public async Task it_outputs_a_major_version_when_analyzer_returns_major()
    {
        // Arrange
        var analyzer = new Mock<IClaudeCodeAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync(_repo.Path, "v1.0.0", null))
            .ReturnsAsync(new AnalysisResult(BumpLevel.Major, "Breaking change"));

        var command = new CommandFactory(analyzer.Object).Build();

        // Act
        var output = await StdoutCapture.CaptureAsync(() =>
            command.InvokeAsync([
                "--repo", _repo.Path,
                "--tag", "v1.0.0",
                "--output", "version-only"
            ])
        );

        // Assert
        Assert.Equal("2.0.0", output.Trim());
    }
}
