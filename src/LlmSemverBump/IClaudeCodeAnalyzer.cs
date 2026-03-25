namespace LlmSemverBump;

public interface IClaudeCodeAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(
        string repoPath,
        string lastRef,
        string? model = null
    );
}
