using System.CommandLine;
using Xunit;

namespace LlmSemverBump.IntegrationTests;

[Collection(nameof(ClaudeCodeIntegration))]
public class when_a_public_class_is_removed : IAsyncLifetime
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
            Path.Combine(_repo.Path, "OrderService.cs"),
            """
            namespace MyLib;

            public class OrderService
            {
                public void PlaceOrder(string item) { }
            }
            """
        );

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"initial commit\"");
        await _repo.RunGitAsync("tag v1.0.0");

        File.Delete(Path.Combine(_repo.Path, "OrderService.cs"));

        await _repo.RunGitAsync("add .");
        await _repo.RunGitAsync("commit -m \"remove OrderService class\"");
    }

    public ValueTask DisposeAsync()
    {
        if (_repo is null)
            return ValueTask.CompletedTask;

        return _repo.DisposeAsync();
    }

    [Fact(Skip = "Claude Code is not logged in", SkipUnless = nameof(ClaudeIsLoggedIn))]
    public async Task it_returns_2_0_0()
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

        // Assert
        Assert.Equal("2.0.0", output.Trim());
    }
}