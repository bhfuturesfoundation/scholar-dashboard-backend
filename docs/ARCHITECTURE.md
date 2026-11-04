# Architecture

How the Scholar Dashboard fits together. For setup see the [README](../README.md); for
rules see [AGENTS.md](../AGENTS.md).

## The system

```
Browser ──► Web app (Next.js, Vercel) ──HTTPS + JWT──► API (ASP.NET Core 8, Railway) ──► PostgreSQL (Railway)
                 ▲                                         │
                 └──────── SignalR (notifications,  ◄──────┤
                           presence, minigames)            ├──► Email provider (SMTP / Resend / …)
                                                           ├──► Dropbox (backup copies, intake passwords)
                                                           ├──► Google Sheets (volunteering hours)
                                                           ├──► bhfuturesfoundation.org/news (daily copy)
                                                           └──► Optional: Redis (SignalR across instances), RabbitMQ (queued email)
```

Two portals share one API and one database:

- **Scholar dashboard**: scholars, mentors, program managers, volunteering team, admins.
- **FLS portal** (`/fls/*`): Future Leaders Summit speakers, FLS admins, and the
  partnerships team's mailing module (`/mailing/*`).

## Projects

| Project | Depends on | Holds |
|---|---|---|
| `Auth.Models` | — | Entities, DTOs, requests, `ApplicationDbContext`, migrations, constants (`AppRoles`, `NotificationKeys`), exceptions |
| `Auth.Services` | Models | All business logic: an interface per service in `Interfaces/`, the class in `Services/` |
| `Auth.API` | Services, Models | Controllers, middleware, SignalR hubs, `Program.cs` startup and wiring |
| `Auth.Tests` | all | xUnit tests |

A request goes: **controller** (bind input, check role) → **service** (rules, database
through `ApplicationDbContext` or `UserManager`) → **DTO** wrapped in `ApiResponse<T>`.

## Areas

| Area | Controllers | Services | What it does |
|---|---|---|---|
| Accounts and sign-in | `AuthController`, `ExternalAuthController`, `AdminController` | `AuthService`, `UserService`, `AccountService`, `AdminAccountService`, `PasswordResetService`, `TokenService` | Sign-in, tokens, password reset, self-service profile, admin account help, Add person |
| Scholars | `ScholarsController`, `MentorMenteeController` | `Scholars/ScholarLifecycleService`, `MentorMenteeService` | Generations, statuses, yearly promotion and revert, spreadsheet intake, mentor pairing |
| Journals | `JournalController`, `QuestionController`, `QuestionAdminController`, `AnswerController`, `ManagerController` | `JournalService`, `QuestionService`, `AnswerService`, `ManagerService` | Monthly questions, drafts and submissions, staff and mentor views |
| Engagement | `EngagementController`, `SkillController`, `SocialController`, `GameScoreController`, `PuzzlesController` | `Engagement/*`, `SkillService`, `Games/*` | Achievements, kudos, skills, minigames |
| Notifications | `NotificationsController`, `PresenceController` | `Notifications/*` | In-app, push and email notifications, reminders, weekly digest, announcements |
| FLS portal | `FLS/*Controller` | `FLS/*` | Speakers, tasks, uploads, documents, meetings, campaigns |
| Mailing | `Mailing/*Controller` | `Mailing/*` | Firm directory, templates, campaigns, schedules |
| Operations | `OperationsController`, `BackupController` | `Operations/*` | Deploy health, configuration status, audit log, backups, exports, test email |
| Settings | `AppSettingsController` | `Settings/*` | Programme settings staff change from the app |
| Other | `NewsController`, `SuggestionsController`, `VolunteeringController` | `News/*`, `Suggestions/*`, `VolunteeringService` | News widget, suggestion board, volunteering hours |

## Sign-in and sessions

1. `POST /api/auth/login` checks the password (5 wrong attempts lock the account for 15
   minutes) and returns a short-lived **JWT access token**. A **refresh token** goes in an
   httpOnly cookie.
2. The web app stores the access token and sends it as `Authorization: Bearer`.
3. When the access token expires but the refresh cookie is valid, the API authenticates the
   request anyway and returns a new token in the `X-New-Token` response header; the web app
   swaps it in. A true 401 makes the web app call `/api/auth/refresh` once, then sign out.
4. Every access token carries the account's `TokenVersion`. "Sign out everywhere" and an
   admin's temporary password bump it, which kills every token already issued
   (`TokenVersionCache` keeps that check cheap).
5. `MustChangePassword` sends the person to Change password before anything else.
6. Google and GitHub sign-in only link to an existing account with the same verified email.
   They never create accounts.

Roles are ASP.NET Identity roles listed in `AppRoles.All` and created at startup.

## Startup order (`Program.cs`)

1. Load `.env` (local only), then environment variables, then **App settings** from the
   database. Later sources win.
2. Register services, authentication, CORS (`https://scholar-dashboard-frontend.vercel.app`
   and `http://localhost:3000` only), rate limits, SignalR.
3. Build. Middleware order: request logging → error handling → routing → CORS → HTTPS →
   rate limiter → authentication → authorization.
4. **Apply pending migrations** (`MigrateAsync`), then load saved App settings.
5. Seeders: roles, the partnerships account, the bootstrap admin (only while no admin
   exists), default journal questions, mailing defaults. A failing seeder logs and the app
   still starts.
6. Background services start: scheduled backups, notification scheduler (reminders, digest,
   outbound email and push), mailing scheduler, news scraper, and the email queue consumer
   when RabbitMQ is enabled.

`GET /healthz` is a liveness check. `GET /version` reports the commit, branch and Railway
deployment actually running; use it to prove what is live after a deploy.

## Configuration

Settings are read through `IConfiguration` at the point of use. Sources, lowest priority
first: `appsettings.json` → environment variables (and `.env` locally) → **App settings
saved in the database** (only the keys listed in `AppSettingsService`). Admin → Operations
→ Configuration shows which tracked variables are set, never their values. The full list is
in [DEPLOYMENT.md](DEPLOYMENT.md).

## Email

Everything goes through `IEmailDispatcher`, which picks the provider (`EMAIL_PROVIDER`),
falls back to another on transient failure if configured, skips suppressed addresses, and
honours `EMAIL_SANDBOX_REDIRECT_TO`. Providers live in `Services/Email/Providers/`. Account
emails (reset, welcome) go through `IResendService` (the name is historical; it uses the
dispatcher). The email-confirmation message is queued through RabbitMQ when it is enabled.

## Notifications

A notification is stored as a **key plus parameters** (`NotificationKeys`), not a finished
sentence, because the server doesn't know the reader's language. The web app renders it from
`src/i18n/*/notificationMessages.ts`. Delivery: in-app (and live over SignalR), web push when
VAPID keys are set, and email per the user's preferences. Never rename a key: notifications
already sent under the old name stop rendering.

## Backups

`ScheduledBackupService` checks once an hour; on the scheduled days and hour it writes a
backup (JSON by default, without password hashes) and copies it to Dropbox when configured.
Admins and program managers can make one on demand. Restoring is a manual job; see
[SUPPORT_RUNBOOK.md](SUPPORT_RUNBOOK.md#restoring-a-backup-technical-team).
