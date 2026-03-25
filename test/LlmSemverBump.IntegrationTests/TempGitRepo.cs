using System.Diagnostics;

namespace LlmSemverBump.IntegrationTests;

internal sealed class TempGitRepo : IAsyncDisposable
{
    public string Path { get; }

    private TempGitRepo(string path)
    {
        Path = path;
    }

    public static async Task<TempGitRepo> CreateAsync()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"semver-bump-test-{Guid.NewGuid()}"
        );

        Directory.CreateDirectory(path);

        var repo = new TempGitRepo(path);

        await repo.RunGitAsync("init");
        await repo.RunGitAsync("config user.email \"test@example.com\"");
        await repo.RunGitAsync("config user.name \"Test\"");

        return repo;
    }

    public async Task RunGitAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Failed to start git process"
            );

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"git {arguments} failed: {error}"
            );
        }
    }

    public ValueTask DisposeAsync()
    {
        // Git object files on Windows are read-only; clear the attribute
        // before deleting so Directory.Delete succeeds.
        foreach (var file in Directory.EnumerateFiles(
            Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Path, recursive: true);
        return ValueTask.CompletedTask;
    }
}
