# semver-bump

AI-powered semantic versioning for .NET NuGet packages. Analyzes your git history using Claude to determine the correct semver bump.

## How It Works

1. Finds the latest git tag as the baseline version
2. Gathers all commits and code diffs since that tag
3. Sends the changes to Claude, which analyzes the **public API surface**
4. Returns `major`, `minor`, or `patch` with reasoning

Claude examines the actual code — not just commit messages — so it catches things like removed public methods, new interfaces, renamed properties, and other API surface changes that conventional-commits-based tools would miss.

## Installation

```bash
# As a global tool
dotnet tool install -g SemverBump

# As a local tool (per-repo)
dotnet new tool-manifest  # if you don't have one yet
dotnet tool install SemverBump
```

## Prerequisites

- .NET 8.0 SDK or later
- Git
- An Anthropic API key (set as `ANTHROPIC_API_KEY` environment variable)

## Usage

### Dry Run (default)

```bash
# Analyze the current repo
semver-bump

# Analyze a specific repo
semver-bump --repo /path/to/repo
```

Output goes to **stderr** (reasoning, context) and **stdout** (just the version), so you can easily capture the version in scripts:

```bash
NEW_VERSION=$(semver-bump)
```

### Apply Changes

```bash
# Update all .csproj files and create a git tag
semver-bump --apply --git-tag

# Update a specific .csproj only
semver-bump --apply --csproj src/MyLib/MyLib.csproj
```

### Output Formats

```bash
# JSON (for CI integration)
semver-bump --output json

# Version only (no reasoning on stderr)
semver-bump --output version-only

# Default text (reasoning on stderr, version on stdout)
semver-bump --output text
```

### Options

| Flag | Short | Description                                                      |
|------|-------|------------------------------------------------------------------|
| `--repo` | `-r` | Path to git repository (default: current directory)              |
| `--tag` | `-t` | Override base tag (default: latest via `git describe`)           |
| `--csproj` | `-c` | Specific .csproj to update                                       |
| `--apply` | `-a` | Apply version bump to .csproj files                              |
| `--git-tag` | | Create a git tag after applying                                  |
| `--model` | `-m` | Claude model to use (default depends on your Claude Code config) |
| `--output` | `-o` | Output format: `text`, `json`, `version-only`                    |

## GitHub Actions Example

```yaml
name: Version Bump on Push to Main

on:
  push:
    branches: [main]

jobs:
  version:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0  # Full history needed for git describe

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Install semver-bump
        run: dotnet tool install -g SemverBump

      - name: Bump version
        env:
          ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}
        run: |
          semver-bump --apply --git-tag
          git push --tags
          git push

      - name: Pack & publish
        run: |
          dotnet pack -c Release
          dotnet nuget push **/*.nupkg --source nuget.org --api-key ${{ secrets.NUGET_KEY }}
```

## How Claude Decides

The prompt instructs Claude to follow standard semver rules for .NET libraries:

- **MAJOR**: Removed/renamed public types or members, changed method signatures, interface member additions (breaks implementors), namespace restructuring
- **MINOR**: New public types/methods/properties, new optional parameters with defaults, new enum values, backwards-compatible features
- **PATCH**: Bug fixes, internal refactoring, docs, tests, dependency updates, performance improvements

Claude sees both the **commit messages** and the **actual code diff**, focusing on the public API surface (classes, interfaces, methods, properties with `public`/`protected` visibility). For very large diffs, the tool automatically summarizes to only public API changes to stay within token limits.

## Tips

- **Tag your initial version first**: The tool needs at least one existing tag. Run `git tag v0.1.0` before first use.
- **Use Sonnet for cost efficiency**: The default model (`claude-sonnet-4-20250514`) balances quality and cost well. Use `--model claude-opus-4-6` for complex repos.
- **Dry run in CI**: Run without `--apply` first and inspect the JSON output before committing to automated bumps.

## License

MIT
