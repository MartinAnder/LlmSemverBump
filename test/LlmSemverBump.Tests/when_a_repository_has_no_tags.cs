using System.CommandLine;
using Moq;
using LlmSemverBump;
using Xunit;

namespace LlmSemverBump.Tests;

public class when_a_repository_has_no_tags : IAsyncLifetime
{
    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.CreateAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "MyLib.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsPackable>true</IsPackable>
                <Version>2.0.0</Version>
              </PropertyGroup>
            </Project>
            """
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"set version 2.0.0\"");

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "README.md"),
            "some change"
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"some change\"");
    }

    public ValueTask DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task it_detects_the_current_version_from_the_csproj()
    {
        // Arrange
        var analyzer = new Mock<IClaudeCodeAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync(
                _repo.Path,
                It.IsAny<string>(),
                null))
            .ReturnsAsync(new AnalysisResult(BumpLevel.Patch, "Bug fix only"));

        var command = new CommandFactory(analyzer.Object).Build();

        // Act
        var output = await StdoutCapture.CaptureAsync(() =>
            command.Parse([
                "--repo", _repo.Path,
                "--output", "version-only"
            ]).InvokeAsync(null, CancellationToken.None)
        );

        // Assert
        Assert.Equal("2.0.1", output.Trim());
    }

    [Fact]
    public async Task it_uses_the_csproj_version_change_commit_as_the_baseline()
    {
        // Arrange
        var capturedRef = (string?)null;
        var analyzer = new Mock<IClaudeCodeAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync(
                _repo.Path,
                It.IsAny<string>(),
                null))
            .Callback<string, string, string?>((_, r, _) => capturedRef = r)
            .ReturnsAsync(new AnalysisResult(BumpLevel.Minor, "New feature"));

        var command = new CommandFactory(analyzer.Object).Build();

        // Act
        await StdoutCapture.CaptureAsync(() =>
            command.Parse([
                "--repo", _repo.Path,
                "--output", "version-only"
            ]).InvokeAsync(null, CancellationToken.None)
        );

        // Assert — the ref passed to Claude should be a full commit hash (40 hex chars),
        // not a tag name
        Assert.NotNull(capturedRef);
        Assert.Matches("^[0-9a-f]{40}$", capturedRef);
    }

    [Fact]
    public async Task it_defaults_to_version_0_1_0_when_no_csproj_version_exists()
    {
        // Arrange — create a separate repo with no <Version> in any csproj
        await using var bareRepo = await TempGitRepo.CreateAsync();

        await File.WriteAllTextAsync(
            Path.Combine(bareRepo.Path, "App.csproj"), 
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """ , TestContext.Current.CancellationToken);

        await bareRepo.RunGitAsync("add .");
        await bareRepo.RunGitAsync("commit -m \"initial\"");

        await File.WriteAllTextAsync(
            Path.Combine(bareRepo.Path, "README.md"), 
            "hello", 
            TestContext.Current.CancellationToken);
        await bareRepo.RunGitAsync("add .");
        await bareRepo.RunGitAsync("commit -m \"add readme\"");

        var analyzer = new Mock<IClaudeCodeAnalyzer>();

        var command = new CommandFactory(analyzer.Object).Build();

        // Act
        var output = await StdoutCapture.CaptureAsync(() =>
            command.Parse([
                "--repo", bareRepo.Path,
                "--output", "version-only"
            ]).InvokeAsync(null, CancellationToken.None)
        );

        // Assert — no version history, so 0.1.0 is the initial version unchanged
        Assert.Equal("0.1.0", output.Trim());
        
        analyzer.Verify(
            a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }
}
