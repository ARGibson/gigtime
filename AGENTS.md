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
