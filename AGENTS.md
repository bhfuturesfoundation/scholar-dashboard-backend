# Working on the Scholar Dashboard API

Rules for anyone changing this repo, human or AI agent. Read this before your first change.
`CLAUDE.md` points here, so Claude Code, Cursor and similar tools pick it up automatically.

The web app is a separate repo ([scholar-dashboard-frontend](https://github.com/Aydhiny/scholar-dashboard-frontend))
with its own `AGENTS.md`. A feature usually needs a pull request in both.

## Commands

```bash
dotnet build Auth-BE.sln                      # build everything
dotnet test Auth.Tests                        # all tests (in-memory, ~2 s, no database needed)
dotnet test Auth.Tests --filter "FullyQualifiedName~AdminAccount"   # one area
cd Auth.API && dotnet run --urls http://localhost:5244              # run the API (needs .env, see README)
dotnet ef migrations add <Name> --project Auth.Models --startup-project Auth.API
docker build -t scholar-api .                 # the image Railway deploys
```

On a machine with only a newer .NET SDK, prefix commands with `DOTNET_ROLL_FORWARD=Major`.

## Where things go

| You are adding | Put it in |
|---|---|
| An HTTP endpoint | `Auth.API/Controllers/<Area>Controller.cs`. Keep it thin: bind, call one service, wrap in `ApiResponse<T>` |
| Business logic | An interface in `Auth.Services/Interfaces/<Area>/` and its class in `Auth.Services/Services/<Area>/` |
| A table | Entity in `Auth.Models/Entities/<Area>/`, `DbSet` + mapping in `ApplicationDbContext`, then a migration |
| Request/response shapes | `Auth.Models/Request/` and `Auth.Models/DTOs/<Area>/`. Never return entities from controllers |
| A role or role group | `Auth.Models/Constants/AppRoles.cs` (add it to `All` and it is created on the next start), plus a label in the web app's `RoleBadge.tsx` |
| A background job | A `BackgroundService` registered in `Program.cs` with `AddHostedService` |
| An environment variable | Read via `IConfiguration`, then list it in `EnvironmentManifest.cs`, `.env.example` and `docs/DEPLOYMENT.md` |
| A setting staff should change themselves | Add it to the `Definitions` list in `Services/Settings/AppSettingsService.cs` and to the App settings page in the web app |
| Wiring | `Auth.API/Program.cs` (`AddScoped<IThing, Thing>()`) |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the pieces fit together.

## Rules

### Access control
- Every controller or action has `[Authorize(Roles = AppRoles.X)]` using the constants in
  `AppRoles.cs`. Never write a role as a string literal; a typo there silently opens an endpoint.
- Anonymous endpoints need `[AllowAnonymous]` and a reason in a comment. Rate-limit them
  (`[EnableRateLimiting("auth-email")]` or another policy in `RateLimiterExtension.cs`).
- Take the caller's id from the token (`ClaimTypes.NameIdentifier`), never from the body or
  route, unless the endpoint is explicitly an admin acting on another account.

### Errors
- Throw from `Auth.Models/Exceptions`: `ValidationException` (400), `NotFoundException` (404),
  `ConflictException` (409), `ForbiddenAccessException` (403). `ErrorHandlingMiddleware` turns
  them into an `ApiResponse` with the right status. Don't catch them in controllers.
- Messages in those exceptions reach the user. Write them as plain sentences, and never
  include provider names, stack traces or internal ids.
- Unexpected exceptions become a 500 with no detail outside Development. Don't return
  `ex.Message` to anonymous callers.

### Audit
- Anything that changes accounts, roles, settings, backups or sends email on someone's
  behalf calls `IAuditService.LogAsync(eventType, actorId, payload)`. Use `Area.Verb`
  names (`Admin.TemporaryPasswordSet`, `Settings.Updated`).
- **Always `await` it.** Fire-and-forget audit calls race the end of the request and are lost.
- Never put passwords, tokens, reset links or journal text in an audit payload or a log line.

### Data and privacy
- Journals are personal reflections written in confidence. Only the scholar, staff in
  `AppRoles.JournalOversight`, and a mentor the scholar has allowed may read them.
- People are deactivated (`IsActive = false`), never deleted.
- Use `UserManager` for anything touching email, user name, password or lockout, so the
  normalised columns and security stamp stay correct.
- To end someone's sessions use `IAccountService.RevokeSessionsAsync`; it also invalidates
  access tokens already issued.

### Configuration
- Read settings through `IConfiguration` (`_configuration["NAME"]`) at the point of use,
  not once in a constructor. App settings and `.env` both feed `IConfiguration`, and reading
  live is what lets a change apply without a restart.
- Treat a missing optional integration (Dropbox, Redis, RabbitMQ, push) as "feature off" and
  report it on Operations → Deploy health, never as a crash at startup.

### Database and migrations
- Migrations run **automatically on startup in production**. Prefer additive changes (new
  tables, nullable columns). Renaming or dropping something needs a plan for the old code
  still running during the deploy.
- Before pushing a migration, apply it to a local Postgres that has data in it, roll it back
  with `dotnet ef database update <PreviousMigration>`, and apply it again. Say in the pull
  request that you did.
- Don't edit a migration that has been merged; add a new one.

### Tests
- xUnit, EF Core in-memory database, Moq. One file per service: `Auth.Tests/<Service>Tests.cs`.
- Test behaviour that would hurt if it broke (access, money-like counts, data loss, emails
  sent or not sent), and name tests as sentences: `TemporaryPassword_ForcesChangeUnlocksAndEndsSessions`.
- Code that needs real Postgres behaviour (constraints, raw SQL) can't be proven in-memory.
  Check it against a local database and mention it in the pull request.

### Comments
- Comment *why*, not *what*. Existing files explain decisions at length; match the density of
  the file you're in, but keep new comments as short as they can be while still answering "why".

### Email and providers
- Send through `IEmailDispatcher` (or `IResendService` for account emails). It handles the
  provider choice, fallback, suppression list and the sandbox redirect.
- New account emails should be bilingual (Bosnian first, then English) because the reader
  hasn't chosen a language yet. The welcome email is; the password-reset email is still
  English-only.

## Before you open a pull request

1. `dotnet build Auth-BE.sln` and `dotnet test Auth.Tests` pass.
2. New endpoint → `[Authorize]` with `AppRoles`, and the web app has a screen for it (or an
   issue saying why not).
3. New environment variable → manifest, `.env.example`, `docs/DEPLOYMENT.md`, and listed under
   "Before merging" in the PR.
4. New migration → tested up, down and up again on a local database with data.
5. User-visible change → the help guide in the web app (`src/i18n/*/helpContent.ts`) and
   `docs/SUPPORT_RUNBOOK.md` still describe the app correctly.
6. Fill in the pull request template.

## Docs in this repo

- [README.md](README.md): local setup
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how the system fits together
- [CONTRIBUTING.md](CONTRIBUTING.md): branches, reviews, releases
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md): hosting, every variable, release and rollback
- [docs/SUPPORT_RUNBOOK.md](docs/SUPPORT_RUNBOOK.md): what staff do when something goes wrong
- [docs/EMAIL_PROVIDERS.md](docs/EMAIL_PROVIDERS.md), [docs/NOTIFICATIONS.md](docs/NOTIFICATIONS.md), [docs/FLS_OUTREACH_RUNBOOK.md](docs/FLS_OUTREACH_RUNBOOK.md)
