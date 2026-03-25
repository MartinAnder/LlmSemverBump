using System.CommandLine;
using Xunit;

namespace LlmSemverBump.IntegrationTests;

[Collection(nameof(ClaudeCodeIntegration))]
public class when_a_new_public_method_is_added : IAsyncLifetime
{
    public static bool ClaudeIsLoggedIn { get; } =
        ClaudeCodeAnalyzer.IsLoggedInAsync().GetAwaiter().GetResult();

    private TempGitRepo _repo = null!;

    public async ValueTask InitializeAsync()
    {
        if (!ClaudeIsLoggedIn)
            return;

        _repo = await TempGitRepo.CreateAsync();

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "GreetingService.cs"),
            """
            namespace MyLib;

            public class GreetingService
            {
                public string Greet(string name) => $"Hello, {name}!";
            }
            """
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"initial commit\"");
        await _repo.RunGitAsync("tag v1.0.0");

        await File.WriteAllTextAsync(
            Path.Combine(_repo.Path, "GreetingService.cs"),
            """
            namespace MyLib;

            public class GreetingService
            {
                public string Greet(string name) => $"Hello, {name}!";
                public string Farewell(string name) => $"Goodbye, {name}!";
            }
            """
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"add Farewell method to GreetingService\"");
    }

    public ValueTask DisposeAsync()
    {
        if (_repo is null)
            return ValueTask.CompletedTask;

        return _repo.DisposeAsync();
    }

    [Fact(Skip = "Claude Code is not logged in", SkipUnless = nameof(ClaudeIsLoggedIn))]
    public async Task it_returns_1_1_0()
    {
        // Arrange
        var command = new CommandFactory(new ClaudeCodeAnalyzer()).Build();

        // Act
        var output = await StdoutCapture.CaptureAsync(() =>
            command.InvokeAsync([
                "--repo", _repo.Path,
                "--tag", "v1.0.0",
                "--model", "claude-haiku-4-5-20251001",
                "--output", "version-only"
            ])
        );

        // Assert
        Assert.Equal("1.1.0", output.Trim());
    }
}