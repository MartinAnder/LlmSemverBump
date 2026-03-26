using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LlmSemverBump;

public static partial class CsprojUpdater
{
    /// <summary>
    /// Updates the Version element in all .csproj files found in the repo,
    /// or in a specific .csproj if provided.
    /// </summary>
    public static async Task<List<string>> UpdateVersionAsync(
        string repoPath, string newVersion, string? specificCsproj = null)
    {
        var updated = new List<string>();

        var csprojFiles = specificCsproj is not null
            ? [specificCsproj]
            : Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories);

        foreach (var file in csprojFiles)
        {
            if (await TryUpdateCsprojAsync(file, newVersion))
            {
                updated.Add(file);
            }
        }

        return updated;
    }

    private static async Task<bool> TryUpdateCsprojAsync(string filePath, string newVersion)
    {
        var content = await File.ReadAllTextAsync(filePath);

        var isPackable = content.Contains(
            "<IsPackable>true</IsPackable>",
            StringComparison.OrdinalIgnoreCase);
        var isPackAsTool = content.Contains(
            "<PackAsTool>true</PackAsTool>",
            StringComparison.OrdinalIgnoreCase);

        if (!isPackable && !isPackAsTool)
            return false;

        // Check if the file has a <Version> element
        if (!content.Contains("<Version>"))
        {
            // Check if it has a <PackageVersion> instead
            if (!content.Contains("<PackageVersion>"))
                return false;
        }

        try
        {
            var doc = XDocument.Parse(content);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var changed = false;

            // Update <Version> elements
            foreach (var element in doc.Descendants(ns + "Version"))
            {
                // Only update Version in PropertyGroup, not in PackageReference
                if (element.Parent?.Name.LocalName == "PropertyGroup")
                {
                    element.Value = newVersion;
                    changed = true;
                }
            }

            // Also update <PackageVersion> if present (used in some project setups)
            foreach (var element in doc.Descendants(ns + "PackageVersion"))
            {
                if (element.Parent?.Name.LocalName == "PropertyGroup")
                {
                    element.Value = newVersion;
                    changed = true;
                }
            }

            if (changed)
            {
                // Replace only the version values in the original content to preserve all
                // formatting, indentation, and empty lines that XDocument.ToString() would strip.
                var result = VersionRegex().Replace(content, $"<Version>{newVersion}</Version>");
                result = PackageVersionRegex().Replace(result, $"<PackageVersion>{newVersion}</PackageVersion>");
                await File.WriteAllTextAsync(filePath, result);
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not parse {filePath} as XML: {ex.Message}");

            // Fallback to regex replacement
            var updated = VersionRegex().Replace(content, $"<Version>{newVersion}</Version>");
            updated = PackageVersionRegex().Replace(updated, $"<PackageVersion>{newVersion}</PackageVersion>");
            if (updated != content)
            {
                await File.WriteAllTextAsync(filePath, updated);
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"<Version>[^<]+</Version>")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"<PackageVersion>[^<]+</PackageVersion>")]
    private static partial Regex PackageVersionRegex();
}
