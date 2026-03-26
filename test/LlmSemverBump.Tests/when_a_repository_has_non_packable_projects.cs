using Moq;
using Xunit;

namespace LlmSemverBump.Tests;

public class when_a_repository_has_non_packable_projects : IAsyncLifetime
{
    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.CreateAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "Packable.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsPackable>true</IsPackable>
                <Version>3.0.0</Version>
              </PropertyGroup>
            </Project>
            """
        );

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "NotPackable.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>9.9.9</Version>
              </PropertyGroup>
            </Project>
            """
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"set versions\"");

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "README.md"),
            "some change"
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"some change\"");
    }

    public ValueTask DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task it_reads_the_version_only_from_the_packable_project()
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

        // Assert — version is bumped from 3.0.0 (packable), not 9.9.9 (non-packable)
        Assert.Equal("3.0.1", output.Trim());
    }

    [Fact]
    public async Task it_skips_non_packable_projects_when_applying_version_bump()
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
        await StdoutCapture.CaptureAsync(() =>
            command.Parse([
                "--repo", _repo.Path,
                "--output", "version-only",
                "--apply"
            ]).InvokeAsync(null, CancellationToken.None)
        );

        // Assert — only the packable project was updated
        var packableContent = await File.ReadAllTextAsync(
            Path.Combine(_repo.Path, "Packable.csproj"),
            TestContext.Current.CancellationToken);
        var notPackableContent = await File.ReadAllTextAsync(
            Path.Combine(_repo.Path, "NotPackable.csproj"),
            TestContext.Current.CancellationToken);

        Assert.Contains("<Version>3.0.1</Version>", packableContent);
        Assert.Contains("<Version>9.9.9</Version>", notPackableContent);
    }
}

public class when_a_repository_has_only_pack_as_tool_projects : IAsyncLifetime
{
    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.CreateAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "MyTool.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackAsTool>true</PackAsTool>
                <Version>1.5.0</Version>
              </PropertyGroup>
            </Project>
            """
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"set version 1.5.0\"");

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "README.md"),
            "some change"
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"some change\"");
    }

    public ValueTask DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task it_reads_the_version_from_a_pack_as_tool_project()
    {
        // Arrange
        var analyzer = new Mock<IClaudeCodeAnalyzer>();
        analyzer
            .Setup(a => a.AnalyzeAsync(
                _repo.Path,
                It.IsAny<string>(),
                null))
            .ReturnsAsync(new AnalysisResult(BumpLevel.Minor, "New feature"));

        var command = new CommandFactory(analyzer.Object).Build();

        // Act
        var output = await StdoutCapture.CaptureAsync(() =>
            command.Parse([
                "--repo", _repo.Path,
                "--output", "version-only"
            ]).InvokeAsync(null, CancellationToken.None)
        );

        // Assert — version bumped from 1.5.0 (PackAsTool project)
        Assert.Equal("1.6.0", output.Trim());
    }
}
