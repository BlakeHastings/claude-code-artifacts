# C# / xUnit cheatsheet

Distilled, sourced conventions for the tests these sub-agents write. Hand the relevant bits to the agent
in its brief; keep the citations here for when a choice is questioned.

## Naming

- Three parts: **method/unit, scenario/state, expected behavior** — `Add_SingleNumber_ReturnsSameNumber`.
  This is Microsoft's recommended scheme; tests double as documentation, so a reader infers behavior
  without reading the implementation.
- Behavior-first variants (`Should_ReturnZero_When_InputEmpty`, Given/When/Then) read more like sentences
  and survive renames of the production method better. The scheme matters less than picking one and keeping
  all three pieces of information. Match the project's existing style.
- Source: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices ,
  https://osherove.com/blog/2005/4/3/naming-standards-for-unit-tests.html

## Structure

- **Arrange / Act / Assert**, one **Act** per test. Separate the phases; do not interleave assertions with
  the action.
- **No logic in tests** — no `if`/`for`/`while`/`switch`/string-concat. A bug in a test is the worst place
  for a bug. If you need variation, use `[Theory]`.
- **One logical assert** per test so a failure names the cause unambiguously.
- Promote magic literals to named constants.
- Source: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices

## FIRST characteristics

Fast, Isolated, Repeatable, Self-checking, Timely. (FIRST is the popular practitioner mnemonic; Microsoft's
page lists the same traits but names the fourth "Self-Checking" and does not use the acronym.) Isolated = no
file system, DB, network, clock, or other process state in a unit test.
Source: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices

## Test doubles (the one rule that matters)

- **Fake** = generic stand-in. **Stub** = supplies data, you do NOT assert against it. **Mock** = a double
  you run `Assert` against (it decides pass/fail). The distinguishing rule: **you assert against a mock,
  never against a stub.** Name doubles generically (`FakeOrder`), not `MockOrder`.
- Prefer fakes/stubs over deep mock hierarchies. Mocking everything means you test the mocks, not the code.
- For nondeterministic globals (`DateTime.Now`, randomness, env), introduce a **seam** (an interface you
  inject) and stub it, rather than reading the global inside the SUT.
- **For time specifically (.NET 8+), prefer the built-in `TimeProvider`** over a hand-rolled clock interface:
  inject `TimeProvider` into the SUT, use `TimeProvider.System` in production, and fake it in tests with
  `FakeTimeProvider` from the `Microsoft.Extensions.TimeProvider.Testing` package (set/advance time
  manually). It also covers `Task.Delay`/`CancellationTokenSource` timeouts. This is the Microsoft-blessed
  time abstraction; reach for a custom interface only on older targets (where `Microsoft.Bcl.TimeProvider`
  backports it to .NET Framework 4.6.2+ / netstandard2.0).
- Sources: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices ,
  https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview

## Assertion library (licensing guardrail)

- Default to xUnit's built-in `Assert`. It is free, always available, and enough for contract tests.
- **Do not reflexively add FluentAssertions.** As of **v8 (Jan 2025)** FluentAssertions requires a paid Xceed
  commercial license (~$130/dev/yr) for commercial use; only **v7.x stays Apache-2.0** and free. An LLM author
  pulling in the latest FluentAssertions silently introduces a paid dependency.
- If a fluent style is genuinely wanted, use a free, Apache-2.0 option: **AwesomeAssertions** (community fork of
  FA v7, API-compatible, near-zero migration) or **Shouldly**. Pin FluentAssertions to `7.*` only if already
  committed to it.
- Sources: https://www.infoq.com/news/2025/01/fluent-assertions-v8-license/ ,
  https://github.com/AwesomeAssertions/AwesomeAssertions

## Lifecycle and fixtures (xUnit)

- xUnit creates **a new instance of the test class per test**. Put setup in the **constructor**, teardown in
  **`Dispose()`** (`IDisposable`). xUnit has no `[SetUp]`/`[TearDown]`.
