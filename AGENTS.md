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
- **Types:** `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`, `style`, `revert`, and `deps` for a commit that only adds, upgrades or removes packages.
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

## Packages

- **Restore from nuget.org only.** `nuget.config` clears every other source; never add a feed.
- **Pin exact versions in `Directory.Packages.props`.** Project files carry `PackageReference` items without versions.
- **Never use a package version published less than 24 hours ago.** After every package change, run `dotnet run scripts/check-package-age.cs` from the repo root; it fails on a version younger than 24 hours or missing from nuget.org.
- **Add or upgrade with `dotnet add <project> package <id> --version <version>`,** then run the age check, and commit the change on its own with a `deps:` subject, including the updated `packages.lock.json` files.
- **Restore with `dotnet restore --locked-mode`.** Commit every `packages.lock.json`; CI refuses a restore that would change one.
- **Do not change `nuget.config`, `Directory.Build.props`, `.editorconfig`, the scripts in `scripts/` or the age threshold in a feature branch.** Change them in their own pull request.

## Code

### Design

gigtime follows [CUPID](https://cupid.dev/) rather than SOLID. Each property below names the check that makes it concrete; the sections after this one hold the detail. Reviewers judge every change against all five.

- **Composable — plays well with others.** Keep public surfaces small and intention-revealing: expose what a caller needs, nothing more. Take dependencies as parameters. Add no interface, base class or `*Service` before a second real use exists (see Dependencies).
- **Unix philosophy — does one thing well.** One endpoint per class, one responsibility per type, one reason to change per file. If a type's name needs "And" or a summary needs two sentences, split it.
- **Predictable — does what you expect.** Deciders and `Handle` methods are pure: same inputs, same result, no hidden I/O. Instants arrive as parameters. A method's name states every effect it has; a getter never writes, and a `Validate` never appends. Refusals are values, not exceptions.
- **Idiomatic — feels natural.** Write current C# and follow `.editorconfig`, Formatting and the Wolverine shapes below. When the language or a library has an established way, use it rather than a home-grown one.
- **Domain-based — models the problem in language and structure.** Name types, members and folders in the time-tracking domain: task, interval, client, project, rate, report. Never `Manager`, `Helper`, `Utils`, `Processor` or `Data`. Folders are features, not layers (`Tasks/`, not `Services/`).

### Layout and style

- **Group by feature.** Keep an aggregate or decider, its events, its endpoints and its projections in one feature folder. Mirror that folder in the test project.
- **Give each type its own file, named after it, unless it belongs to a group below.** Split a file by responsibility as it nears 300 lines; never split only to meet a line count.
- **Group small types in one file when they are read together.** A group holds at most one type with behaviour; the rest are records, enums or one-line interfaces. Four groups are allowed:
  - *Family:* a closed set of one-line records that one feature owns, in a file named for the set: an aggregate's commands with their base record (`TaskCommand.cs`), its events (`TaskEvents.cs`), or one resource's requests (`TaskRequests.cs`) or response DTOs (`TaskDtos.cs`).
  - *Arity:* a type and its generic forms, in the file named after the non-generic type: `Change.cs` holds `Change` and `Change<T>`.
  - *Satellite:* a main type with the small records and enums that only it uses, declared below it, in `<MainType>.cs`. `TaskView.cs` may hold `TaskView`, `TaskState` and `Interval`.
  - *Pair:* an interface and its one implementation, in the interface's file.
- **Move a type into its own file** once code outside the feature uses it, or once it gains a member body longer than one line.
- **Use file-scoped namespaces, `var`, records for events and DTOs, and collection expressions (`[]`).** Do not use `#region`.
- **Name private fields in bare camelCase, without a leading underscore:** `private readonly ILogger<CatalogueStore> log;`. Name constants in PascalCase. End every method that returns `Task` or `ValueTask` in `Async`, including Wolverine's `HandleAsync`, `LoadAsync` and `ValidateAsync`; test methods are exempt. The build enforces the suffix on methods marked `async`.
- **Comment why, not what.** Put a type's intent in a `<summary>` only when its name does not make it obvious. When a line exists because of a bug or a decision, cite the issue: `// #12: report periods use the client's timezone.`
- **Give every non-obvious package pin or build property a comment with its reason.**

### Formatting

`.editorconfig` is the source of truth; the build fails on most of it. These rules cover what it cannot express.

- **Indent with tabs.** `dotnet run scripts/check-indentation.cs` fails on a line that starts with a space; `dotnet format` does not catch it.
- **Put braces on their own line (Allman), and one statement on each line.** Never join statements with `;` on one line: `Id = e.TaskId; State = TaskState.Running;` is wrong. The build does not catch this; Rider's reformat splits them.
- **Brace every body that spans more than one line.** A single-statement `if` or `case` body may stay on its line, unbraced, when it reads as one thought: `if (!exists) return NotFound(id);`, `case Paused: return "gig resume";`. Otherwise put it on the next line, indented.
- **Expression-bodied members only when they fit on one line.** Properties, accessors and one-line helpers use `=>`; constructors never do; a method with more than one statement uses a block.
- **Write out every access modifier; never qualify with `this.`.**
- **Use primary constructors for injected dependencies.**
- **Break long lines before they reach 140 columns.** Chains go one call per line with the dot leading; ternaries break before `?` and `:`; long parameter lists go one per line. Multi-line initialisers and collections end with a trailing comma.
- **Order members:** fields and properties, constructors, public methods, then private helpers below their first caller.
- **Prefer `switch` expressions for mapping a value; use a `switch` statement for side effects.**
- **Check null with patterns:** `is null`, `is not null`, `is { } value`; never `== null` or `!= null`.
- **Format each changed file in this order before committing:** Rider's reformat (`reformat_file` through its MCP server, when you have Rider), then `dotnet format`, then Rider's `lint_files`. Rider lays out lines and wraps; `dotnet format` adds what reformat does not, such as braces on a multi-line body. The build's verdict wins: when they disagree, `dotnet format` is right.

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
- **Before committing, run:** `dotnet build`, `dotnet format --verify-no-changes`, `dotnet run scripts/check-indentation.cs`, `dotnet test`, and `dotnet run scripts/check-package-age.cs` when packages changed.
- **Report what a check does not prove.** A fake-server CLI test does not prove the Unix socket; a JIT build does not prove the AOT publish.
- **Check the squash subject before asking for a merge:** `gh pr view <n> --json title,commits`. On a single-commit PR, GitHub uses the commit subject.
