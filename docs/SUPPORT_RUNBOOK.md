# Support runbook

What to do when something goes wrong, written for the foundation staff who run the
platform day to day. Everyday "how do I…" guides for every role are in the app itself at
**`/help`** (profile menu → Help, or "Can't sign in?" on the sign-in page).

Who can do what:

| Role | Can |
|---|---|
| **Admin** | Everything: users, account help, roles, scholars, backups, configuration |
| **Program manager** | Scholars (add, promote, mentors), journals, backups, exports. Not user accounts |
| **Technical team** | Railway, Vercel, environment variables, code. See [DEPLOYMENT.md](DEPLOYMENT.md) |

---

## "I can't sign in"

Ask the person for their email address, then: **Admin → Users → find them → lifebuoy
button.** The panel's top box says what is probably wrong.

| The panel says | Do this |
|---|---|
| **Deactivated** | Tick them on the Users page → *Set Active*, if they should have access |
| **Locked until …** | Press **Unlock**. Locks come from 5 wrong passwords and clear by themselves after 15 minutes |
| **Never signed in** | Press **Send password reset email**. It works as an invite: they choose their own password |
| **Nothing wrong** | Check they type exactly the email shown, then **Send password reset email** |

**The email doesn't arrive** (after 10 minutes, spam checked):

1. Press **Set a temporary password**. Give it to them **by phone or in person**.
   They must choose a new one when they sign in. Their old password stops working and
   they are signed out everywhere.
2. Then check email is working: **Operations → Deploy health → Send test email**. If
   your own test email doesn't arrive, email is broken for everyone. See "Email isn't
   being delivered" below.

**The email address is wrong:** lifebuoy panel → *Name and email* → Edit → Save. Tell
the person, because they now sign in with the new address.

**A phone or laptop was lost, or someone else knows the password:** lifebuoy panel →
**Sign out everywhere**, then **Set a temporary password**.

**The admin themself is locked out:** another admin can help. If there is no other
admin, the technical team can create one with `BOOTSTRAP_ADMIN_*` only on a database
with no admins at all. Otherwise they need database access. **Keep at least two admins.**

Every action above is recorded in **Admin → Audit** with who did it and when.

## Adding and removing people

| Task | Where |
|---|---|
| One person (any role) | Admin → Users → **Add person**. A welcome email lets them choose a password. A temporary password is also shown once, as a fallback |
| A whole generation of scholars | Scholars → **Add scholars**: download the template, upload, check, create. Copy the passwords: they are shown once |
| Change someone's role | Users → pencil button |
| Someone leaves | Users → tick → **Set Inactive**. Journals are kept. For scholars who did not finish, also set status *Withdrawn* in Scholars |
| New school year | Scholars → **Promote**: Preview, then Apply. Seniors → Alumni first, then Juniors → Seniors. Every run can be reverted from its history |

People are never deleted from the app, only deactivated. That keeps their journal
history for reports. If someone asks for their data to be erased, contact the technical
team.

## Email isn't being delivered

1. **Operations → Deploy health → Send test email**. It reports which provider was used
   and the provider's error, if any.
2. Common causes:
   - The provider's password or API key changed or expired (Gmail app passwords get revoked
     when the Google account password changes).
   - The free tier ran out for the month (Resend 3,000/month, EmailJS 200/month).
   - `EMAIL_SANDBOX_REDIRECT_TO` was left set, so all mail goes to one address.
3. The technical team fixes the variables on Railway ([EMAIL_PROVIDERS.md](EMAIL_PROVIDERS.md)).
   No redeploy of the web app is needed.

Until it is fixed, use **temporary passwords** for anyone who can't get in.

## Backups

**What runs by itself:** a backup on the **1st and 15th of each month at 02:00 UTC**, kept
90 days. It is a JSON copy of all data **without passwords**, uploaded to Dropbox
**only if the Dropbox variables are set**. Check Operations → Backups: every scheduled
row should say *Completed*, and Deploy health should show Dropbox as configured.

**Manual backup:** Operations → Backups → choose a format → Create. It downloads to your
computer. **Use the *pg_dump* format whenever it is offered**: it is the only one that
contains the full database structure and can be restored in one step.

Backup files contain every scholar's journal. Store them only in the foundation's own
protected storage, and never send them by email.

**The real safety net is the database host's own backups.** In Railway, open the
Postgres service and check that backups are switched on. Do this once, now.

### Restoring a backup (technical team)

Always restore into a **new, empty database first**, check it, and only then switch the
API to it. Never restore over the live database.

- **Railway's own backup:** restore from the Postgres service's Backups tab.
- **pg_dump file (`.dump`):**
  ```bash
  pg_restore --no-owner --no-privileges -d "postgresql://USER:PASSWORD@HOST:PORT/NEWDB" backup.dump
  ```
  Then set the API's `PG*` variables to the new database and redeploy.
- **JSON / CSV / SQL backups** are data only. Restoring them means creating the schema
  first: start the API once against the empty database so it applies its migrations,
  stop it, then load the data with a script. This is a developer task.

**Scheduled backups contain no passwords.** After restoring one, every person must use
"Forgot password?", or an admin gives them a temporary password.

## The site is down

1. Does the web app load at all? If not, check **Vercel** (deployments and status).
2. Does it load but say it can't reach the server, or sign-in fails for everyone? Check
   the **Railway API service**: is the latest deployment active, and what do its logs say?
3. Signed in as admin: **Operations → Deploy health** shows the database, migrations
   and integrations at a glance.
4. A bad deploy? Redeploy the previous deployment in Railway or Vercel. See
   [DEPLOYMENT.md](DEPLOYMENT.md#rolling-back).

When you report a problem to the technical team, include: what you tried, the exact
error text or a screenshot, the time it happened, and the affected person's email.
