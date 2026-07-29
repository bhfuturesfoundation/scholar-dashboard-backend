# Email Providers — Setup Guide

The API can send email through several services. You pick one as the default, and
optionally a fallback chain. Nothing about the application code changes when you switch —
only environment variables.

All variables are read from the process environment. In local development the API loads a
`.env` file from the repo root via `DotNetEnv`, so you can put them there. In production
(Railway, Vercel, Docker) set them as real environment variables.

**After changing any of these, restart the API.** They are read once at startup.

---

## Quick start

The absolute minimum to get sending working — plain SMTP with a Gmail app password:

```bash
EMAIL_PROVIDER=smtp
EMAIL_FROM_ADDRESS=partnerships@bhfuturesfoundation.org
EMAIL_FROM_NAME=BH Futures Foundation
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_ENABLE_SSL=true
SMTP_USERNAME=partnerships@bhfuturesfoundation.org
SMTP_PASSWORD=your-16-char-app-password
SMTP_FROM_EMAIL=partnerships@bhfuturesfoundation.org
```

Verify it worked: sign in as a partner member, open **Settings** in the partner portal, and
confirm the provider shows **Ready**. Then use **Send test to myself** on the Compose page.

---

## Choosing a provider

| Provider | Key | Cost | Good for | Watch out for |
|---|---|---|---|---|
| SMTP | `smtp` | Free | Any relay you already own; Gmail/Workspace | Gmail caps ~500/day and needs an app password |
| GMass | `gmass` | Free tier + paid | Gmail-based bulk sending with tracking | Sends via your Gmail account, so Gmail's quota still applies |
| Mailchimp Transactional | `mailchimp` | Paid | Large volume, best deliverability and reporting | Must be *Transactional* (Mandrill), not Mailchimp Marketing |
| Resend | `resend` | 3k/month free, then paid | Modern API, simple domain setup | Domain must be DNS-verified before it will send |
| EmailJS | `emailjs` | 200/month free | Small volumes, quickest to set up | Server-side sends need the **private key** — see below |
| Log only | `log` | — | Local development with no credentials | Delivers nothing; writes to the API log |

---

## Global settings

These apply regardless of which provider is active.

| Variable | Default | What it does |
|---|---|---|
| `EMAIL_PROVIDER` | `smtp` | Provider key used when a message doesn't name one |
| `EMAIL_FROM_ADDRESS` | falls back to `SMTP_FROM_EMAIL` | Default from-address |
| `EMAIL_FROM_NAME` | falls back to `SMTP_FROM_NAME` | Default display name |
| `EMAIL_REPLY_TO` | *(none)* | Reply-To header on every message |
| `EMAIL_ENABLE_FALLBACK` | `false` | Retry a failed send on the next configured provider |
| `EMAIL_FALLBACK_ORDER` | *(registration order)* | Comma-separated keys to try, e.g. `resend,smtp` |
| `EMAIL_SANDBOX_REDIRECT_TO` | *(none)* | Redirect **all** mail to this address — see Sandbox mode |
| `EMAIL_SEND_DELAY_MS` | `0` | Pause between messages in a bulk send |
| `EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN` | `500` | Refuse to send to a larger audience than this |

### Fallback

Only *transient* failures fall through to the next provider — a timeout, a 5xx, or a rate
limit. A permanent failure (malformed address, rejected sending domain) stops immediately,
because retrying it elsewhere would fail identically and just multiply the delay.

```bash
EMAIL_PROVIDER=resend
EMAIL_ENABLE_FALLBACK=true
EMAIL_FALLBACK_ORDER=smtp,gmass
```

### Sandbox mode

Set `EMAIL_SANDBOX_REDIRECT_TO` and every outbound email goes to that one address instead
of the real recipient, with the intended recipient prefixed onto the subject line:

```
[SANDBOX → amina@example.org] Welcome to the FLS 2026 Speaker Portal
```

Use this on staging, and any time you want to rehearse a broadcast against real speaker
data without mailing real speakers. The partner portal shows a blue banner whenever it is
active, so nobody sends a campaign believing it went out.

### Pacing

Free tiers throttle hard. `EMAIL_SEND_DELAY_MS=1500` puts 1.5 seconds between messages,
which keeps a 200-person broadcast inside most rate limits at the cost of ~5 minutes of
wall time. Leave it at `0` for Mailchimp or Resend, which are built for bursts.

---

## SMTP (`smtp`)

```bash
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_ENABLE_SSL=true
SMTP_USERNAME=you@yourdomain.org
SMTP_PASSWORD=your-app-password
SMTP_FROM_EMAIL=you@yourdomain.org
SMTP_FROM_NAME=BH Futures Foundation
```

**Gmail / Google Workspace:** your normal password will not work. Turn on 2-Step
Verification, then generate an **App Password** at
<https://myaccount.google.com/apppasswords> and use that as `SMTP_PASSWORD`.

