using System.CommandLine;
using LlmSemverBump;

return await new CommandFactory(new ClaudeCodeAnalyzer()).Build().InvokeAsync(args);
