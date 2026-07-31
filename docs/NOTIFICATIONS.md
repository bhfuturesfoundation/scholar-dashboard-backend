# Notifications

How a scholar finds out something happened, and how to configure it.

Nothing below is required. With no configuration at all the system still works: notifications
appear in the bell menu, reminders are created on schedule, and email goes out through
whichever provider is already configured. The variables here tune that behaviour and switch
on web push.

---

## How it fits together

```
something happens ──► INotificationService.CreateAsync
                          │
                          ├─► writes a Notification row      (always)
                          ├─► pushes down SignalR             (best effort, cosmetic)
                          └─► flags WantsEmail / WantsPush    (after preferences)
                                     │
                                     ▼
                     NotificationSchedulerService drains the row
                                     │
                          ┌──────────┴──────────┐
                          ▼                     ▼
                    IEmailDispatcher       IPushSender
                   (suppression list)     (VAPID / browser)
```

The row **is** the queue — a transactional outbox. Three consequences worth knowing:

- Awarding a badge never blocks on SMTP, so a slow mail provider cannot make the progress
  page time out.
- A notification created while the mail provider is down goes out when it recovers, rather
  than vanishing into a fire-and-forget task.
- Quiet hours are a column (`DeferredUntil`), not a scheduled callback a deploy would drop.

Notifications are stored as a **key plus parameters**, never as a finished sentence. The
server has no idea which language the reader picked. The bell menu renders from the frontend
dictionary; email and push render from `NotificationCatalog`, which holds the same strings
server-side because there is no browser at 08:00 when a reminder goes out.

---

## Reminders and the submission window

| Variable | Default | What it does |
|---|---|---|
| `NOTIFICATIONS_SCHEDULER_ENABLED` | `true` | Set `false` to stop reminders, digests **and** the outbound drain. Notifications would still be created, but nothing would leave the app. |
| `JOURNAL_REMINDER_DAYS` | `5,2,0` | Days before the deadline a reminder is sent. `0` means the day itself. |
| `JOURNAL_WINDOW_CLOSE_DAY` | `9` | Last day of the month a journal may be submitted for the previous month. Clamped to 1–28 so every month has the day. |
| `JOURNAL_TIMEZONE` | `Europe/Sarajevo` | The zone the deadline is anchored to. "By the 9th" means the end of the 9th where the foundation is, not wherever a scholar is studying. |
| `JOURNAL_ENFORCE_WINDOW` | `false` | See below. |
| `DIGEST_DAY` | `Monday` | Day of the week the weekly summary goes out. |
| `DIGEST_HOUR_UTC` | `7` | Hour (UTC) it goes out. |

### `JOURNAL_ENFORCE_WINDOW` — read before switching on

The submission window used to exist **only in the browser**. The API accepted a journal for
any month at any time, so the deadline was decoration on top of an open endpoint.

`JournalWindowService` now owns the rule, but enforcement is **off by default and opt-in**,
because turning it on is a real behaviour change: staff have historically been able to
submit on a scholar's behalf after the fact, and that would start failing. Turn it on
deliberately, having decided that late submission should be impossible rather than merely
discouraged.

### Why reminders are a background service

They used to be a React effect that ran when the dashboard mounted. The scholar who most
needs a deadline reminder is the one who has not opened the dashboard since last month — so
the reminder reached everyone except its audience. It is a `BackgroundService` now for that
reason alone.

Reminders are idempotent through a unique `DedupeKey` (`journal:2026-06:t-2`), not through
hoping the schedule never overlaps. The sweep can run hourly, on several instances, without
sending anything twice.

---

## Web push

Optional. Without it the push column is hidden in the settings UI rather than shown as
switches that silently do nothing.

| Variable | Required for push | What it does |
|---|---|---|
| `VAPID_PUBLIC_KEY` | yes | Handed to the browser at subscribe time. |
| `VAPID_PRIVATE_KEY` | yes | Signs every push request. Secret. |
| `VAPID_SUBJECT` | no | Contact URL the push service uses if the app misbehaves. A `mailto:` or `https:` URL. Defaults to `mailto:info@bhfuturesfoundation.org`. |

### Generating a key pair

Sign in as an Admin and `POST /api/notifications/push/generate-keys`. It returns a fresh
pair and forgets it — the endpoint cannot read the keys currently in use.

Set both values in the environment and redeploy.

> **Rotating the pair invalidates every existing subscription.** Browsers bind a subscription
> to the public key they were given, so every device has to opt in again. Generate once.

