using System.CommandLine;

namespace LlmSemverBump;

public class CommandFactory(IClaudeCodeAnalyzer analyzer)
{
    public RootCommand Build()
    {
        var repoOption = new Option<string>("--repo", ["-r"])
        {
            Description = "Path to the git repository",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory()
        };

        var tagOption = new Option<string?>("--tag", ["-t"])
        {
            Description =
                "Override the base tag "
                + "(default: latest tag via git describe)"
        };

        var csprojOption = new Option<string?>("--csproj", ["-c"])
        {
            Description =
                "Path to a specific .csproj to update "
                + "(default: all .csproj files with <Version>)"
        };

        var applyOption = new Option<bool>("--apply", ["-a"])
        {
            Description =
                "Apply the version bump to .csproj files "
                + "(default: dry run)"
        };

        var gitTagOption = new Option<bool>("--git-tag")
        {
            Description =
                "Create a git tag with the new version after applying"
        };

        var modelOption = new Option<string?>("--model", ["-m"])
        {
            Description =
                "Claude model to use "
                + "(passed to claude CLI --model)"
        };

        var outputOption = new Option<string>("--output", ["-o"])
        {
            Description = "Output format: text, json, or version-only",
            DefaultValueFactory = _ => "text"
        };

        var rootCommand = new RootCommand(
            "AI-powered semantic version bumping. "
            + "Analyzes git history with Claude Code CLI "
            + "to determine the correct semver bump.")
        {
            repoOption,
            tagOption,
            csprojOption,
            applyOption,
            gitTagOption,
            modelOption,
            outputOption,
        };

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var repo = parseResult.GetValue(repoOption)!;
            var tag = parseResult.GetValue(tagOption);
            var csproj = parseResult.GetValue(csprojOption);
            var apply = parseResult.GetValue(applyOption);
            var gitTag = parseResult.GetValue(gitTagOption);
            var model = parseResult.GetValue(modelOption);
            var output = parseResult.GetValue(outputOption)!;

            try
            {
                // 1. Resolve the baseline ref and current version
                if (output == "text")
                    Console.Error.WriteLine(
                        $"Analyzing git history in {repo}..."
                    );

                string lastRef;
                Version currentVersion;
                string displayRef;

                if (tag != null)
                {
                    lastRef = tag;
                    currentVersion = GitAnalyzer.ParseVersion(tag);
                    displayRef = tag;
                }
                else
                {
                    var csprojVersion =
                        await GitAnalyzer.ReadVersionFromCsprojAsync(repo);

                    var versionChangeRef =
                        await GitAnalyzer.TryGetLastVersionChangeRefAsync(repo);

                    if (versionChangeRef != null)
                    {
                        currentVersion = csprojVersion ?? new Version(0, 1, 0);
                        lastRef = versionChangeRef;
                        displayRef =
                            $"{versionChangeRef[..7]} "
                            + "(last .csproj version change)";
                    }
                    else
                    {
                        var latestTag = await GitAnalyzer.TryGetLastTagAsync(repo);
                        if (latestTag != null)
                        {
                            lastRef = latestTag;
                            currentVersion = GitAnalyzer.ParseVersion(latestTag);
                            displayRef = latestTag;
                        }
                        else if (csprojVersion != null)
                        {
                            currentVersion = csprojVersion;
                            lastRef = await GitAnalyzer.GetRootCommitAsync(repo);
                            displayRef = "beginning of repository";
                        }
                        else
                        {
                            // No version anywhere — 0.1.0 is the initial version.
                            // Nothing to analyse; output it directly.
                            var initial = "0.1.0";
                            Console.Error.WriteLine(
                                "No version found. "
                                + $"Using {initial} as the initial version."
                            );
                            switch (output)
                            {
                                case "json":
                                    Console.WriteLine($$"""
                                        {
                                          "current_version": "{{initial}}",
                                          "new_version": "{{initial}}",
                                          "bump": "none",
                                          "reasoning": "No version history found.",
                                          "commits_analyzed": 0,
                                          "base_ref": ""
                                        }
                                        """);
                                    break;

                                case "version-only":
                                    Console.WriteLine(initial);
                                    break;

                                default:
                                    Console.Error.WriteLine();
                                    Console.Error.WriteLine(
                                        $"  Version    : {initial}"
                                    );
                                    Console.WriteLine(initial);
                                    break;
                            }
                            return 0;
                        }
                    }
                }

                var commitCount = await GitAnalyzer
                    .GetCommitCountSinceRefAsync(repo, lastRef);

                if (commitCount == 0)
                {
                    if (output == "text")
                        Console.Error.WriteLine(
                            "No commits found since last ref. "
                            + "Version unchanged."
                        );

                    var currentVersionStr = currentVersion.ToString();

                    switch (output)
                    {
                        case "json":
                            Console.WriteLine($$"""
                                {
                                  "current_version": "{{currentVersionStr}}",
                                  "new_version": "{{currentVersionStr}}",
                                  "bump": "none",
                                  "reasoning": "No commits since last ref.",
                                  "commits_analyzed": 0,
                                  "base_ref": "{{lastRef}}"
                                }
                                """);
                            break;

                        case "version-only":
                            Console.WriteLine(currentVersionStr);
                            break;

                        default:
                            Console.Error.WriteLine();
                            Console.Error.WriteLine(
                                $"  Version    : {currentVersionStr} (unchanged)"
                            );
                            Console.WriteLine(currentVersionStr);
                            break;
                    }

                    return 0;
                }

                if (output == "text")
                {
                    Console.Error.WriteLine(
                        $"Found {commitCount} commits "
                        + $"since {displayRef}"
                    );
                    Console.Error.WriteLine(
                        $"Current version: {currentVersion}"
                    );
                    Console.Error.WriteLine(
                        "Asking Claude Code to analyze changes..."
                    );
                }

                // 2. Analyze with Claude Code CLI
                var result = await analyzer.AnalyzeAsync(
                    repo,
                    lastRef,
                    model
                );

                // 3. Compute new version
                var newVersion = GitAnalyzer.BumpVersion(
                    currentVersion,
                    result.Level
                );

                // 4. Output results
                switch (output)
                {
                    case "json":
                        var reasoning = result.Reasoning
                            .Replace("\"", "\\\"");
                        var bump = result.Level
                            .ToString()
                            .ToLowerInvariant();
                        Console.WriteLine($$"""
                            {
                              "current_version": "{{currentVersion}}",
                              "new_version": "{{newVersion}}",
                              "bump": "{{bump}}",
                              "reasoning": "{{reasoning}}",
                              "commits_analyzed": {{commitCount}},
                              "base_ref": "{{lastRef}}"
                            }
                            """);
                        break;

                    case "version-only":
                        Console.WriteLine(newVersion);
                        break;

                    default:
                        Console.Error.WriteLine();
                        Console.Error.WriteLine(
                            $"  Bump level : "
                            + result.Level
                                .ToString()
                                .ToUpperInvariant()
                        );
                        Console.Error.WriteLine(
                            $"  Reasoning  : {result.Reasoning}"
                        );
                        Console.Error.WriteLine(
                            $"  Version    : "
                            + $"{currentVersion} → {newVersion}"
                        );
                        Console.WriteLine(newVersion);
                        break;
                }

                // 5. Apply if requested
                if (apply)
                {
                    var updatedFiles =
                        await CsprojUpdater.UpdateVersionAsync(
                            repo,
                            newVersion,
                            csproj
                        );

                    if (updatedFiles.Count == 0)
                    {
                        Console.Error.WriteLine(
                            "Warning: No .csproj files with "
                            + "<Version> found to update."
                        );
                    }
                    else
                    {
                        foreach (var file in updatedFiles)
                            Console.Error.WriteLine(
                                $"  Updated: {file}"
                            );
                    }

                    // 6. Git tag if requested
                    if (gitTag)
                    {
                        var tagName = $"v{newVersion}";
                        var psi =
                            new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "git",
                            Arguments = $"tag {tagName}",
                            WorkingDirectory = repo,
                            RedirectStandardError = true,
                            UseShellExecute = false
                        };
                        using var process =
                            System.Diagnostics.Process.Start(psi)!;
                        await process.WaitForExitAsync();

                        if (process.ExitCode == 0)
                            Console.Error.WriteLine(
                                $"  Tagged: {tagName}"
                            );
                        else
                            Console.Error.WriteLine(
                                $"  Failed to create tag {tagName}"
                            );
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        });

        return rootCommand;
    }
}
