# gigtime — repository conventions

Apply these to every change in this repository, whether an agent or a person makes it.

## Branches

- **Never commit to `main`.** Every change reaches `main` through a feature branch and a pull request.
- **Open one branch per issue.** Name it `<type>/<issue>-<slug>`, where `<type>` is a commit type from the list below and `<slug>` is a few lowercase words joined by hyphens:
  - `feat/1-scaffold-domain`
  - `fix/12-report-timezone`
  - `docs/7-readme-walkthrough`
- **Branch from the current `origin/main`.** Rebase on it before opening the pull request. Delete the branch after the merge.

## Commits

- **Write every commit message in [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/) form:** `<type>(<scope>): <subject>`.
- **Types:** `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`, `style`, `revert`.
- **Scope (optional):** the area changed — `domain`, `contracts`, `platform`, `server`, `cli`, `tests`, `repo`.
- **Subject:** imperative, lowercase start, no full stop, at most 72 characters including the prefix.
- **Body:** say why, when the subject does not make it obvious.
- **Footer:** `Refs #<issue>` on every commit made for an issue. Mark a breaking change with `!` after the type or scope and a `BREAKING CHANGE:` footer.
- **Keep each commit green:** it builds and its tests pass.

Examples:

- `feat(domain): add task lifecycle decider` — correct.
- `fix(cli): map conflict to exit code 7` — correct.
- `Added the task decider.` — wrong: no type, past tense, full stop.
- `feat: Add Task Decider` — wrong: capitalised subject.

## Code

### Layout and style

- **Group by feature.** Keep an aggregate or decider, its events, its endpoints and its projections in one feature folder. Mirror that folder in the test project.
- **Name each file after its main type.** Keep an aggregate's events together in `<Aggregate>Events.cs`. Split a file by responsibility as it nears 300 lines; never split only to meet a line count.
- **Use file-scoped namespaces, `var`, records for events and DTOs, and collection expressions (`[]`).** Do not use `#region`.
- **Name private fields in bare camelCase, without a leading underscore:** `private readonly ILogger<CatalogueStore> log;`.
- **Comment why, not what.** Put intent on a type's `<summary>`. When a line exists because of a bug or a decision, cite the issue: `// #12: report periods use the client's timezone.`
- **Give every non-obvious package pin or build property a comment with its reason.**

### Handlers and endpoints

- **Write one HTTP endpoint per static class, one class per file.** Never put a read and a write in the same class.
- **Shape each handler as `Load` → `Validate` → `Handle`.** `Load` does all data access. `Validate` returns every refusal, so nothing is appended before a refusal: it returns `EnvelopeResult?`, gigtime's one `IResult`, which writes the failure envelope and its HTTP status, or null to continue. `Handle` is pure: it returns events or the response and does no other I/O.
- **Return a typed body (the `Gigtime.Contracts` envelope) from `Handle`.** Never return `IResult` or `Results.*` from `Handle`. Never return an error object as a tuple member: Wolverine cascades the extra element as a message and writes only the first, so the caller gets a 200 with it, or an empty 404 when it is null.
- **Declare no body parameter on a bodyless `POST`, `PUT`, `PATCH` or `DELETE`.** Mark every concrete complex parameter `[NotBody]` in every method of the chain, including each document `Load` returns. On a write verb, Wolverine reads an unmarked one from the request body before `Load` runs, so a bodyless request gets a 400.
- **Return documents or records from a `Load` tuple, never primitives.** A primitive in `Handle` binds from the query string and the loaded value is discarded. Above three elements, return a named `Loaded…` record.
- **Take the current instant as a `DateTimeOffset now` parameter.** gigtime's clock source (#2) supplies it from the registered `TimeProvider`, ahead of Wolverine's built-in `now`, which reads the wall clock; tests replace the `TimeProvider` to pin the clock. Never call `DateTimeOffset.UtcNow` in a handler. Domain deciders take instants as arguments.
- **Take `ILogger<T> log`, with `T` a type in the feature** (a static endpoint class cannot be a type argument). Log through `[LoggerMessage]` source-generated methods with named placeholders (`"Stopped task {TaskId}"`), never string interpolation. Declare the class `partial` to host them.
- **Regenerate Wolverine code after any handler change:** `rm -rf src/Gigtime.Server/Internal/Generated && dotnet run --project src/Gigtime.Server -- codegen write`. Stale generated code does not compile, so delete it first. Commit the regenerated files in the same commit. Read the generated handler for the route and confirm that each `Handle` argument comes from where you intended.

### Dependencies

- **Add an interface only when a second real implementation exists.** "It might get one" does not count. The exceptions are the platform seams `IPlatformPaths` and `IServiceInstaller`, which exist so Windows and macOS can be added without touching callers.
- **Do not wrap logic a handler can hold in an injected `*Service`.** In order of preference: a compound handler, then a static helper in the feature folder once a third caller needs it, then middleware for cross-cutting concerns. Two copies are cheaper than the wrong abstraction.
- **Keep an injected service only when it owns a DI lifetime, an algorithm, or a contested write.**
- **Never make a dependency optional (`IFoo? foo = null`).** A missing registration fails at startup instead of silently skipping a check.
- **Keep call stacks shallow.** Take dependencies as parameters; never resolve them from a nested `IServiceProvider`.

## Tests

- **Name tests as sentences with underscores:** `Stop_from_paused_closes_no_interval`. Name test classes `<Subject>Tests`.
- **Assert with Shouldly.** Use no mocking library; when a test needs a stand-in, write a small fake. Test the server through Alba against the real host and a temporary SQLite file.
- **Drive a task into a state through the real commands, then act.** Never seed the state the handler reads. When seeding is unavoidable, also assert that the seed matches what the real path produces.
- **Use fixed UTC instants** (`DateTimeOffset.Parse("2026-03-02T09:00:00Z")`) and fresh ids per test. Never depend on the wall clock or on test order.
- **For each persisted projection, prove that replaying the stream equals the stored document.**
- **Cover the issue's acceptance criteria, not only the mechanism.** Test behaviour; do not restate the implementation.
- **Treat a run that executes zero tests as a failure.** Check a `--filter` for typos before calling a run green.

## Working in the code

- **Read the surrounding code and the nearest tests before changing anything.**
- **Make the smallest change that meets the issue.** Raise a refactor in its own issue.
- **Stop and ask when acceptance is ambiguous or cannot be met as written.**
- **Before committing, run:** `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`, and `dotnet run scripts/check-package-age.cs` when packages changed.
- **Report what a check does not prove.** A fake-server CLI test does not prove the Unix socket; a JIT build does not prove the AOT publish.
- **Check the squash subject before asking for a merge:** `gh pr view <n> --json title,commits`. On a single-commit PR, GitHub uses the commit subject.
