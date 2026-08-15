# FLS 2026 outreach — setup and runbook

Everything the partnerships team needs to run the FLS 2026 company campaign from the app.

For the full list of email-provider variables see [EMAIL_PROVIDERS.md](EMAIL_PROVIDERS.md).
This document is about **this campaign**: what is already loaded, which provider to use, and
how to actually send.

---

## What is already in the production database

| | Count | Notes |
|---|---|---|
| Companies | **561** | From `Lista kompanija - Sheet1.csv` (570 rows, 9 duplicate addresses collapsed) |
| …with a CC address | 160 | Copied on every send — see [CC](#cc-addresses) |
| …with a hand-checked salutation | 561 | Used verbatim as the greeting line |
| …with a firm type set | 97 | NGO, Hospital, University, Bank. The rest need [auto-categorising](#1-categorise-the-rest) |
| Templates | **7** | The four FLS Word documents, split into initial + follow-up |

Templates loaded, by id:

| Id | Template | Applies to |
|---|---|---|
| 3 | FLS 2026 — General — Initial | any firm |
| 4 | FLS 2026 — General — Follow-up | any firm |
| 5 | FLS 2026 — Healthcare — Initial | Hospital |
| 6 | FLS 2026 — Healthcare — Follow-up | Hospital |
| 7 | FLS 2026 — Insurance — Initial | Insurance |
| 8 | FLS 2026 — Insurance — Follow-up | Insurance |
| 9 | FLS 2026 — Returning partner | any firm — pick the FLS 2025 sponsors by hand |

The internal Bosnian working notes from the Word files (`OBAVEZNO U PRILOG PROSPEKT`,
`STAVIT LINK OD DIREKTOROVOG KALENDARA`, the checklist at the top of the general template)
were **not** imported. They are instructions to the sender, and shipping them would
eventually mean emailing them to a partner.

---

## Which email service to use

This is cold B2B outreach, which is a different problem from transactional email, and the
usual "just use Mailchimp" answer is wrong here.

### Recommendation: send through your own Google Workspace over SMTP

You already pay for it, it costs nothing extra, and it is the only option on this list where
the sending reputation is *yours* and the mail comes from a real person at a real domain —
which is what actually gets a cold partnership email opened and answered.

```bash
EMAIL_PROVIDER=smtp
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USERNAME=partnerships@bhfuturesfoundation.org
SMTP_PASSWORD=<16-character Google App Password, not the account password>
SMTP_FROM_EMAIL=partnerships@bhfuturesfoundation.org
SMTP_FROM_NAME=BH Futures Foundation
SMTP_ENABLE_SSL=true

# Google cuts you off past ~2,000 recipients/day on Workspace. Pacing keeps a 561-firm
# send inside the limit and looks far less like a blast.
EMAIL_SEND_DELAY_MS=4000
EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN=700
```

The App Password comes from Google Account → Security → 2-Step Verification → App passwords.
A normal password will not work once 2FA is on.

**At 4 seconds apart, 561 emails take about 37 minutes.** That is a feature, not a cost —
it is well inside Google's limits and reads as a human working through a list.

### If you outgrow that

| Service | Free tier | Paid | Verdict for this use |
|---|---|---|---|
| **Brevo** | 300/day (~9,000/mo) | €19/mo for 20k | Best free headroom. Explicitly allows B2B prospecting. |
| **Resend** | 3,000/mo | $20/mo for 50k | Already implemented, excellent API. Aimed at transactional; check their policy before a cold blast. |
| **Amazon SES** | — | $0.10 per 1,000 | Cheapest at volume, most setup, and you must warm the domain yourself. |
| **Mailchimp** | 500 contacts | from ~$13/mo | **Avoid for this.** Their terms prohibit emailing people who did not opt in, and a cold list can get the account suspended. |

Switching is one variable — `EMAIL_PROVIDER=brevo` etc. — plus that provider's keys. Nothing
in the campaign or template setup changes.

> **A word on the law.** These firms did not opt in. In the EU/EEA, B2B cold email is
> generally defensible under legitimate interest, but only if every message identifies who
> you are and offers a way to opt out. Keep the footer, honour every removal request
> immediately (mark the firm **Unsubscribed** in the app — it is then excluded from every
> future audience permanently), and do not import personal addresses of named individuals
> at companies without a reason to.

---

## Before the first send

### 1. Categorise the rest

464 firms have no type, because the source sheet only said "Company". The app can infer one
from the name, website and email domain:

**Mailing → Firms → Bulk actions → Auto-categorise**

It proposes a type per firm with a confidence score; review before applying. This is what
makes the healthcare and insurance templates target the right companies — without it,
everyone gets the general template.

### 2. Set the campaign variables

Two placeholders are filled per campaign rather than per firm:

| Placeholder | Put in |
|---|---|
| `{{meetingLink}}` | The director's Google calendar booking link |
| `{{senderName}}` / `{{senderRole}}` | Whoever is signing — appears above "BH Futures Foundation" |

`{{salutation}}`, `{{firmName}}` and the rest come from the firm record automatically.

### 3. Rehearse against real data

```bash
EMAIL_SANDBOX_REDIRECT_TO=you@bhfuturesfoundation.org
```

Every message goes to you instead, with the intended recipient prefixed onto the subject.
Run the whole 561-firm campaign this way once. The app shows a banner while it is on, so
nobody sends for real believing it was a rehearsal. **Remove the variable to go live.**

---

## Running a campaign

1. **Mailing → Campaigns → New**
2. Pick the template (id 3 for the general first touch).
3. Build the audience — filter by firm type so healthcare firms get template 5, insurance
   gets 7, and everyone else gets 3.
4. **Preview** — it renders against a real firm from the audience, not a sample.
5. **Send test** to yourself.
6. Attach the partnership prospectus PDF.
7. **Schedule** or send now.

Follow-ups are a second campaign against the same audience, using the follow-up template,
scheduled two weeks later. The app records `LastContactedAt` and `ContactCount` per firm, so
you can filter to "not contacted in 14 days" rather than tracking it in the sheet.

### CC addresses

160 firms have a CC — usually a general office inbox. The dispatcher copies it automatically
on every send; there is nothing to configure. Several organisations only ever reply when
that address is copied, which is why it is kept per firm rather than as one campaign-wide
address.

CC is supported on **SMTP** and **Resend**. Providers that cannot send a CC ignore it rather
than failing the message — losing the copy is a smaller harm than losing the email.

---

## Things that will bite

- **`EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN` defaults to 500** and you have 561 firms. A whole-list
  campaign is refused until you raise it. Set it to 700.
- **A bounced address is not retried forever.** Hard bounces mark the firm `Bounced` and
  exclude it from future audiences. Fix the address to bring it back.
- **Never send the same campaign twice to catch stragglers.** Build a new audience filtered
  on "no reply / not contacted recently" instead — the app tracks this per firm.
- **Attachments are per campaign, not per template.** The prospectus has to be attached each
  time. Every one of the four source templates says "prospectus attached"; if it is not,
  the email is worse than not sending it.
