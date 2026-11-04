# Contributing

How changes get from an idea to production. Coding rules are in [AGENTS.md](AGENTS.md).

## Flow

1. Branch from `main`: `feature/<short-name>` or `fix/<short-name>`.
2. Make the change. If it is user-facing, the matching web app change goes in a pull request
   on [scholar-dashboard-frontend](https://github.com/Aydhiny/scholar-dashboard-frontend)
   with the same branch name, and each PR links to the other.
3. Run it locally end to end (API + web app). Tests alone don't count as tested.
4. Open a pull request against `main` and fill in the template. CI must be green.
5. Someone else reviews it. Self-merging is for emergencies only, with a note in the PR.
6. Merge. Railway deploys `main`. Follow the release checklist in
   [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md#release-checklist), and merge the backend before a
   web app change that depends on it.

## Commits

One logical change per commit. Subject line in the imperative, no prefix, under ~70
characters, saying what changes for the user or the system:

```
Let admins change programme settings from the app
Fix reset links breaking for addresses with a plus sign
```

The body explains *why* when it isn't obvious.

## Pull requests

- Keep them reviewable: one feature or fix. Split anything past ~1,500 changed lines.
- List new environment variables under **Before merging**, so they are set on Railway first.
- Say how you tested, with the scenarios you ran. For a migration: up, down and up again on
  a local database that has data.
- Update docs in the same PR: `docs/DEPLOYMENT.md` for variables, `docs/SUPPORT_RUNBOOK.md`
  for anything staff do, and the web app's help guide for anything users see.

## Getting help

Start with [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The code explains its own decisions
in comments; search for the feature name before asking.
