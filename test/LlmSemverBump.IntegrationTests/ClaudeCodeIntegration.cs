using Xunit;

namespace LlmSemverBump.IntegrationTests;

[CollectionDefinition(nameof(ClaudeCodeIntegration))]
public class ClaudeCodeIntegration : ICollectionFixture<object> { }