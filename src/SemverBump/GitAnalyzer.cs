using System.Diagnostics;
using System.Text;

namespace SemverBump;

public record GitContext(
    string LastTag,
    Version CurrentVersion,
    string CommitLog,
    string DiffSummary,
    string PublicApiDiff,
    int CommitCount
);

public static class GitAnalyzer
{
    public static async Task<GitContext> GatherContextAsync(string repoPath, string? tagOverride = null)
    {
        var lastTag = tagOverride ?? await GetLastTagAsync(repoPath);
        var currentVersion = ParseVersion(lastTag);

        var commitLog = await RunGitAsync(repoPath,
            $"log {lastTag}..HEAD --pretty=format:\"%h %s\" --no-merges");

        var commitCount = string.IsNullOrWhiteSpace(commitLog)
            ? 0
            : commitLog.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        // Get a stat summary (files changed, insertions, deletions)
        var diffStat = await RunGitAsync(repoPath,
            $"diff {lastTag}..HEAD --stat");

        // Get the actual diff for public API surface files only (.cs files)
        // We filter to public-facing changes to keep token usage reasonable
        var publicApiDiff = await GetPublicApiDiffAsync(repoPath, lastTag);

        return new GitContext(
            LastTag: lastTag,
            CurrentVersion: currentVersion,
            CommitLog: commitLog,
            DiffSummary: diffStat,
            PublicApiDiff: publicApiDiff,
            CommitCount: commitCount
        );
    }

    private static async Task<string> GetLastTagAsync(string repoPath)
    {
        try
        {
            var tag = await RunGitAsync(repoPath, "describe --tags --abbrev=0");
            return tag.Trim();
        }
        catch
        {
            // No tags found — treat as initial development
            throw new InvalidOperationException(
                "No git tags found. Create an initial version tag first, e.g.: git tag v0.1.0");
        }
    }

    private static async Task<string> GetPublicApiDiffAsync(string repoPath, string lastTag)
    {
        // Get the full diff for .cs files
        var fullDiff = await RunGitAsync(repoPath,
            $"diff {lastTag}..HEAD -- \"*.cs\"");

        if (string.IsNullOrWhiteSpace(fullDiff))
            return "(no C# file changes)";

        // If the diff is very large, extract only the interesting parts:
        // file headers, and lines with public/protected/internal modifiers
        if (fullDiff.Length > 30_000)
        {
            return SummarizePublicApiChanges(fullDiff);
        }

        return fullDiff;
    }

    /// <summary>
    /// For very large diffs, extract only the lines relevant to public API surface.
    /// This keeps token usage manageable while preserving the important signal.
    /// </summary>
    private static string SummarizePublicApiChanges(string diff)
    {
        var sb = new StringBuilder();
        sb.AppendLine("(Large diff summarized — showing public API surface changes only)");
        sb.AppendLine();

        var currentFile = "";

        foreach (var line in diff.Split('\n'))
        {
            // Always include file headers
            if (line.StartsWith("diff --git") || line.StartsWith("---") || line.StartsWith("+++"))
            {
                if (line.StartsWith("diff --git"))
                {
                    currentFile = line;
                    sb.AppendLine();
                }
                sb.AppendLine(line);
                continue;
            }

            // Include added/removed lines that affect the public API surface
            if ((line.StartsWith('+') || line.StartsWith('-')) && line.Length > 1)
            {
                var trimmed = line[1..].Trim();
                if (IsPublicApiRelevant(trimmed))
                {
                    sb.AppendLine(line);
                }
            }
        }

        var result = sb.ToString();
        // If even the summary is huge, truncate with a note
        if (result.Length > 50_000)
        {
            return result[..50_000] + "\n\n... (truncated, diff too large)";
        }

        return result;
    }

    private static bool IsPublicApiRelevant(string line)
    {
        // Skip empty lines, comments, using statements
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.StartsWith("//") || line.StartsWith("/*") || line.StartsWith("*")) return false;
        if (line.StartsWith("using ")) return false;

        // Include lines with access modifiers, class/interface/enum/struct declarations,
        // method signatures, property declarations, attributes
        var keywords = new[]
        {
            "public ", "protected ", "internal ", "private ",
            "class ", "interface ", "enum ", "struct ", "record ",
            "namespace ", "[Obsolete", "[Deprecated",
            "abstract ", "virtual ", "override ", "sealed ",
            "delegate ", "event "
        };

        return keywords.Any(k => line.Contains(k, StringComparison.Ordinal));
    }

    public static Version ParseVersion(string tag)
    {
        var cleaned = tag.TrimStart('v', 'V');
        if (System.Version.TryParse(cleaned, out var version))
            return version;

        throw new InvalidOperationException(
            $"Could not parse version from tag '{tag}'. Expected format: v1.2.3 or 1.2.3");
    }

    public static string BumpVersion(Version current, BumpLevel level)
    {
        return level switch
        {
            BumpLevel.Major => $"{current.Major + 1}.0.0",
            BumpLevel.Minor => $"{current.Major}.{Math.Max(current.Minor, 0) + 1}.0",
            BumpLevel.Patch => $"{current.Major}.{Math.Max(current.Minor, 0)}.{Math.Max(current.Build, 0) + 1}",
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
    }

    private static async Task<string> RunGitAsync(string workingDirectory, string arguments)
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
            ?? throw new InvalidOperationException("Failed to start git process");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed: {error}");

        return output;
    }
}
