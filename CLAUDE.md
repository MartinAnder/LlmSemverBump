# Code Style Rules

## Design Principles

- **Follow SOLID principles** when designing classes and code. Each class should have a single responsibility, be open for extension but closed for modification, follow Liskov substitution, depend on abstractions not concretions, and interfaces should be kept focused.
- **Name classes after what they concretely do, and split when responsibility doesn't fit.** When adding new functionality, ask whether it belongs to the existing class's single responsibility. If it does, add it there. If it does not, create a new class with its own interface. Prefer concrete, behavior-describing names — `PersonaEvaluator`, `TopicSuggester`, `ImageQualityChecker` — over vague, catch-all names like `XyzService` or `XyzManager`.
- **Name methods after what they concretely do from the caller's perspective.** A developer should be able to read only the interface — without looking at the implementation — and understand exactly what each method does. Prefer specific, outcome-describing names over abstract or mechanical ones. Avoid generic verbs like `Complete`, `Process`, `Execute`, or `Handle`. Instead, name what is being produced or performed: `GetChatCompletionAsync`, `EvaluatePersonaAsync`, `SuggestTopicsAsync`.
- **Prefer simple, explicit solutions over clever ones.** Readable and maintainable code is always preferred over terse or clever code.
- **Value business stability over premature optimization.** Do not optimize until there is a measured reason to do so.
- **Value deterministic, idempotent systems.**
- **Fail fast on invalid state.** Validate inputs and invariants as early as possible and throw immediately rather than propagating invalid state through the system. Operations should produce the same result when repeated and should not have unintended side effects.

## Workflow Rules

- **Always enter plan mode before making significant changes.** Use the `EnterPlanMode` tool when asked to implement a non-trivial feature, refactor, or any change that touches multiple files or has architectural implications. Present a plan and wait for approval before executing.
- **No TODOs or pseudocode in committed code.** A feature is not done if TODOs or pseudocode remain. Either implement it fully or remove it.
- **Always build the solution after making changes.** Run `dotnet build` to verify the solution compiles before considering a task done.
- **Always run tests to verify new features.** Do not consider a feature complete without running the test suite.

## C# Conventions

- **Configurable values belong in `appsettings.json`**, read via a typed options class injected with `IOptions<T>`. Do not hardcode configuration values or read them directly from environment variables in business logic.
- **Never use `Console.WriteLine`.** Always use `ILogger<T>` for logging. Inject it via the constructor and use the appropriate log level (`LogInformation`, `LogWarning`, `LogError`, etc.).
- **No empty try-catches.** Never write a try-catch that swallows exceptions silently unless it is explicitly necessary and justified with a comment explaining why.
- **Never default strings to `string.Empty` or `""`**. Prefer `null` for absent values. If a value is required, guard with an exception (e.g. `ArgumentNullException`) rather than silently falling back to an empty string.
- **All concrete service/component implementations must have a corresponding interface.** This ensures dependencies can be mocked in unit tests. Inject the interface, not the concrete type.
- **Maximum line length is 120 characters.** Hard-wrap lines that exceed this limit.

- **Prefer more lines over longer lines.** Break up expressions across multiple lines for readability, especially when chaining method calls with `.`.

  ```csharp
  // Correct
  var result = await _context
      .Articles
      .Where(a => a.Status == ArticleStatus.Published)
      .OrderByDescending(a => a.PublishedAt)
      .ToListAsync();

  // Wrong
  var result = await _context.Articles.Where(a => a.Status == ArticleStatus.Published).OrderByDescending(a => a.PublishedAt).ToListAsync();
  ```

- **Method signatures with multiple parameters must place each parameter on its own line.**

  ```csharp
  // Correct
  Task<Guid> SaveArticleAsync(
      BlogPost post,
      byte[]? headerImage
  );
  
  // Wrong
  Task<Guid> SaveArticleAsync(BlogPost post, byte[]? headerImage);
  ```

- **Ternary expressions must be formatted across 3 lines.** Always place the condition on the first line, the `?` branch on the second, and the `:` branch on the third.

  ```csharp
  var attemptLabel = _maxAttempts > 1
      ? $" (attempt {attempt}/{_maxAttempts})"
      : "";
  ```

- **Prefer early returns over if/else symmetry.** When a branch can return early, do so. Avoid structuring code as `if/else` where both branches set variables and then fall through to a shared return — instead, return directly from the branch and let the default case be the final statement.

  ```csharp
  // Correct
  if (useOverride)
  {
      var data = await FetchDataAsync();
      return existing with { Name = data.Name, Value = data.Value };
  }

  return existing with { Value = newValue };

  // Wrong
  string name;
  if (useOverride)
      name = data.Name;
  else
      name = existing.Name;

  return new MyRecord { Name = name, Value = newValue };
  ```

