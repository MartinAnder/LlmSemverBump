using System.Diagnostics;
using System.Text.Json;

namespace SemverBump;

public record AnalysisResult(
    BumpLevel Level,
    string Reasoning
);

public class ClaudeCodeAnalyzer : IClaudeCodeAnalyzer
{
    public async Task<AnalysisResult> AnalyzeAsync(
        string repoPath,
        string lastTag,
        string? model = null
    )
    {
        await AssertClaudeIsAuthenticatedAsync();

        var prompt = BuildPrompt(lastTag);
        var output = await RunClaudeAsync(
            repoPath,
            prompt,
            model
        );

        return ParseResponse(output);
    }

    private static async Task AssertClaudeIsAuthenticatedAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = "auth status",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Failed to start claude process. "
                + "Ensure Claude Code CLI is installed: "
                + "npm install -g @anthropic-ai/claude-code"
            );

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        if (!root.TryGetProperty("loggedIn", out var loggedIn)
            || !loggedIn.GetBoolean())
        {
            var email = root.TryGetProperty("email", out var e)
                ? e.GetString()
                : null;

            var hint = email is not null
                ? $" (last known account: {email})"
                : "";

            throw new InvalidOperationException(
                $"Claude Code CLI is not authenticated{hint}. "
                + "Run `claude login` to sign in."
            );
        }
    }

    private static string BuildPrompt(string lastTag)
    {
        return $$"""
            You are a semantic versioning analyst for a .NET NuGet package.

            Analyze the git changes since the last release tag `{{lastTag}}` in this repository
            and determine the correct semver bump level.

            Use `git log`, `git diff`, and read any relevant source files
            to understand the changes.

            ## Rules

            **MAJOR** bump when:
            - Public types (classes, interfaces, structs, enums, records) are removed or renamed
            - Public method signatures change (parameters added/removed/reordered, return type changed)
            - Public properties are removed or change type
            - Interfaces gain new members (breaks implementors)
            - Namespaces are restructured in a breaking way
            - Behavioral changes that break existing consumers

            **MINOR** bump when:
            - New public types are added
            - New public methods or properties are added to existing types
            - New optional parameters with defaults are added
            - New interfaces or base classes are introduced
            - New enum values are added
            - New features that are backwards-compatible

            **PATCH** bump when:
            - Bug fixes with no API surface changes
            - Internal/private refactoring
            - Documentation changes
            - Test changes only
            - Dependency updates (unless they cause public API changes)
            - Performance improvements with no API changes

            ## Response Format

            Respond with ONLY a JSON object, no markdown fences, no extra text:

            {"bump": "major|minor|patch", "reasoning": "A single paragraph explaining why."}
            """;
    }

    private static async Task<string> RunClaudeAsync(
        string workingDirectory,
        string prompt,
        string? model
    )
    {
        var arguments = "-p --output-format json --max-turns 30";
        if (model is not null)
        {
            arguments += $" --model {model}";
        }

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                "Failed to start claude process. "
                + "Ensure Claude Code CLI is installed: "
                + "npm install -g @anthropic-ai/claude-code"
            );

        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"claude CLI failed (exit code {process.ExitCode}): {error}"
            );
        }

        return output;
    }

    private static AnalysisResult ParseResponse(string claudeOutput)
    {
        // The claude CLI with --output-format json returns a JSON object
        // with a "result" field containing the text response.
        using var doc = JsonDocument.Parse(claudeOutput);
        var root = doc.RootElement;

        var resultText = root.GetProperty("result").GetString()
            ?? throw new InvalidOperationException(
                "Claude CLI returned null result"
            );

        return ParseInnerJson(resultText);
    }

    private static AnalysisResult ParseInnerJson(string resultText)
    {
        // Strip markdown fences if Claude wrapped the JSON
        var cleaned = resultText.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline >= 0)
            {
                cleaned = cleaned[(firstNewline + 1)..];
            }

            var lastFence = cleaned.LastIndexOf("```");
            if (lastFence >= 0)
            {
                cleaned = cleaned[..lastFence];
            }

            cleaned = cleaned.Trim();
        }

        try
        {
            using var innerDoc = JsonDocument.Parse(cleaned);
            var innerRoot = innerDoc.RootElement;

            var bumpStr = innerRoot
                .GetProperty("bump")
                .GetString()
                ?.ToLowerInvariant();

            var reasoning = innerRoot
                .GetProperty("reasoning")
                .GetString() ?? "";

            var level = bumpStr switch
            {
                "major" => BumpLevel.Major,
                "minor" => BumpLevel.Minor,
                "patch" => BumpLevel.Patch,
                _ => throw new InvalidOperationException(
                    $"Unknown bump level: {bumpStr}"
                )
            };

            return new AnalysisResult(level, reasoning);
        }
        catch (JsonException)
        {
            return ParseFallback(resultText);
        }
    }

    private static AnalysisResult ParseFallback(string response)
    {
        var lower = response.ToLowerInvariant();

        BumpLevel level;
        if (lower.Contains("major"))
            level = BumpLevel.Major;
        else if (lower.Contains("minor"))
            level = BumpLevel.Minor;
        else
            level = BumpLevel.Patch;

        return new AnalysisResult(
            level,
            $"(Could not parse structured response, "
            + $"inferred {level} from raw output)"
        );
    }
}
