namespace SemverBump;

public interface IClaudeCodeAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(
        string repoPath,
        string lastTag,
        string? model = null
    );
}
