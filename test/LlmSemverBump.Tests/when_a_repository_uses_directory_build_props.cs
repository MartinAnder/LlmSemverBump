using Moq;
using Xunit;

namespace LlmSemverBump.Tests;

public class when_a_repository_has_a_directory_build_props_with_version : IAsyncLifetime
{
    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.CreateAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <Version>2.0.0</Version>
              </PropertyGroup>
            </Project>
            """
        );

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "MyLib.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <IsPackable>true</IsPackable>
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
    public async Task it_reads_the_version_from_directory_build_props()
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

        // Assert — version bumped from 2.0.0 declared in Directory.Build.props
        Assert.Equal("2.0.1", output.Trim());
    }

    [Fact]
    public async Task it_updates_the_version_in_directory_build_props_when_applying()
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

        // Assert — Directory.Build.props is updated; MyLib.csproj has no version to update
        var propsContent = await File.ReadAllTextAsync(
            Path.Combine(_repo.Path, "Directory.Build.props"),
            TestContext.Current.CancellationToken);
        var csprojContent = await File.ReadAllTextAsync(
            Path.Combine(_repo.Path, "MyLib.csproj"),
            TestContext.Current.CancellationToken);

        Assert.Contains("<Version>2.0.1</Version>", propsContent);
        Assert.DoesNotContain("<Version>", csprojContent);
    }
}