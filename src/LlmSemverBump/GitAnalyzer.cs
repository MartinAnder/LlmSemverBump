using System.Diagnostics;

namespace LlmSemverBump;

public static class GitAnalyzer
{
    public static async Task<string?> TryGetLastTagAsync(
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
            return null;
        }
    }

    public static async Task<string?> TryGetLastVersionChangeRefAsync(
        string repoPath
    )
    {
        try
        {
            var hash = await RunGitAsync(
                repoPath,
                "log -1 --pretty=format:\"%H\" -G \"<Version>[0-9]\" -- *.csproj"
            );
            var trimmed = hash.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<Version?> ReadVersionFromCsprojAsync(
        string repoPath
    )
    {
        var files = Directory.GetFiles(
            repoPath,
            "*.csproj",
            SearchOption.AllDirectories
        );

        Version? highest = null;

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file);

            if (content.Contains(
                    "<IsPackable>false</IsPackable>",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var start = content.IndexOf("<Version>");
            if (start < 0)
                continue;

            start += "<Version>".Length;
            var end = content.IndexOf("</Version>", start);
            if (end < 0)
                continue;

            var versionStr = content[start..end].Trim();
            if (!Version.TryParse(versionStr, out var version))
                continue;

            if (highest == null || version > highest)
                highest = version;
        }

        return highest;
    }

    public static async Task<string> GetRootCommitAsync(
        string repoPath
    )
    {
        var hash = await RunGitAsync(
            repoPath,
            "rev-list --max-parents=0 HEAD"
        );
        return hash.Trim();
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

    public static async Task<int> GetCommitCountSinceRefAsync(
        string repoPath,
        string gitRef
    )
    {
        var log = await RunGitAsync(
            repoPath,
            $"log {gitRef}..HEAD --pretty=format:\"%h\" --no-merges"
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
