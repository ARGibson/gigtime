# gigtime

Time tracking for people who bill by the hour, without leaving the terminal.

Type `gig start acme web` when you sit down, `gig stop` when you're done, and gigtime keeps every minute against the right client and project. `gig timesheet` shows when you worked; `gig report` totals the figures you submitted.

```console
$ gig start acme web --ref sprint-12
$ gig pause        # lunch
$ gig resume
$ gig stop --submit
$ gig report --week
  Client   Project   Currency   Raw        Rounded    Amount
  acme     web       GBP        03:40:00   03:40:00   146.67
```

## Why gigtime

Starting and stopping has to be instant, or you stop doing it. `gig` is a native binary that starts in about 7 ms, and zsh completion fills in your clients, projects and recent task ids from your own data.

It does the arithmetic the way an invoice needs it. Each client has a currency and timezone, and each project an hourly rate. A task's total is rounded once, to the nearest minute, and submission fixes the task's rate and rules, so raising your rate later doesn't rewrite a submitted figure. Until then, the figures stay provisional.

Mistakes are cheap to fix. Forgot to stop? `gig amend` corrects the times. Forgot to start? `gig add` backfills the task. Logged it to the wrong project? Reassign it. Every change is recorded as a new event rather than overwriting the old one, so the history stays honest.

Reports follow your clients, not your clock: weeks and months are counted in each client's timezone, from the week start you choose, and amounts in different currencies are never added together.

Your data stays on your machine. A small local service owns one SQLite file behind a private Unix socket. There's no network listener, no account and no sync, and `gig export` writes the whole history as JSON Lines whenever you want a backup.

Scripts and agents can drive it too. Every command takes `--json` and prints one predictable envelope, with exit codes you can branch on.

gigtime runs on Linux, with systemd user services.

## Data boundary

gigtime stores hours, client and project keys and names, rates and opaque refs. It never
stores task content. A `ref` must never carry task material: use an unrelated opaque
identifier, never a task description, prompt, response or rubric. Examples here use
fictional clients Acme and Globex. Treat exports as private financial records.

## Install