- **Never pre-declare variables before a conditional block.** Do not declare variables above an `if/else` just to assign them inside each branch. Declare variables at the point of use. If a conditional assigns different values to the same variable, restructure with early returns or inline expressions instead.

- **Use `with` expressions when copying records with minor modifications.** Whenever you write a `new T { ... }` construction with multiple properties, first ask: is there an existing instance of this type already in scope? If yes, use `existing with { Prop = newValue }` to copy and override only what changes. Only use a full `new T { ... }` construction when no existing instance is available. This avoids fragile boilerplate and makes diffs easier to read.

  ```csharp
  // Correct
  return existing with { Name = newName, Value = newValue };

  // Wrong
  return new MyRecord
  {
      Name = newName,
      Value = newValue,
      Id = existing.Id,           // copied verbatim — fragile
      CreatedAt = existing.CreatedAt,
      // ... every other property ...
  };
  ```

- **DTOs and records must use required init properties.** When creating DTO classes or records, always use `required` properties with `init` accessors instead of mutable setters or constructor-only initialization.

  ```csharp
  // Correct
  public class MyDto
  {
      public required string Name { get; init; }
      public required int Value { get; init; }
  }
  
  // Avoid
  public class MyDto
  {
      public string Name { get; set; }
      public int Value { get; set; }
  }
  ```

## Testing

- **All test projects must use xUnit v3** (`xunit.v3` NuGet package, version 3.x). Do not use xUnit v2 or any other test framework.

- **Tests must verify observable behaviour, not implementation details.** A test should assert what the system produces or how it behaves from the outside — return values, state changes, side effects on collaborators — not how it achieves that result internally. Tests that assert on private methods, internal call counts, or internal data structures are fragile and should be avoided.

- **Test names must describe behaviour in plain language.** Use the pattern `when_<context_or_precondition>` for the class name and `it_<expected_observable_outcome>` for the method name. The full name should read as a sentence that a non-developer could understand.

  ```csharp
  // Correct
  public class when_a_draft_post_is_submitted_for_review
  {
      [Fact]
      public void it_appears_in_the_moderation_queue() { ... }

      [Fact]
      public void it_is_no_longer_editable_by_the_author() { ... }
  }

  // Wrong
  public class DraftPostTests
  {
      [Fact]
      public void Submit_CallsRepository_AndSetsStatus() { ... }
  }
  ```

- **Each test verifies one behaviour.** If a test needs to assert multiple unrelated things, split it into separate tests.

- **Structure every test with `// Arrange`, `// Act`, and `// Assert` comments**, each separated by a blank line. Always use the explicit comments — blank lines alone are not sufficient. Do not mix setup and assertions.

  ```csharp
  [Fact]
  public async Task it_returns_the_published_article()
  {
      // Arrange
      var article = _fixture.Build<Article>()
          .With(a => a.Status, ArticleStatus.Draft)
          .Create();

      // Act
      var result = await _sut.PublishAsync(article);

      // Assert
      Assert.Equal(ArticleStatus.Published, result.Status);
  }
  ```

- **Tests must not depend on each other or share mutable state.** Execution order must never matter. Each test must be able to run in isolation and produce the same result regardless of what ran before it.

- **Integration and end-to-end tests must start from a known clean state.** Each test must either clean up after itself or reset the database/environment before starting, so reruns are always reliable.

- **Never test framework code or third-party libraries.** Only test logic you own. Do not write tests that verify that EF Core saves to the database, that ASP.NET routing works, or that a NuGet package behaves as documented.

- **A flaky test is a bug.** A test that sometimes fails must be fixed or deleted immediately — it erodes trust in the entire suite.

### Test data

- **Use `fixture.Build<T>().With(...).Create()` and `fixture.Build<T>().With(...).CreateMany()`** to generate test objects. This approach automatically populates all properties with random values, so newly added properties on DTOs or entities do not require changes to existing tests. Only override properties that are semantically meaningful to the behaviour under test.

  ```csharp
  // Correct — only pin what matters to this test
  var article = _fixture
      .Build<Article>()
      .With(a => a.Status, ArticleStatus.Draft)
      .Create();

  // Wrong — brittle: breaks whenever a new property is added to Article
  var article = new Article
  {
      Id = Guid.NewGuid(),
      Title = "Test",
      Status = ArticleStatus.Draft,
  };
  ```

