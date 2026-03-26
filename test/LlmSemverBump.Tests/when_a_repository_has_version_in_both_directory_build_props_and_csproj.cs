using Moq;
using Xunit;

namespace LlmSemverBump.Tests;

public class when_a_repository_has_version_in_both_directory_build_props_and_csproj
    : IAsyncLifetime
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
                <Version>1.0.0</Version>
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
                <Version>1.5.0</Version>
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
    public async Task it_reads_the_highest_version()
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

        // Assert — highest is 1.5.0 from MyLib.csproj, bumped to 1.5.1
        Assert.Equal("1.5.1", output.Trim());
    }
}