- `IClassFixture<T>` — one shared instance across all tests in **one** class (expensive shared context).
- `ICollectionFixture<T>` + `[CollectionDefinition("name")]` + `[Collection("name")]` — one shared instance
  across **multiple** classes. Collection definitions must live in the same assembly as the tests.
- `[Fact]` = invariant; `[Theory]` + `[InlineData]` (compile-time constants) / `[MemberData]` (computed/
  shared) / `[ClassData]` (reusable `IEnumerable<object[]>`). `TheoryData<>` is the typed form.
- Sources: https://xunit.net/docs/shared-context ,
  https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-csharp-with-xunit

## Parallelism and global state (the trap this workflow surfaces)

- The unit of parallelism is the **test collection**. By default each test class is its own collection, so
  **different classes run in parallel**, while **tests within one class run sequentially**.
- Therefore: two classes that mutate the same process-global state (an environment variable, current
  directory, a static field, console redirection, a shared file path) will **race** and fail
  nondeterministically.
- Fix: put those classes in **one shared collection** and disable its parallelism:
  ```csharp
  [CollectionDefinition("ProcessGlobalState", DisableParallelization = true)]
  public sealed class ProcessGlobalStateCollection { public const string Name = "ProcessGlobalState"; }

  [Collection(ProcessGlobalStateCollection.Name)]
  public sealed class SomeEnvTouchingTests { /* ... */ }
  ```
  Same `[Collection]` → sequential relative to each other; `DisableParallelization` → also not run alongside
  other collections. Always save and restore the original global value in `Dispose()`.
- Better still, where you can: inject the value behind a seam so the SUT never reads the global at all.
- Sources: https://xunit.net/docs/running-tests-in-parallel , https://xunit.net/docs/shared-context

## Isolation recipe (disk/env)

Give each test its own temp dir and restore any env override:
```csharp
public Tests()
{
    _dir = Path.Combine(Path.GetTempPath(), "myunit-" + Guid.NewGuid().ToString("N"));
    _prev = Environment.GetEnvironmentVariable(EnvVar);
    Environment.SetEnvironmentVariable(EnvVar, _dir);
}
public void Dispose()
{
    Environment.SetEnvironmentVariable(EnvVar, _prev);
    try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
}
```

## The abstract / contract-test pattern (one suite, every implementation)

When more than one type implements the interface (e.g. the real service and a fake), write the contract
tests once against the interface and run them against each implementation via a factory:
- An abstract test base holds the tests, written purely against the interface, and declares an abstract
  `CreateSut()` (Template Method + Factory Method). A subclass per implementation overrides `CreateSut()`.
- Or use parameterized tests fed by each implementation. Either way the **fake is held to the same
  contract** as the real type, so it is a faithful stand-in, not a fiction.
- Sources: https://testingpatterns.net/patterns/abstract_test/ ,
  https://www.baeldung.com/java-junit-verify-interface-contract

## Correct vs current

Contract tests assert the **documented** behavior. If no spec exists and you are only pinning what the code
*currently* does (bugs included), that is a **characterization / golden-master** test — a change-detector
safety net, not a correctness oracle. Keep the two categories separate and labeled.
Source: https://en.wikipedia.org/wiki/Characterization_test

## LLM-written-test failure modes (why the blind-author + ledger discipline exists)

- **Behavior-pinning / tautological:** tests generated *from the code* bake its bugs into the assertions.
  Independent, contract-only authoring is the documented mitigation.
- **Coverage lies:** LLM suites often reach high line coverage but kill few mutants (~40% mutation scores
  reported). Use mutation testing (Stryker.NET) as the real gate.
- **Hallucinated APIs:** fabricated symbols cause most compile failures; pasting exact signatures into the
  brief removes the need to guess.
- **Vacuous/over-mocked:** asserting "did not throw" or pinning call order. Assert observable results.
- Sources: https://arxiv.org/pdf/2312.13010 (AgentCoder: independent test designer beats one agent doing
  both), https://arxiv.org/html/2406.18181v1 , https://stryker-mutator.io/docs/stryker-net/introduction/
