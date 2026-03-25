using System.CommandLine;
using SemverBump;

var repoOption = new Option<string>(
    aliases: ["--repo", "-r"],
    description: "Path to the git repository",
    getDefaultValue: () => Directory.GetCurrentDirectory());

var tagOption = new Option<string?>(
    aliases: ["--tag", "-t"],
    description: "Override the base tag (default: latest tag via git describe)");

var csprojOption = new Option<string?>(
    aliases: ["--csproj", "-c"],
    description: "Path to a specific .csproj to update (default: all .csproj files with <Version>)");

var applyOption = new Option<bool>(
    aliases: ["--apply", "-a"],
    description: "Apply the version bump to .csproj files (default: dry run)");

var gitTagOption = new Option<bool>(
    aliases: ["--git-tag"],
    description: "Create a git tag with the new version after applying");

var modelOption = new Option<string?>(
    aliases: ["--model", "-m"],
    description: "Claude model to use (default: claude-sonnet-4-20250514)");

var outputOption = new Option<string>(
    aliases: ["--output", "-o"],
    description: "Output format: text, json, or version-only",
    getDefaultValue: () => "text");

var rootCommand = new RootCommand(
    "AI-powered semantic version bumping. Analyzes git history with Claude to determine the correct semver bump.")
{
    repoOption,
    tagOption,
    csprojOption,
    applyOption,
    gitTagOption,
    modelOption,
    outputOption,
};

rootCommand.SetHandler(async (repo, tag, csproj, apply, gitTag, model, output) =>
{
    try
    {
        // 1. Gather git context
        if (output == "text")
            Console.Error.WriteLine($"Analyzing git history in {repo}...");

        var context = await GitAnalyzer.GatherContextAsync(repo, tag);

        if (context.CommitCount == 0)
        {
            Console.Error.WriteLine("No commits found since last tag. Nothing to bump.");
            Environment.ExitCode = 0;
            return;
        }

        if (output == "text")
        {
            Console.Error.WriteLine($"Found {context.CommitCount} commits since {context.LastTag}");
            Console.Error.WriteLine($"Current version: {context.CurrentVersion}");
            Console.Error.WriteLine("Asking Claude to analyze changes...");
        }

        // 2. Analyze with Claude
        var analyzer = new VersionAnalyzer(model: model);
        var result = await analyzer.AnalyzeAsync(context);

        // 3. Compute new version
        var newVersion = GitAnalyzer.BumpVersion(context.CurrentVersion, result.Level);

        // 4. Output results
        switch (output)
        {
            case "json":
                Console.WriteLine($$"""
                    {
                      "current_version": "{{context.CurrentVersion}}",
                      "new_version": "{{newVersion}}",
                      "bump": "{{result.Level.ToString().ToLowerInvariant()}}",
                      "reasoning": "{{result.Reasoning.Replace("\"", "\\\"")}}",
                      "commits_analyzed": {{context.CommitCount}},
                      "base_tag": "{{context.LastTag}}"
                    }
                    """);
                break;

            case "version-only":
                Console.WriteLine(newVersion);
                break;

            default: // text
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  Bump level : {result.Level.ToString().ToUpperInvariant()}");
                Console.Error.WriteLine($"  Reasoning  : {result.Reasoning}");
                Console.Error.WriteLine($"  Version    : {context.CurrentVersion} → {newVersion}");
                Console.WriteLine(newVersion); // stdout gets just the version
                break;
        }

        // 5. Apply if requested
        if (apply)
        {
            var updatedFiles = await CsprojUpdater.UpdateVersionAsync(repo, newVersion, csproj);

            if (updatedFiles.Count == 0)
            {
                Console.Error.WriteLine("Warning: No .csproj files with <Version> found to update.");
            }
            else
            {
                foreach (var file in updatedFiles)
                    Console.Error.WriteLine($"  Updated: {file}");
            }

            // 6. Git tag if requested
            if (gitTag)
            {
                var tagName = $"v{newVersion}";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"tag {tagName}",
                    WorkingDirectory = repo,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var process = System.Diagnostics.Process.Start(psi)!;
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                    Console.Error.WriteLine($"  Tagged: {tagName}");
                else
                    Console.Error.WriteLine($"  Failed to create tag {tagName}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Environment.ExitCode = 1;
    }
},
repoOption, tagOption, csprojOption, applyOption, gitTagOption, modelOption, outputOption);

return await rootCommand.InvokeAsync(args);
