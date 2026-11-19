# Licences

The Android app is free on phones, tablets and TVs. The Mac and Windows apps need a licence: monthly, annual or lifetime. Monthly and annual can start with a free trial, which Paddle runs. How the apps check it is in [ARCHITECTURE.md](ARCHITECTURE.md#licence).

## How it works

- **Licences** live in the `flyylink-licences` D1 database (`web/migrations/`). People buy through Paddle; its webhook (`/api/paddle/webhook`) adds, renews, ends or revokes the licence. A lifetime purchase (`transaction.completed`) never ends. A subscription (`subscription.*`) follows Paddle's status alone: active, trialing and past due (while Paddle retries a payment) let computers in; canceled and paused don't. A trial whose first payment fails doesn't count while Paddle retries it. A refund or chargeback (`adjustment.*`) revokes either. If a webhook never arrives, signing in (or opening the account page) without a licence asks Paddle's API for that email's subscriptions and purchases and saves what it finds. Webhooks the worker turns away are logged under the worker's Observability tab.
- **Trials** are set on the monthly and annual prices in Paddle. The buy page shows each price's trial and starts it through the normal checkout, which takes a card and charges it when the trial ends. Lifetime is a one-time price and can't have one. Paddle sends `subscription.trialing` when a trial starts, `subscription.activated` when it converts and `subscription.canceled` when it's cancelled.
- **Plans** are in `web/worker/licence/plans.ts`. A plan is on sale when its Paddle price is set, and the buy page shows the prices Paddle gives for the visitor's country.
- **Sign-in** uses a code emailed through SendPulse (Nodemailer with `nodemailer-sendpulse-transport`), on the site and in the computer apps.
- **Computers** that sign in get a token signed with `LICENCE_SIGNING_KEY`, which the app checks offline and renews every few hours.
- **Rules** (computers per licence, removals a month, 30 days offline plus 7 of grace) are in `web/worker/licence/licensing.ts`. The first two are the `max_computers` and `removals_per_month` settings (5 and 3 by default), changed with `flyylink-cli settings:set`; the buy page, account page and apps show whatever they are set to. A subscription's token asks to renew when its Paddle period ends.

## Setup, once

Run the `wrangler` commands from `web/`.

1. **Licence keys.** Run `node scripts/licence-keys.mjs`. Put the private key in the worker with `npx wrangler secret put LICENCE_SIGNING_KEY`, and add the public key as the `LICENCE_PUBLIC_KEY` variable under GitHub > Settings > Secrets and variables > Actions > Variables. Release builds of the Mac and Windows apps only check licences when this variable is set.
2. **Paddle.** Under Catalog, add a FlyyLink product with three prices: monthly (recurring every month), annual (recurring every year) and lifetime (one-time). Add their `pri_…` IDs as the `PADDLE_PRICE_MONTHLY`, `PADDLE_PRICE_ANNUAL` and `PADDLE_PRICE_LIFETIME` variables under the worker's Settings > Variables and Secrets. Leave one out to stop selling that plan. An older `PADDLE_PRICE_ID` still counts as the lifetime price. For a free trial, turn on the trial period on the monthly and annual prices and set its length; change or remove it there at any time. Under Retain > Payment recovery (or the default retries without Retain), choose to cancel the subscription when retries run out. Under Developer tools:
   - Authentication: make a client-side token and add it as the `PADDLE_CLIENT_TOKEN` variable the same way, and an API key with customer read and customer portal session write access for `npx wrangler secret put PADDLE_API_KEY`.
   - Notifications: add a destination for `https://flyylink.toneflix.net/api/paddle/webhook` with the events `transaction.completed`, `adjustment.created`, `adjustment.updated` and every `subscription.*` event. Put its secret key in `npx wrangler secret put PADDLE_WEBHOOK_SECRET`.
   - Checkout settings: approve `flyylink.toneflix.net` as a website, and set the default payment link to `https://flyylink.toneflix.net/buy`.
   - Start with a sandbox account (`PADDLE_SANDBOX` set to `"true"`). When going live, repeat the steps in the live account and set it to `"false"`.
3. **SendPulse.** Under Settings > API, make an API key and put it in `npx wrangler secret put SENDPULSE_API_KEY`. Under SMTP, verify the `MAIL_FROM` sender in `wrangler.jsonc`.
4. **Sign-in codes.** `npx wrangler secret put CODE_SECRET` with any long random text.
5. **Cloudflare deploys.** Use the deploy command in [WEBSITE.md](WEBSITE.md) so new migrations run before each deploy.
6. **Release uploads.** Add the Cloudflare secrets listed in [RELEASING.md](RELEASING.md).

## Free licences

Grant a lifetime licence without a purchase (for a reviewer, a tester or a refund you want to honour) with the [admin CLI](ADMIN.md). The email gets an account if it has none.

```sh
flyylink-cli licences:grant reviewer@example.com
```

Revoke it with `licences:revoke <id>`, taking the id from `licences:list` (computers lose access when their token next renews, within a few hours).

## App store reviewers

Reviewers can't receive the emailed code, so listed emails can sign in with a fixed code instead. Other emails are unaffected.

1. Give the reviewer email a free licence (above).
2. List the email with `flyylink-cli settings:set review_emails reviewer@example.com` (comma-separated for several). The `REVIEW_EMAILS` variable still works as the default.
3. `npx wrangler secret put REVIEW_CODE` with a code of at least 8 characters. Shorter or unset turns the bypass off.

No email is sent to a listed address; the fixed code is the only way in. To end it, delete `REVIEW_CODE` (or take the email off `review_emails`) and revoke the licence.

## Going live, in this order

1. Let Cloudflare deploy the site with the licence server.
2. Buy each plan in the Paddle sandbox and sign in on a Mac and a PC to check the whole flow. Start a monthly trial, check the account page says it's free until the trial ends, then cancel it from the account page and check its computers lock.
3. Switch Paddle to live (step 2 above).
4. Tag a release. It uploads to R2 and builds the apps with the licence key, and installed copies update from the website from then on.
5. Make the GitHub repo private. Copies installed before step 4 can no longer update after this.

## Local testing

Copy `web/.dev.vars.example` to `web/.dev.vars` and run `npm run preview`. Sign-in codes are printed instead of emailed.
