# Scholar Dashboard — API

ASP.NET Core 8 API for the BH Futures Foundation scholar platform: accounts and roles,
scholar journals, cohorts, mentors, the FLS speaker portal, partnership mailing,
notifications and backups. The web app lives in
[scholar-dashboard-frontend](https://github.com/Aydhiny/scholar-dashboard-frontend).

**Running the platform rather than developing it?** Read
[docs/SUPPORT_RUNBOOK.md](docs/SUPPORT_RUNBOOK.md) and
[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

## Projects

| Project | What is in it |
|---|---|
| `Auth.API` | Controllers, middleware, startup (`Program.cs`: DI, CORS, migrations, seeding) |
| `Auth.Services` | Business logic. Interfaces in `Interfaces/`, implementations in `Services/` |
| `Auth.Models` | EF Core entities, DTOs, requests, `ApplicationDbContext`, migrations |
| `Auth.Tests` | xUnit tests (EF in-memory + Moq) |
| `Auth.Tools.ScholarImport` | One-off console tool for scholar imports |

Roles are defined once in `Auth.Models/Constants/AppRoles.cs`; controllers use those
constants in `[Authorize(Roles = ...)]`.

## Run it locally

You need the .NET 8 SDK and Docker.

```bash
docker run -d --name scholar-pg -e POSTGRES_PASSWORD=localpw -e POSTGRES_DB=scholar_local -p 127.0.0.1:5432:5432 postgres:16
cp .env.example .env
```

In `.env`, set at least:

```
PGUSER=postgres
PGPASSWORD=localpw
PGDATABASE=scholar_local
JWT_SECRET=<any 64+ character string>
JWT_ISSUER=scholar-local
JWT_AUDIENCE=scholar-local
EMAIL_PROVIDER=log
FRONTEND_BASE_URL=http://localhost:3000
BOOTSTRAP_ADMIN_EMAIL=admin@example.test
BOOTSTRAP_ADMIN_PASSWORD=ChangeMe123!
BACKUP_SCHEDULE_ENABLED=false
NEWS_SCRAPE_ENABLED=false
```

Leave `PGHOST` unset locally. Then:

```bash
cd Auth.API && dotnet run --urls http://localhost:5244
```

The first start creates the schema and seeds roles and the bootstrap admin. With
`EMAIL_PROVIDER=log`, emails are written to the console instead of being sent, so you
can copy reset links from there.

Run the web app with `NEXT_PUBLIC_API_BASE_URL=http://localhost:5244/api npm run dev`
(only `http://localhost:3000` is allowed by CORS).

## Tests

```bash
dotnet test Auth.Tests
```

CI runs the same on every pull request (`.github/workflows/ci.yml`).

## Database migrations

```bash
dotnet tool install --global dotnet-ef --version 8.0.5
dotnet ef migrations add <Name> --project Auth.Models --startup-project Auth.API
```

Migrations are applied **automatically on startup**, in production too. Read
[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md#what-happens-to-the-database-on-deploy) before
merging one.

## Adding an environment variable

1. Read it through `IConfiguration` (`_configuration["NAME"]`).
2. Add it to `Auth.Services/Services/Operations/EnvironmentManifest.cs` so it shows on
   Admin → Operations → Configuration.
3. Add it to `.env.example` and the tables in `docs/DEPLOYMENT.md`.
4. Name it in the pull request so it is set on Railway **before** merging.

## More docs

- [AGENTS.md](AGENTS.md): coding rules for developers and AI agents (start here)
- [CONTRIBUTING.md](CONTRIBUTING.md): branches, commits, pull requests, releases
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how the system fits together
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md): hosting, every variable, release and rollback
- [docs/SUPPORT_RUNBOOK.md](docs/SUPPORT_RUNBOOK.md): sign-in problems, people, email, backups, outages
- [docs/EMAIL_PROVIDERS.md](docs/EMAIL_PROVIDERS.md): setting up each email provider
- [docs/NOTIFICATIONS.md](docs/NOTIFICATIONS.md): reminders, digests, push
- [docs/FLS_OUTREACH_RUNBOOK.md](docs/FLS_OUTREACH_RUNBOOK.md): FLS outreach campaigns