Download the Linux x64 archive and checksums from the
[latest release](https://github.com/ARGibson/gigtime/releases/latest):

```sh
version=0.1.0
base="https://github.com/ARGibson/gigtime/releases/download/v$version"
curl --fail --location --remote-name "$base/gigtime-$version-linux-x64.tar.gz"
curl --fail --location --remote-name "$base/SHA256SUMS"
sha256sum -c --ignore-missing SHA256SUMS
tar -xzf "gigtime-$version-linux-x64.tar.gz"
cd "gigtime-$version-linux-x64"
./install.sh --from .
```

Choose the version shown on the release page. Upgrading uses the same steps with the
newer archive. The bundled installer runs `gig setup`, switches the installed release,
and checks health. Add `~/.local/bin` to `PATH` if it reports a warning.

Requirements: Linux x64 with systemd user services, glibc **2.34 or newer**, and the
**ASP.NET Core Runtime 10** (which includes the .NET runtime) for `gigtimed`. The native
`gig` needs no .NET runtime. The ASP.NET Core runtime must be discoverable by the
systemd user unit: install it system-wide, or register its directory in
`/etc/dotnet/install_location`. An unregistered `~/.dotnet` install without `DOTNET_ROOT` in the unit
isn't found. The release binaries require glibc 2.34, measured from their imported
`GLIBC_*` symbols. Alpine/musl is not supported by this archive.

## Build from source

From a checkout, first installs and upgrades use the same command:

```sh
scripts/install.sh
```

The script builds a native `gig` and the service into `~/.local/lib/gigtime/releases/`,
checks the new binary, switches the relative `current` link atomically, and links
`~/.local/bin/gig` to `current/gig`. It runs setup, requests a restart of an already-running
service, and polls health for up to ten seconds. Add `~/.local/bin` to `PATH` if the script warns about it.
A clean commit already installed is a no-op; `--force` rebuilds or restages, then reruns
setup, restart and health checking. Existing release folders are never replaced: identical files reuse
the folder, while different files for an installed label get a UTC timestamp suffix.
Uncommitted changes always get a commit-and-UTC-timestamp label. Commit labels use
seven characters on both install paths. Three releases are retained for rollback,
plus any older release a running daemon uses, even if the health check fails.
Installing a retained release switches `current` back to it without changing its files.

```sh
scripts/install.sh --prefix "$HOME/apps/gigtime" --bin-dir "$HOME/bin"
scripts/install.sh --from /path/to/published-pair
scripts/install.sh --force
```

`--from` copies the entire published directory, including the service's dependency files,
and uses seven characters of the native binary's version commit suffix as its label
(or a UTC timestamp if there is no suffix). No SDK build is needed in that mode; the service still requires
a compatible .NET/ASP.NET Core runtime. Install paths cannot be inside the
`$XDG_DATA_HOME/gigtime` data directory. The SDK may write its own NuGet cache under
`$XDG_DATA_HOME`. A failed build or version check leaves the current install intact. A
health failure after switching exits non-zero and retains the new release so its error
can be investigated. Setup keeps the service executable path at `current/gigtimed`, so
unit content stays the same across upgrades.

To roll back, choose a retained release label and switch the link, then restart and check
health (replace the root below when using `--prefix`):

```sh
root="$HOME/.local/lib/gigtime"
ln -sfn "releases/<label>" "$root/current"
systemctl --user try-restart gigtimed.service
timeout 10s bash -c 'until "$1" setup --check; do sleep 0.25; done' bash "$root/current/gig"
```

The install script changes only install files, links and user units; it never writes the
`$XDG_DATA_HOME/gigtime` data directory. Database compatibility between releases is outside its scope.

For a manual build and install:

Install the .NET SDK selected by `global.json`. Native publishing also requires the
platform's C toolchain (clang and zlib development headers on Linux). Restore only from
the repository's configured nuget.org source:

```sh
dotnet restore --locked-mode
dotnet build --no-restore
dotnet publish src/Gigtime.Cli -c Release -r linux-x64 -p:PublishAot=true --no-restore -o artifacts/install
dotnet publish src/Gigtime.Server -c Release -r linux-x64 --self-contained false --no-restore -o artifacts/install
```

Keep the entire install directory, including the service's DLLs and dependency files.
Add it to `PATH`, then run `gig setup`. The installed `gig` resolves its directory through
`/proc/self/exe` and expects an executable `gigtimed` beside it. `gig setup --gigtimed
/absolute/path/gigtimed` overrides that location. Daemon paths containing `"`, `\` or `$` are refused; spaces and `%` are supported.
A missing/non-executable daemon exits
with `config` (5), naming the path.

Setup writes `gigtimed.service` and `gigtimed.socket` under
`$XDG_CONFIG_HOME/systemd/user` (default `~/.config/systemd/user`). It enables and starts
the socket with `systemctl --user`. Units with identical content stay unchanged. If
socket activation cannot be enabled, setup installs a service enabled at login and
reports that fallback. Repeated setup retains that mode; removing the fallback service
unit before another setup allows a fresh socket-activation attempt.

`gig setup --check` reports unit paths and presence, the socket path/presence, database
path and the result of `/v1/health`. It writes no units and invokes no systemctl command.
It returns 6 if health is unavailable; its JSON still contains the successful inspection
with `healthy: false`. A successful setup reports installation even when a replacement
service manager cannot provide health yet.

Storage is `$XDG_DATA_HOME/gigtime/gigtime.db` (default
`~/.local/share/gigtime/gigtime.db`). The socket is
`$XDG_RUNTIME_DIR/gigtime/gigtimed.sock`. Without an absolute runtime root, gigtime uses
a private `gigtime-<username>` directory under the temporary directory. Existing fallback
directories must belong to the user and have mode 0700. The socket and database are
private to the user. `--check` and live completion don't create an absent fallback root.

## Instances

Set `GIGTIME_INSTANCE=dev` for a separate development instance, including commands
run by agents. Names match `^[a-z][a-z0-9-]{0,31}$`; invalid values fail with `config`
(exit 5) before creating files. Unset or empty selects the default instance.

For `<name>`, data lives in `$XDG_DATA_HOME/gigtime-<name>/gigtime.db` and the socket
in `$XDG_RUNTIME_DIR/gigtime-<name>/gigtimed.sock`. The private runtime fallback is
`gigtime-<username>-<name>` under the temporary directory, containing the instance's
`gigtime-<name>/gigtimed.sock`. The XDG defaults above still apply.

`bash scripts/install.sh --instance dev` installs shared binaries and sets up
`gigtimed-dev.service` and `gigtimed-dev.socket`. The service remembers its instance.
The script uses only `--instance`, ignoring inherited `GIGTIME_INSTANCE`; without
the flag it installs the default instance.
A development build installed with `--instance` still moves the shared `current` and
`gig` link, so use a separate `--prefix` and `--bin-dir` for development builds.

Subsequent commands select an instance:

```sh
GIGTIME_INSTANCE=dev gig status
GIGTIME_INSTANCE=dev gig setup --check
```

Human status and setup output identifies a named instance with `Instance: dev`.
Their JSON data includes `instance`, a string for a named instance or `null` for
the default. Each instance keeps its own catalogue and tasks.

## A working day

```sh
gig setup
gig defaults set --timezone Europe/London --week-starts Monday --rounding nearest-minute
gig client add acme --name Acme --currency GBP
gig project add acme work --name Work --rate 40
gig start acme work --ref opaque-42
gig pause
gig resume
gig timesheet --week
gig stop --submit
gig report --week
gig log
```

Several tasks can stay open, with one running and the rest paused. Pause closes the
running interval; resume opens another or reopens a stopped task on its original history.
`start --switch` and `resume <task> --switch` pause the running task and run the target
as one change. Submitted tasks first require `unsubmit`; voided tasks cannot reopen.
Stop closes the task and leaves its figures provisional. `gig submit <task>` fixes the
rounded total, rate and currency at submission; `gig stop --submit` does both together.
Submitted tasks refuse corrections and voiding until `gig unsubmit <task>` returns them
to stopped. `add <client> <project>` backfills a new stopped task; `add <task>` adds time
to an existing open or stopped task. `void` preserves
history while excluding a task from reports and the default log. `log --all` includes
voided tasks.

Times accept ISO 8601 instants or local date-times, and `HH:mm` for today in the client's
zone. An explicit offset is unambiguous; local times in a daylight-saving gap or overlap
are refused. Intervals cannot overlap or run backwards. Timesheets split intervals at
local midnight, including daylight-saving changes, and show raw time on each day it
happened. Settlement reports count submitted tasks in the period containing their
submission instant, in the client's timezone.

## Commands

Every command accepts `--help`; `gig --version` prints the CLI version. Service commands and setup accept `--json`; `export`
prints JSON Lines in either mode. Shell completion prints shell text, and `__complete`
prints plain candidates. Options requiring values reject an omitted value.

| Command | Example |
|---|---|
| `gig setup` | `gig setup --check --json` or `gig setup --gigtimed /opt/gigtime/gigtimed` |
| `gig completion zsh` | `gig completion zsh > ~/.zfunc/_gig` |
| `gig defaults show` | `gig defaults show --json` |
| `gig defaults set` | `gig defaults set --timezone UTC --week-starts Sunday --rounding nearest-minute` |
| `gig client add` | `gig client add globex --name Globex --currency JPY --timezone UTC --rounding nearest-minute` |
| `gig client set` | `gig client set globex --key globex-new --name Globex --currency USD --clear-timezone --clear-rounding` |
| `gig client list` | `gig client list --all` |
| `gig client archive` | `gig client archive globex-new` |
| `gig project add` | `gig project add acme work --name Work --rate 40 --rounding nearest-minute` |
| `gig project set` | `gig project set acme work --key build --name Build --clear-rate --clear-rounding` |
| `gig project list` | `gig project list acme --all` (omit the client to list all clients) |
| `gig project archive` | `gig project archive acme build` |
| `gig start` | `gig start acme work --ref opaque-42 --rate 45 --switch` |
| `gig pause` | `gig pause` or `gig pause abcdef` |
| `gig resume` | `gig resume` or `gig resume abcdef --switch` |
| `gig stop` | `gig stop abcdef --submit` |
| `gig submit` | `gig submit abcdef` |
| `gig unsubmit` | `gig unsubmit abcdef` |
| `gig status` | `gig status --json` |
| `gig log` | `gig log --last 20 --client acme --project work --all` |
| `gig show` | `gig show abcdef` |
| `gig add` | `gig add acme work --from 2026-03-02T09:00:00Z --to 2026-03-02T10:00:00Z --ref opaque-42 --rate 45` or `gig add abcdef --from 09:00 --to 10:00` |
| `gig amend` | `gig amend abcdef --interval 1 --start 09:00 --end 10:15` |
| `gig void` | `gig void abcdef --reason duplicate` |
| `gig timesheet` | `gig timesheet --range 2026-03-01..2026-03-31 --client acme --project work` |
| `gig report` | `gig report --range 2026-03-01..2026-03-31 --client acme --project work` |
| `gig export` | `gig export --from 2026-03-01 --to 2026-03-31 > events.jsonl` |

Client `set` also accepts `--timezone` and `--rounding`; project `set` accepts `--rate`
and `--rounding`. A value flag and its `--clear-*` counterpart cannot be combined.
`amend` also accepts `--client acme --project work` together, `--ref opaque-43` or
`--clear-ref`, and `--rate 45` or `--clear-rate`. Time corrections require `--interval` and at least one time flag.
`timesheet` and `report` select one of `--week` (default), `--month`, or inclusive local dates with
`--range`. Export date-only bounds include the entire UTC date. Task ids are six
lowercase hexadecimal characters.
Completion offers stopped tasks for `submit` and submitted tasks for `unsubmit`.

A task's own `--rate` overrides the project rate and records the client currency.
Moving it to a client with another currency requires `--rate` or `--clear-rate` in the
same amendment. If that client's currency changes later, submission refuses until the
own rate is replaced or cleared. Unsubmitted figures use the current task or project
rate and catalogue rounding; submission fixes them until the task is unsubmitted.

Bare `pause` targets the running task. Bare `stop` targets the running task, or the
paused tasks when none runs; bare `resume` targets paused tasks when none runs.
A single eligible task acts. Multiple choices or no eligible task refuse with
`invalid_transition` (exit 3); ambiguous choices appear in `error.state.tasks`.
Explicit ids on `pause`, `resume` and `stop` select the task named.

With stdin and stdout both on terminals, a refused switch offers confirmation and an
ambiguous target offers a numbered choice. Enter declines and changes nothing.
Piped commands and `--json` never prompt; `--switch` authorises the switch directly.
Each confirmed choice is sent as a fresh request. A confirmed switch carries the
running task id shown in the prompt; if another task has started running, the retry
refuses instead of pausing it. If nothing runs, the confirmed switch proceeds.
The API carries this optional guard as `switch_from` in a start request or a resume
query parameter. Plain `--switch` requests carry no expected running id.

`add` takes one positional task id to append an interval, or a client and
project to create a stopped task. Existing-task additions take neither `--ref`
nor `--rate`, and must end after they start. Intervals keep their numbers and
appear in time order; touching ends are allowed, overlaps within a task are
refused. Added time on a running task ends no later than its running interval
starts. Submitted and voided tasks refuse additions.

A non-empty ref repeating an open task's ref in the same project adds a `repeated_ref`
warning without blocking creation. Human warnings go to stderr; JSON includes them
in the success envelope's optional `warnings` list.

The supported time-rounding policy is `nearest-minute` (half a minute rounds up).
Settings inherit from defaults to client to project. Settlement amounts stay separate per
currency. Earnings are unrounded decimal amounts per task, summed and then rounded once
per project total and client/currency total, away from zero at the currency's minor unit.
Most currencies use two decimal places; exceptions include zero for JPY and three for BHD.

`timesheet` counts every task except voided ones, including running intervals up to now.
It shows one heading per local day, task lines beneath it, and period totals per project
and client. It contains raw time only, with no rounding or money.

`report` settles submitted tasks using their fixed rounded totals and amounts. Its
Unsubmitted section lists every running, paused or stopped task with provisional figures,
including tasks outside the selected period. Client and project filters apply to both
sections. Each unsubmitted row's nullable `rate` is the task's own rate, in the same
money shape as a task's `rate`. A stale-currency own rate leaves `amount` null and
renders as `unpriced`. `not rated` means neither the task nor its project has a rate.
Unsubmitting removes a task from settled totals; resubmitting places it in the new
submission's period. Migrated tasks keep their old stop instant as their submission
instant, preserving historical settlement totals.

Human task times use the effective client timezone, printed once as `Times in
America/New_York` (or the selected IANA zone). Instants on today in that zone use
`HH:mm`; other dates use `yyyy-MM-dd HH:mm`. Durations remain `HH:MM:SS`.
Open tasks show `open`, and running intervals show `running`. `log` includes
started/stopped times and starts a new labelled table when the timezone changes,
keeping newest-first order. JSON instants remain UTC; each task also carries its
resolved `timezone`.

For a task crossing midnight in New York, viewed after both dates:

```text
Times in America/New_York
Started: 2026-03-02 23:30  Stopped: 2026-03-03 00:30
```

## JSON reference

Service responses are envelopes. Success has `ok` set to true, `command` and `data`, and omits
`error`. Failure has `ok` set to false, `command` and `error`, and omits `data`. Service envelopes
are printed verbatim, with a final newline added when missing. Error members are `code`,
`message`, `state` and `hint`; the last two can be null. Error state has nullable `task_id`,
`state`, `command` and `tasks`. The latter lists eligible ids for an ambiguous target.
`command` names the operation. Instants use UTC ISO 8601 strings,
durations use integer seconds, and money uses decimal strings. Nullable members are
explicit null unless an envelope omission rule applies. Successful `warnings` are
omitted when empty and contain `code` and `message` when present.

| Data shape | JSON keys |
|---|---|
| Envelope | `ok`, `command`, `data`, `error`, `warnings` (omitted when empty) |
| Warning | `code`, `message` |
| Error | `code`, `message`, `state`, `hint` |
| Error state | `task_id`, `state`, `command`, `tasks` (nullable list of eligible task ids) |
| Defaults (`defaults show/set`) | `timezone`, `week_starts`, `rounding` |
| Client (`client add/set`, entries of `client list`) | `id`, `key`, `name`, `currency`, `timezone`, `rounding`, `archived` |
| Client list | `clients` array |
| Project (`project add/set`, entries of `project list`) | `id`, `key`, `client`, `name`, `rate`, `rounding`, `archived` |
| Project list | `projects` array |
| Entity (nested client/project) | `id`, `key` |
| Money (rate) | `amount`, `currency` |
| Acknowledgement (archive) | `id` (nullable) |
| Task (start/pause/resume/stop/submit/unsubmit/show/add/amend/void, log entries) | `id`, `client`, `project`, `ref`, `state`, `intervals`, `raw_seconds`, `rounded_seconds`, `display`, `submission`, `started_at`, `stopped_at`, `timezone`, `rate` (own rate, nullable) |
| Interval | `number`, `start`, `end` (nullable while running) |
| Submission (nullable until submitted) | `submitted_at`, `rounded_seconds`, `rounding`, `rate` (nullable), `currency`, `amount` (nullable Earnings) |
| Status | `running` (nullable task), `paused` (tasks, most recently paused first), `instance` (null for default) |
| Log | `tasks` array |
| Report | `totals`, `clients`, `unsubmitted` arrays |
| Unsubmitted task | `task_id`, `client`, `project`, `ref`, `state`, `raw_seconds`, `rounded_seconds`, `display`, `amount` (nullable, provisional), `rate` (own rate, nullable) |
| Timesheet | `days`, `projects`, `clients` arrays |
| Timesheet day | `date`, `raw_seconds`, `display`, `tasks` array |
| Timesheet task | `task_id`, `client`, `project`, `ref`, `raw_seconds`, `display` |
| Timesheet project total | `client`, `project`, `raw_seconds`, `display` |
| Timesheet client total | `client`, `raw_seconds`, `display` |
| Project report total | `client`, `project`, `currency`, `raw_seconds`, `rounded_seconds`, `display`, `amount` |
| Client/currency report total | `client`, `currency`, `raw_seconds`, `rounded_seconds`, `display`, `amount` |
| Earnings (nullable when a rate is absent) | `currency`, `unrounded`, `rounded` |
| Setup/check | `service_unit`, `service_installed`, `socket_unit`, `socket_installed`, `socket_path`, `socket_exists`, `database_path`, `healthy`, `changed`, `socket_activation`, `instance` (null for default) |
| Health (`/v1/health`, inspected by setup) | `status` |
| Export event (one object per line, no success envelope) | `sequence`, `type`, `stream`, `version`, `timestamp`, `correlation_id`, `data` |

Task `state` is `running`, `paused`, `stopped`, `submitted` or `voided`. `display` is `HH:mm:ss`
(hours can exceed 24). Export `data` is the event payload; its schema follows the event
`type`, including catalogue changes and task lifecycle/correction events. Export orders
by global `sequence`, including void events. It streams bounded pages rather than
materializing the history. A failed export can leave complete event lines on stdout;
a later diagnostic goes to stderr in both modes, with a nonzero exit code. Check that
code before treating an export as a complete backup. A single line has a ten-second
read deadline. Ordinary command responses also have a ten-second deadline.

### Export payloads

Export `data` uses snake_case event members, rather than the envelope DTOs. Event rates
are decimal JSON numbers (nullable); weekday values are numeric DayOfWeek values
(Sunday = 0). Change objects contain `is_specified` and `value`, preserving a field's
omitted/cleared distinction. Current event shapes are:

| Event | Payload keys |
|---|---|
| DefaultsChanged | `timezone`, `week_starts`, `rounding` |
| ClientAdded | `client_id`, `key`, `name`, `currency`, `timezone`, `rounding` |
| ClientChanged | `client_id`, `key`, `name`, `currency`, `timezone`, `rounding` (change objects) |
| ClientArchived | `client_id` |
| ProjectAdded | `project_id`, `client_id`, `key`, `name`, `rate`, `rounding` |
| ProjectChanged | `project_id`, `key`, `name`, `rate`, `rounding` (change objects) |
| ProjectArchived | `project_id` |
| TaskStarted | `task_id`, `project_id`, `ref`, `at` |
| TaskPaused / TaskResumed / TaskReopened | `at` |
| IntervalAdded | `interval`, `start`, `end` |
| TaskOpened / TaskClosed | `task_id` |
| TrackerAdvanced | empty object |
| TaskStopped | `at`, `rounding`, `rate`, `currency` |
| TaskRecorded | `task_id`, `project_id`, `ref`, `start`, `end`, `rounding`, `rate`, `currency` |
| TaskSubmitted | `at`, `rounded_seconds`, `rounding`, `rate`, `currency` |
| TaskUnsubmitted | `at` |
| TaskRateSet | `rate`, `currency` (both nullable to clear the own rate) |
| MigrationApplied | `name` |
| IntervalAmended | `interval`, `start`, `end` |
| TaskReassigned | `project_id` |
| TaskRefChanged | `ref` |
| TaskVoided | `reason` |
| OpenTaskSet | `task_id` |
| OpenTaskCleared | empty object |

New `TaskStopped` and `TaskRecorded` events leave `rounding`, `rate` and `currency` null.
Legacy events retain these fields. On upgrade, stopped tasks with legacy figures gain
`TaskSubmitted` at their original stop instant; open and voided tasks keep their states.
The `MigrationApplied` marker records completion, including for an empty store.

## Exit codes

| Exit | Code | Meaning |
|---|---|---|
| 0 | success | Command completed |
| 1 | internal | Invalid response or unexpected failure |
| 2 | usage | Invalid CLI arguments |
| 3 | invalid_transition | Command not valid in the current task state |
| 4 | not_found | Resource or open task not found |
| 5 | config | Local setup/configuration fault |
| 6 | unavailable | Service or connection unavailable |
| 7 | conflict | Optimistic concurrency conflict |
| 8 | validation | Domain input refused |

## zsh completion

```zsh
mkdir -p ~/.zfunc
gig completion zsh > ~/.zfunc/_gig
fpath=(~/.zfunc $fpath)
autoload -Uz compinit
compinit
```

The script completes commands, flags, active client keys, project keys scoped to a client,
and recent task ids. Hidden `gig __complete clients [prefix]`, `gig __complete projects
[prefix] --client acme` and `gig __complete tasks [prefix]` print one candidate per line.
They print nothing on service failure and bound network work to 100 ms. No shell code
comes from the service. Completion is read-only.

## Verification

```sh
dotnet format --verify-no-changes --no-restore
dotnet run scripts/check-indentation.cs
dotnet test --no-build
dotnet run scripts/check-package-age.cs
```

The normal test run includes `tests/Gigtime.E2E.Tests`: it publishes the AOT CLI and the
service once per run, then drives real subprocesses with temporary sockets, databases,
HOME and XDG roots. It requires Linux, the native build toolchain, systemd-analyze and zsh. Missing tools
fail the tests, including in CI; no end-to-end test silently skips. The tests replace
systemctl with a temporary executable, so they never contact the user's service manager.
README tests check command coverage, public JSON member names and the exact exit table.
