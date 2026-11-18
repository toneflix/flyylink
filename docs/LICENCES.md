# Licences

The Android app is free. The Mac and Windows apps need a licence after a 14-day trial: monthly, annual or lifetime. How the apps check it is in [ARCHITECTURE.md](ARCHITECTURE.md#licence).

## How it works

- **Licences** live in the `flyylink-licences` D1 database (`web/migrations/`). People buy through Paddle; its webhook (`/api/paddle/webhook`) adds, renews, ends or revokes the licence. A lifetime purchase (`transaction.completed`) never ends. A subscription (`subscription.*`) stays active while Paddle says it is active, trialing or past due, and its computers stop 3 days after the paid period if no renewal arrives. A refund or chargeback (`adjustment.*`) revokes either.
- **Plans** are in `web/worker/licence/plans.ts`. A plan is on sale when its Paddle price is set, and the buy page shows the prices Paddle gives for the visitor's country.
- **Sign-in** uses a code emailed through SendPulse (Nodemailer with `nodemailer-sendpulse-transport`), on the site and in the computer apps.
- **Computers** that sign in get a token signed with `LICENCE_SIGNING_KEY`, which the app checks offline and renews every few hours.
- **Rules** (5 computers, 3 removals a month, 14-day trial, 30 days offline plus 7 of grace) are in `web/worker/licence/licensing.ts`. The first three can be changed with `flyylink-cli settings:set`.

## Setup, once

Run the `wrangler` commands from `web/`.

1. **Licence keys.** Run `node scripts/licence-keys.mjs`. Put the private key in the worker with `npx wrangler secret put LICENCE_SIGNING_KEY`, and add the public key as the `LICENCE_PUBLIC_KEY` variable under GitHub > Settings > Secrets and variables > Actions > Variables. Release builds of the Mac and Windows apps only check licences when this variable is set.
2. **Paddle.** Under Catalog, add a FlyyLink product with three prices: monthly (recurring every month), annual (recurring every year) and lifetime (one-time). Add their `pri_…` IDs as the `PADDLE_PRICE_MONTHLY`, `PADDLE_PRICE_ANNUAL` and `PADDLE_PRICE_LIFETIME` variables under the worker's Settings > Variables and Secrets. Leave one out to stop selling that plan. An older `PADDLE_PRICE_ID` still counts as the lifetime price. Under Developer tools:
   - Authentication: make a client-side token and add it as the `PADDLE_CLIENT_TOKEN` variable the same way, and an API key with customer read and customer portal session write access for `npx wrangler secret put PADDLE_API_KEY`.
   - Notifications: add a destination for `https://flyylink.toneflix.net/api/paddle/webhook` with the events `transaction.completed`, `adjustment.created`, `adjustment.updated` and every `subscription.*` event. Put its secret key in `npx wrangler secret put PADDLE_WEBHOOK_SECRET`.
   - Checkout settings: approve `flyylink.toneflix.net` as a website, and set the default payment link to `https://flyylink.toneflix.net/buy`.
   - Start with a sandbox account (`PADDLE_SANDBOX` set to `"true"`). When going live, repeat the steps in the live account and set it to `"false"`.
3. **SendPulse.** Under Settings > API, make an API key and put it in `npx wrangler secret put SENDPULSE_API_KEY`. Under SMTP, verify the `MAIL_FROM` sender in `wrangler.jsonc`.
4. **Trial length (optional).** Add a `TRIAL_DAYS` build variable under the worker's Settings > Build > Variables and secrets to change the 14 days. The site copy uses it at build time, and the deploy command hands it to the worker. New trials get the new length; trials already running keep their end date. The apps only use their own 14 days until they first reach the server.
5. **Sign-in codes.** `npx wrangler secret put CODE_SECRET` with any long random text.
6. **Cloudflare deploys.** Use the deploy command in [WEBSITE.md](WEBSITE.md) so new migrations run before each deploy.
7. **Release uploads.** Add the Cloudflare secrets listed in [RELEASING.md](RELEASING.md).

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
2. Buy each plan in the Paddle sandbox and sign in on a Mac and a PC to check the whole flow. Cancel the monthly one from the account page and check its computers lock after the period ends.
3. Switch Paddle to live (step 2 above).
4. Tag a release. It uploads to R2 and builds the apps with the licence key, and installed copies update from the website from then on.
5. Make the GitHub repo private. Copies installed before step 4 can no longer update after this.

## Local testing

Copy `web/.dev.vars.example` to `web/.dev.vars` and run `npm run preview`. Sign-in codes are printed instead of emailed.