Port 587 with `SMTP_ENABLE_SSL=true` means STARTTLS, which is what you want. Port 465
(implicit TLS) is not supported by .NET's `SmtpClient`.

---

## GMass (`gmass`)

GMass exposes an SMTP relay, so setup is credentials rather than code.

```bash
GMASS_API_KEY=your-gmass-api-key
GMASS_FROM_EMAIL=you@yourdomain.org
GMASS_FROM_NAME=BH Futures Foundation
```

Optional overrides — the defaults are correct for GMass and you shouldn't normally set these:

```bash
GMASS_SMTP_HOST=smtp.gmass.co
GMASS_SMTP_PORT=587
GMASS_SMTP_USERNAME=gmass
```

Get the API key from the GMass dashboard under **Settings → API Keys**. The username is
the literal string `gmass` — that is not a placeholder. The from-address must be the Gmail
account connected to your GMass subscription.

---

## Mailchimp Transactional (`mailchimp`)

```bash
MAILCHIMP_TRANSACTIONAL_API_KEY=your-mandrill-api-key
MAILCHIMP_FROM_EMAIL=partnerships@bhfuturesfoundation.org
MAILCHIMP_FROM_NAME=BH Futures Foundation
MAILCHIMP_SUBACCOUNT=fls        # optional, for separate reporting
```

This is **Mailchimp Transactional**, formerly Mandrill — a separate paid add-on from
Mailchimp Marketing. Marketing is list-and-campaign based and is the wrong tool for
"email these twelve speakers now"; Transactional sends individual messages on demand.

Get the key from <https://mandrillapp.com/settings>. The from-address must be on a domain
you have verified in Mailchimp, otherwise every send is rejected with a `rejected` status
even though the HTTP call returns 200.

---

## Resend (`resend`)

```bash
RESEND_API_KEY=re_your_api_key
RESEND_FROM_EMAIL=partnerships@bhfuturesfoundation.org
RESEND_FROM_NAME=BH Futures Foundation
```

Add and verify your domain at <https://resend.com/domains> first — this means adding the
DNS records they give you. Until verification completes, sends fail with a 403.

---

## EmailJS (`emailjs`)

```bash
EMAILJS_SERVICE_ID=service_xxxxxxx
EMAILJS_TEMPLATE_ID=template_xxxxxxx
EMAILJS_PUBLIC_KEY=your_public_key
EMAILJS_PRIVATE_KEY=your_private_key
```

Two things trip people up here, and both produce silent-looking failures:

**1. The private key is mandatory.** EmailJS blocks API calls that don't come from a
browser unless you send an access token. Without `EMAILJS_PRIVATE_KEY` every send fails
with *"API calls are disabled for non-browser applications"*. You also have to tick
**Allow EmailJS API for non-browser applications** in
Account → Security on the EmailJS dashboard.

**2. Your EmailJS template controls the layout, not this app.** The provider passes the
message as template parameters, so your template must reference them:

| Parameter | Use in template as |
|---|---|
| `subject` | `{{subject}}` |
| `message_html` | `{{{message_html}}}` — **three** braces |
| `message` | `{{message}}` (plain-text fallback) |
| `to_email` / `email` | recipient address |
| `to_name` | recipient name |

`message_html` needs triple braces. With two, EmailJS escapes the HTML and your recipients
see raw `<p>` tags in the message body.

---

## Log only (`log`)

```bash
EMAIL_PROVIDER=log
```

Writes each message to the application log and reports success. Nothing is delivered. Use
it to exercise the full compose → preview → send → history flow locally without any vendor
account. It is never selected automatically — the dispatcher will only route here if you
explicitly choose it or list it in `EMAIL_FALLBACK_ORDER`.

---

## Seeded accounts

| Variable | Default | Purpose |
|---|---|---|
| `SEED_PARTNER_MEMBER_PASSWORD` | `Admin1234!` | Password for the seeded `partnerships@bhfuturesfoundation.org` account |

The partner account is created on startup if it doesn't exist. If it already exists the
seeder leaves the password alone and only re-asserts the `PartnerMember` role — so setting
this variable after first boot has no effect. Change the password through the app instead.

---

## Troubleshooting

**Settings shows every provider as "Not set up"**
The API can't see your variables. On Railway/Vercel confirm they're set on the *API*
service, not the frontend, and that you redeployed. Locally, confirm `.env` is at the repo
root where `Env.TraversePath().Load()` will find it.

**"No email provider is configured" when sending**
No provider passed its configuration check. The Settings page lists exactly which variable
each one is missing.

**Emails send but never arrive**
Check spam first, then confirm the from-domain is verified with the provider. An unverified
domain is the single most common cause — Mailchimp and Resend both accept the API call and
drop the message.

**Everything goes to one inbox**
`EMAIL_SANDBOX_REDIRECT_TO` is set. Unset it and restart.

**A campaign shows failures**
Open the campaign in **Sent Mail** — each failed recipient shows the provider's own error
message. Fix the cause and use **Retry failed**, which re-sends only those recipients.
