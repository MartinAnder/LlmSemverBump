using System.Diagnostics;

namespace LlmSemverBump;

public static class GitAnalyzer
{
    public static async Task<string> GetLastTagAsync(
        string repoPath
    )
    {
        try
        {
            var tag = await RunGitAsync(
                repoPath,
                "describe --tags --abbrev=0"
            );
            return tag.Trim();
        }
        catch
        {
            throw new InvalidOperationException(
                "No git tags found. Create an initial version tag "
                + "first, e.g.: git tag v0.1.0"
            );
        }
    }

    public static Version ParseVersion(string tag)
    {
        var cleaned = tag.TrimStart('v', 'V');
        if (Version.TryParse(cleaned, out var version))
            return version;

        throw new InvalidOperationException(
            $"Could not parse version from tag '{tag}'. "
            + "Expected format: v1.2.3 or 1.2.3"
        );
    }

    public static string BumpVersion(
        Version current,
        BumpLevel level
    )
    {
        return level switch
        {
            BumpLevel.Major => $"{current.Major + 1}.0.0",
            BumpLevel.Minor =>
                $"{current.Major}"
                + $".{Math.Max(current.Minor, 0) + 1}.0",
            BumpLevel.Patch =>
                $"{current.Major}"
                + $".{Math.Max(current.Minor, 0)}"
                + $".{Math.Max(current.Build, 0) + 1}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(level)
            )
        };
    }

    public static async Task<int> GetCommitCountSinceTagAsync(
        string repoPath,
        string tag
    )
    {
        var log = await RunGitAsync(
            repoPath,
            $"log {tag}..HEAD --pretty=format:\"%h\" --no-merges"
        );

        return string.IsNullOrWhiteSpace(log)
            ? 0
            : log.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
            ).Length;
    }

    private static async Task<string> RunGitAsync(
        string workingDirectory,
        string arguments
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Failed to start git process"
            );

        var output = await process.StandardOutput
            .ReadToEndAsync();
        var error = await process.StandardError
            .ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {arguments} failed: {error}"
            );
        }

        return output;
    }
}
