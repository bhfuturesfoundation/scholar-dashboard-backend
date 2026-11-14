# Deploying and configuring the Scholar Dashboard

This is the page to read before you change a setting or ship a release. It covers where
everything runs, every environment variable and what breaks without it, and a deploy and
rollback checklist that keeps the database safe.

## Where things run

| Part | Where | Deploys from |
|---|---|---|
| API (this repo) | Railway, Docker image built from `Dockerfile` | `main` |
| Database | Railway PostgreSQL | — |
| Web app ([scholar-dashboard-frontend](https://github.com/Aydhiny/scholar-dashboard-frontend)) | Vercel | `master` |

The API reads its settings from environment variables on the Railway **API service**
(Variables tab). The web app reads one setting from Vercel (Project → Settings →
Environment Variables).

After a deploy, open **Admin → Operations**:

- **Deploy health** shows whether the database, migrations, email and integrations are working.
- **Configuration** lists every tracked variable as set or not set. It never shows the values.

## What happens to the database on deploy

The API **applies pending migrations automatically when it starts** (`Program.cs`,
`MigrateAsync`). There is no separate migration step.

- A release that adds a migration changes the database the moment the new container
  starts. **Take a backup before deploying any release that adds files under
  `Auth.Models/Migrations/`** (Admin → Operations → Backups, format *pg_dump* if it is
  offered, otherwise use the Railway Postgres backup).
- A release without new migration files does not touch the schema.
- Seeders also run on every start. They only create things that are missing (roles,
  the partnerships account, the bootstrap admin when no admin exists, default
  questions). They never delete or overwrite data.

## Settings staff change in the app

**Admin → App settings** covers the journal deadline day, reminder days, refusing late
journals, the weekly summary email day and hour, and the backup schedule, format and
retention. A value saved there **overrides** the environment variable of the same name
(`JOURNAL_WINDOW_CLOSE_DAY`, `JOURNAL_REMINDER_DAYS`, `JOURNAL_ENFORCE_WINDOW`,
`DIGEST_DAY`, `DIGEST_HOUR_UTC`, `BACKUP_SCHEDULE_*`, `BACKUP_RETENTION_DAYS`). *Use
default* removes the override. Saved values live in the `AppSettings` table and apply
immediately, with no redeploy. On more than one API instance, the others pick them up
at their next restart.

Everything else (database, sign-in keys, email provider, storage, integrations) stays
in the variables below on purpose: those are secrets or can take the site down.

## Environment variables — API (Railway)

### Required: the API does not work without these

| Variable | What it is | If it is wrong or missing |
|---|---|---|
| `PGHOST` | Postgres host. Use Railway's reference to the Postgres service. | The API connects to `localhost` and finds nothing. **Never point production at another database.** |
| `PGPORT` | Postgres port | Defaults to 5432 |
| `PGDATABASE` | Database name | API fails to start |
| `PGUSER` | Database user | API fails to start |
| `PGPASSWORD` | Database password | API fails to start |
| `JWT_SECRET` | Long random string that signs sign-in tokens | Nobody can sign in. **Changing it signs everyone out** (harmless, but expect calls). |
| `JWT_ISSUER` | Token issuer name | Every token is rejected. Keep it the same between deploys. |
| `JWT_AUDIENCE` | Token audience name | Same as above |

`DATABASE_URL` is **not** read by the API. Older notes mention it; ignore them.

### Strongly recommended

| Variable | What it is | If it is missing |
|---|---|---|
| `FRONTEND_BASE_URL` | Public address of the web app, e.g. `https://scholar-dashboard-frontend.vercel.app` (no trailing slash needed) | Reset and welcome email links point to the old Vercel address. **Set this if the site ever moves to its own domain.** |
| `EMAIL_PROVIDER` | `smtp`, `gmass`, `mailchimp`, `resend` or `emailjs`. Never `log` in production | Uses the first provider that is configured |
| Provider settings | e.g. `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM_EMAIL`. Full list in [EMAIL_PROVIDERS.md](EMAIL_PROVIDERS.md) | **No email at all**: no password resets, no welcome emails, no reminders. Test with Operations → Deploy health → *Send test email*. |
| `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | Default sender | Providers without their own from-address cannot send |
| `FLS_UPLOAD_PATH` | Folder for FLS speaker uploads, **on a Railway volume** (e.g. mount a volume at `/data` and set `/data/fls`) | Files are saved inside the container and **lost on every redeploy** |
| `DROPBOX_APP_KEY`, `DROPBOX_APP_SECRET`, `DROPBOX_REFRESH_TOKEN` | Off-site copies of scheduled backups and intake password files | Scheduled backups are **not** copied off the server. See the backups section of [SUPPORT_RUNBOOK.md](SUPPORT_RUNBOOK.md). |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Defaults to Production. Never set `Development` in production: it shows internal error details. |

### Access and first setup

| Variable | What it does | Default and advice |
|---|---|---|
| `ALLOW_PUBLIC_REGISTRATION` | `true` lets anyone on the internet create their own scholar account | **Leave unset.** Staff add people through Admin → Users → Add person, or Scholars → Add scholars. |
| `BOOTSTRAP_ADMIN_EMAIL`, `BOOTSTRAP_ADMIN_PASSWORD` | Create the first administrator, **only if the database has no admin at all** | Use once on a new environment: set both, deploy, sign in, change the password, then **delete both variables**. They do nothing while any admin exists. |
| `SEED_PARTNER_MEMBER_PASSWORD` | First password of the `partnerships@bhfuturesfoundation.org` account | Set a strong value before the first deploy. Ignored after the account exists. |

### Optional

| Variable | What it does |
|---|---|
| `EMAIL_SANDBOX_REDIRECT_TO` | Sends **all** email to one address. Staging only: **if set in production, nobody gets their email.** |
| `EMAIL_ENABLE_FALLBACK`, `EMAIL_FALLBACK_ORDER` | Try another provider when one fails |
| `EMAIL_SEND_DELAY_MS`, `EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN` | Bulk email pacing and cap |
| `BACKUP_SCHEDULE_ENABLED`, `BACKUP_SCHEDULE_DAYS`, `BACKUP_SCHEDULE_HOUR_UTC`, `BACKUP_SCHEDULE_FORMAT`, `BACKUP_RETENTION_DAYS` | Scheduled backups. Default: on, the 1st and 15th, 02:00 UTC, JSON, kept 90 days. Prefer changing them in **Admin → App settings** |
| `NEWS_SCRAPE_ENABLED`, `NEWS_SCRAPE_HOUR_UTC` | Daily copy of the foundation news for the home page |
| `NOTIFICATIONS_SCHEDULER_ENABLED`, `JOURNAL_REMINDER_DAYS` (also in App settings), `JOURNAL_WINDOW_CLOSE_DAY`, `JOURNAL_TIMEZONE`, `JOURNAL_ENFORCE_WINDOW`, `DIGEST_DAY`, `DIGEST_HOUR_UTC` | Journal reminders and weekly digest. See [NOTIFICATIONS.md](NOTIFICATIONS.md) |
| `VAPID_PUBLIC_KEY`, `VAPID_PRIVATE_KEY`, `VAPID_SUBJECT` | Browser push notifications. Changing the keys drops every existing subscription. |
| `GOOGLE_CREDENTIALS`, `SPREADSHEET_ID` | Volunteering team hours sheet. Note the name: `GOOGLE_CREDENTIALS`, not `GOOGLE_SHEETS_CREDENTIALS`. |
| `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GITHUB_CLIENT_ID`, `GITHUB_CLIENT_SECRET` | "Sign in with Google/GitHub" for existing accounts |
| `JWT_EXPIRATION_MINUTES` | How long a sign-in token lasts before it is silently renewed, 5 to 1440. Default 60 |
| `REDIS_URL` | Needed only if the API runs on more than one instance. With one instance, leave it unset and **delete the Redis service** to save money |
| `RABBITMQ_URL`, `RABBITMQ_ENABLED` | Not needed. Without them the API sends email directly in the background. To save money, set `RABBITMQ_ENABLED=false` (or remove `RABBITMQ_URL`), deploy, send a test email, then **delete the RabbitMQ service** |

## Environment variables — web app (Vercel)

| Variable | Value |
|---|---|
| `NEXT_PUBLIC_API_BASE_URL` | The API address **including `/api`**, e.g. `https://<railway-api-host>/api` |

It is baked in at build time. After changing it, **redeploy** the Vercel project.

If the web app ever moves to a new domain, change three things together:

1. Vercel: add the domain.
2. Railway API: set `FRONTEND_BASE_URL` to the new address.
3. API code: add the new address to the CORS list in `Auth.API/Program.cs`
   (`AllowSpecificOrigin`). **Without this, the browser blocks every request and nobody can sign in.**

## Release checklist

1. CI is green on the pull request (`.github/workflows/ci.yml`).
2. Does the release add files under `Auth.Models/Migrations/`? Then take a backup first.
3. Add any **new** variables named in the pull request to Railway or Vercel **before** merging.
4. Merge. Railway rebuilds the API, Vercel rebuilds the web app.
5. When Railway shows the deploy as active, open Admin → Operations → Deploy health:
   everything green or "not configured", and the database check healthy.
6. Sign in as an admin and as a test scholar. Send a test email.

## Releases with a one-way data change

| Migration | What it does | Undo |
|---|---|---|
| `AddMentorAssignments` | Moves every scholar's mentor into a new table that allows two mentors, keeping each scholar's journal-sharing choice, then removes the old mentor column | Reverting keeps only each scholar's first mentor |
| `AddQuestionSkillType` | Gives each goal question its goal area, based on its order (1 Personal, 2 Professional, 3 Teamwork and leadership, 4 Knowledge) | Safe |
| `AddResourceDocuments` | Adds the Resources table and lists the 8 built-in PDFs in it | Removes uploaded documents |

**Take a backup before deploying these.** After the deploy, open Scholars → Mentors and
check a few pairings, and Questions to check each goal question has its area.

## Housekeeping the API does by itself

Once a day it removes data nobody needs any more, to keep the database small:
sign-in tokens that ended over 30 days ago, dismissed notifications older than 90 days,
read notifications older than 180 days, and audit entries older than a year.

## Rolling back

- **Code only (no new migration):** in Railway, open the API service → Deployments →
  redeploy the previous deployment. Do the same in Vercel if needed. Safe.
- **The release added a migration:** the database has already changed, and old code may
  not match it. Read the migration first. If it only adds tables or columns, redeploying
  the old code is usually fine. If the app errors on the database after a rollback,
  restore the backup you took in step 2. See "Restoring a backup" in
  [SUPPORT_RUNBOOK.md](SUPPORT_RUNBOOK.md).

## Things never to do

- Point the API at a different database "to test". Startup applies migrations to whatever it connects to.
- Delete or rename `PG*`, `JWT_*` variables on a running service.
- Leave `EMAIL_SANDBOX_REDIRECT_TO` or `EMAIL_PROVIDER=log` set in production.
- Leave `BOOTSTRAP_ADMIN_*` set after the first admin has signed in.
- Set `ALLOW_PUBLIC_REGISTRATION=true` unless you really want open sign-up.