- **Use [Fortitude](https://github.com/Timmoth/Fortitude) to mock HTTP dependencies** in integration and end-to-end tests. Do not call real external HTTP services from any test.

### Test types and project placement

There are three kinds of test projects, each named after the production project they target:

| Type | Project name | Purpose |
|------|-------------|---------|
| Unit | `ProjectBeingTested.Tests` | Fast, isolated tests with all dependencies mocked |
| Integration | `ProjectBeingTested.IntegrationTests` | Tests that exercise real infrastructure (database, etc.) |
| End-to-end | `ProjectBeingTested.EndToEndTests` | Tests that exercise the full running application |

- **Every API endpoint must have at least one end-to-end test covering the happy path.**
- **Important or complex database queries — including EF Core LINQ queries — must have an integration test** that runs against a real database.

### Tooling per test type

**Unit tests** use AutoFixture, Moq, and AutoMoq to construct objects and mock dependencies:

```csharp
[Theory, AutoMoqData]
public void it_returns_the_expected_result(
    [Frozen] Mock<IMyDependency> dependency,
    MyService sut)
{
    // Arrange
    dependency.Setup(d => d.GetValue()).Returns(42);

    // Act
    var result = sut.DoSomething();

    // Assert
    Assert.Equal(42, result);
}
```

**Integration tests** use TestContainers to spin up real infrastructure for the duration of the test run. Each test class should share a single container instance via a collection fixture to keep startup costs low:

```csharp
[Collection("Database")]
public class when_fetching_published_articles : IAsyncLifetime
{
    private readonly DatabaseFixture _db;
    private readonly Fixture _fixture = new();

    public when_fetching_published_articles(DatabaseFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task it_excludes_draft_articles()
    {
        // Arrange
        var published = _fixture
            .Build<ArticleEntity>()
            .With(a => a.Id, Guid.NewGuid())
            .With(a => a.Status, ArticleStatus.Published)
            .Create();

        var draft = _fixture
            .Build<ArticleEntity>()
            .With(a => a.Id, Guid.NewGuid())
            .With(a => a.Status, ArticleStatus.Draft)
            .Create();

        await _db.Context.Articles.AddRangeAsync(published, draft);
        await _db.Context.SaveChangesAsync();

        // Act
        var result = await _db.Context.GetPublishedArticlesAsync();

        // Assert
        Assert.Contains(result, a => a.Id == published.Id);
        Assert.DoesNotContain(result, a => a.Id == draft.Id);
    }
}
```

**End-to-end tests** depend on the Aspire project that belongs to the production project under test. The Aspire host starts the full application stack, and tests interact with it over real HTTP.

The test fixture must implement `IAsyncLifetime` and expose at minimum a `DistributedApplication` and an `HttpClient`. It is shared across all test classes in the collection via `ICollectionFixture<T>`:

```csharp
public class MyApiFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    public HttpClient HttpClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<AppHost>(args: []);

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        App = await appHost.BuildAsync();

        await App.StartAsync();

        await App.Services
            .GetRequiredService<ResourceNotificationService>()
            .WaitForResourceAsync("my-api", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(30));

        HttpClient = App.CreateHttpClient("my-api");
    }

    public async Task DisposeAsync() => await App.DisposeAsync();
}

[CollectionDefinition(nameof(MyApiCollection))]
public class MyApiCollection : ICollectionFixture<MyApiFixture> { }
```

Test classes reference the collection by name and receive the shared fixture via constructor injection:

```csharp
[Collection(nameof(MyApiCollection))]
public class when_an_article_is_published
{
    private readonly MyApiFixture _fixture;

    public when_an_article_is_published(MyApiFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task it_returns_200_ok()
    {
        // Arrange
        var request = new { Id = Guid.NewGuid() };

        // Act
        var response = await _fixture.HttpClient.PostAsJsonAsync(
            "/articles/publish",
            request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

## Project Structure

- **Production projects** (executables and libraries) go in `src/`.
- **Test projects** go in `test/`.
- **Developer projects** (e.g. Aspire) go in `dev/`.

## Git

- **All created files must be added to git.** After creating new files, stage them with `git add`.
- **The default branch is always called `master`.** Never use `main` as the default branch name.

## CLI / Environment

- **On macOS**, when running CLI commands that involve migrations or end-to-end tests, prefix with `export DOCKER_DEFAULT_PLATFORM=linux/amd64` to allow MSSQL Server to run correctly.

## Entity Framework Core

- **Prefer `.ExecuteUpdateAsync` and `.ExecuteDeleteAsync`** over loading tracked entities and calling `.SaveChangesAsync` for update and delete operations.
- **Entity IDs must be `required Guid` and set explicitly with `Guid.NewGuid()` at the call site.** Never rely on EF Core or the database to generate the ID. This keeps ID assignment explicit, testable, and visible.
  ```csharp
  context.Orders.Add(
      new OrderEntity
      {
          Id = Guid.NewGuid(),
          // ...
      });
  ```

- **Entities must reference other entities by ID, not by navigation properties.** Do not add reference navigation properties (e.g. `public Order Order { get; init; }`) — use the foreign key ID only (e.g. `public required Guid OrderId { get; init; }`).
- **Entity classes must be immutable.** Use `required` properties with `init` accessors. Updates are performed via `.ExecuteUpdateAsync()`, not by mutating tracked entities.
