using System.CommandLine;
using LlmSemverBump;

return await new CommandFactory(new ClaudeCodeAnalyzer()).Build()
    .Parse(args)
    .InvokeAsync(null, CancellationToken.None);
