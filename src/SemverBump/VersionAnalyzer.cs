using Anthropic;
using Anthropic.Models.Messages;

namespace SemverBump;

public record AnalysisResult(BumpLevel Level, string Reasoning);

public class VersionAnalyzer
{
    private readonly AnthropicClient _client;
    private readonly string _model;

    public VersionAnalyzer(string? apiKey = null, string? model = null)
    {
        _client = apiKey is not null
            ? new AnthropicClient { ApiKey = apiKey }
            : new AnthropicClient(); // falls back to ANTHROPIC_API_KEY env var

        _model = model ?? "claude-sonnet-4-20250514";
    }

    public async Task<AnalysisResult> AnalyzeAsync(GitContext context)
    {
        var prompt = BuildPrompt(context);
        var parameters = new MessageCreateParams
        {
            MaxTokens = 1024,
            Model = _model,
            Messages =
            [
                new() { Role = Role.User, Content = prompt }
            ]
        };

        var message = await _client.Messages.Create(parameters);

        var responseText = message.Content
            .OfType<TextBlock>()
            .Select(b => b.Text)
            .FirstOrDefault() ?? "";

        return ParseResponse(responseText);
    }

    private static string BuildPrompt(GitContext context)
    {
        return $"""
            You are a semantic versioning analyst for a .NET NuGet package.

            Analyze the following git changes since the last release ({context.LastTag}, version {context.CurrentVersion})
            and determine the correct semver bump.

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

            ## Commit History ({context.CommitCount} commits)

            {context.CommitLog}

            ## Diff Summary

            {context.DiffSummary}

            ## Code Changes (C# public API surface)

            {context.PublicApiDiff}

            ## Response Format

            Respond with EXACTLY this format (no markdown, no extra text):

            LEVEL: major|minor|patch
            REASONING: A single paragraph explaining why.
            """;
    }

    private static AnalysisResult ParseResponse(string response)
    {
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        BumpLevel? level = null;
        string reasoning = "";

        foreach (var line in lines)
        {
            if (line.StartsWith("LEVEL:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["LEVEL:".Length..].Trim().ToLowerInvariant();
                level = value switch
                {
                    "major" => BumpLevel.Major,
                    "minor" => BumpLevel.Minor,
                    "patch" => BumpLevel.Patch,
                    _ => null
                };
            }
            else if (line.StartsWith("REASONING:", StringComparison.OrdinalIgnoreCase))
            {
                reasoning = line["REASONING:".Length..].Trim();
            }
        }

        if (level is null)
        {
            // Fallback: try to find the word in the raw response
            var lower = response.ToLowerInvariant();
            if (lower.Contains("major")) level = BumpLevel.Major;
            else if (lower.Contains("minor")) level = BumpLevel.Minor;
            else level = BumpLevel.Patch; // safe default

            if (string.IsNullOrEmpty(reasoning))
                reasoning = $"(Could not parse structured response, inferred {level} from raw output)";
        }

        return new AnalysisResult(level.Value, reasoning);
    }
}
