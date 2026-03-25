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
                // Preserve the original encoding and formatting as much as possible
                await File.WriteAllTextAsync(filePath, doc.ToString());
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not parse {filePath} as XML: {ex.Message}");

            // Fallback to regex replacement
            var updated = VersionRegex().Replace(content, $"<Version>{newVersion}</Version>");
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
}
