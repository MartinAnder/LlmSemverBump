using System.CommandLine;
using LlmSemverBump;
using Xunit;

namespace LlmSemverBump.IntegrationTests;

[Collection(nameof(ClaudeCodeIntegration))]
public class when_claude_code_analyzes_real_commits : IAsyncLifetime
{
    // Checked once at class load; drives [Fact(SkipUnless = ...)] below.
    public static bool ClaudeIsLoggedIn { get; } =
        ClaudeCodeAnalyzer.IsLoggedInAsync().GetAwaiter().GetResult();

    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        if (!ClaudeIsLoggedIn)
            return;

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
            "fixed a typo in the documentation"
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"fix typo in docs\"");
    }

    public ValueTask DisposeAsync()
    {
        if (_repo is null)
            return ValueTask.CompletedTask;

        return _repo.DisposeAsync();
    }

    [Fact(Skip = "Claude Code is not logged in", SkipUnless = nameof(ClaudeIsLoggedIn))]
    public async Task it_returns_a_valid_semver_bump()
    {
        // Arrange
        var command = new CommandFactory(new ClaudeCodeAnalyzer()).Build();

        // Act
        var output = await StdoutCapture.CaptureAsync(() =>
            command.Parse([
                "--repo", _repo.Path,
                "--tag", "v1.0.0",
                "--model", "claude-haiku-4-5-20251001",
                "--output", "version-only"
            ]).InvokeAsync(null, CancellationToken.None)
        );

        // Assert — bump level is non-deterministic; verify output is a valid
        // semver string bumped from 1.0.0.
        var validBumps = new[] { "1.0.1", "1.1.0", "2.0.0" };
        Assert.Contains(output.Trim(), validBumps);
    }
}