### What each party controls

Three separate "no"s, and confusing them is where the debugging time goes:

1. **The browser** grants notification permission. `denied` is sticky — the page cannot ask
   again, only the user can undo it in site settings.
2. **The push service** (Google, Mozilla, Apple) issues the subscription.
3. **Our API** stores it so the scheduler can reach the device later.

A subscription the push service answers with **404 or 410** is gone for good — the browser
was cleared or the app uninstalled — and is deleted immediately. Softer failures are counted,
and the subscription is dropped after five consecutive ones, so a phone that was off for a
night is not discarded.

---

## Preferences

A row per user, created on first read. Absence of a row means defaults, so accounts that
predate this feature behave sensibly and nothing has to be backfilled.

**Always on, not configurable:**

- The **in-app bell** for every category. It is the record of what happened; muting it would
  mean events with no trace anywhere the scholar can reach.
- The **System** category on every channel. Nobody gets to switch off being told their
  password changed.
- **Minigame invites are never emailed.** An invite expires in three minutes, so an email
  about one is guaranteed to arrive after it is worthless.

**Defaults worth knowing:** journal email and journal push are on; everything else on push is
off. Granting notification permission is not the same as asking for every category, and push
is the channel people uninstall you over.

### Quiet hours

Default 22:00–08:00 in the user's own zone. Only email and push are held — the in-app entry
appears immediately, because otherwise somebody opening the app at 07:00 would see nothing
and be told about it an hour later.

The overnight window wraps midnight, which a naive `hour >= start && hour < end` gets wrong
for every hour of the night. An unparseable time zone falls back to UTC rather than throwing:
a bad preference value must never be able to stop a send.

### Collapsing

Notifications sharing a `CollapseKey` merge if they arrive within six hours while still
unread. A well-liked scholar recognised five times in an afternoon sees one line, not five —
recognition arriving as a burst of identical rows reads as noise. Collapsing deliberately
does **not** re-trigger the email; only the first one is sent.

---

## Announcements

`/admin/announcements`, available to Admin and Program Manager.

Preview-then-send, the same shape as bulk promotion and firm import. The send button stays
disabled until a preview has been run, and **any edit to the form invalidates the preview** —
otherwise somebody could preview "12 juniors", widen the filter, and send to 300 people while
still looking at the number 12.

Audience filters are ANDed, which is what a form implies: ticking Senior *and* Mentor means
seniors who are mentors, not seniors plus every mentor.

The preview counts email and push recipients **after** applying each person's preferences, so
it reports what will actually be sent rather than an optimistic total.

Action links must be **relative paths**. An absolute URL would turn the compose box into an
open redirect that any staff account could point anywhere.

This replaces a string that was compiled into the frontend — "The Gaming Update is live" was
seeded into any account with an empty notification cache, so it kept arriving for scholars
who joined long after that release, and came back for anyone who cleared their browser data.

---

## Realtime

`NotificationsHub` at `/hubs/notifications` (and `/api/hubs/notifications` — the frontend's
base URL carries `/api` on some deployments and not others).

Deliberately separate from `MinigamesHub`. That hub holds per-connection game state in static
dictionaries and broadcasts on a 150 ms throttle during a duel; putting the bell menu on the
same connection would make every scholar pay for machinery they may never use, and a bug in
duel cleanup could take notifications down with it.

Delivery is server-to-client only, addressed with `Clients.User(...)`. A scholar signed in on
a laptop and a phone gets both updated, with no group bookkeeping to leak on a dropped
connection.

If the hub cannot connect at all — a proxy that blocks websockets, say — the client falls
back to polling once a minute. The bell is then stale by at most a minute rather than forever.

---

## Troubleshooting

**"Nothing is being sent."** Check `NOTIFICATIONS_SCHEDULER_ENABLED`, then check that an
email provider is configured at all (`/admin/operations` → Configuration). The scheduler
waits two minutes after start-up before its first tick, so migrations and seeding finish
first.

**"Some people get email, most do not."** Expected if their preferences say so — the preview
counts reflect this. Also check the suppression list: inactive accounts are suppressed at
dispatch, deliberately and silently, and appear in the log at Debug rather than Warning.

**"Push works for me but not on my phone."** Push subscriptions are per-device. Each device
has to be enabled separately from `/notifications`.

**"The deadline shows a different day than I expect."** Check `JOURNAL_TIMEZONE`. An unknown
value logs an error and falls back to UTC, which shifts the deadline by up to two hours.
