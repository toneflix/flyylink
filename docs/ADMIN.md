# Admin CLI

`flyylink-cli` manages FlyyLink from your own machine: users, licences, settings and notices. It talks to the website's worker at `/api/admin/run`, so nothing needs the Cloudflare dashboard or `wrangler d1 execute`.

## Install

Needs Node 22 or later.

```sh
npm install -g @toneflix/flyylink-cli
```

Update it with the same command. Releasing a new version is in [RELEASING.md](RELEASING.md#admin-cli).

## Connect

1. Run `flyylink-cli login`. It makes a key for this machine and prints its public key. Add `--url=` to use another site, such as a preview.
2. **First operator:** set the public key as a worker secret and deploy:

   ```sh
   cd web
   npx wrangler secret put ADMIN_BOOTSTRAP_KEY
   ```

   Then make it permanent with `flyylink-cli operators:add <your name> <public key>`. The bootstrap key stops working once an operator exists, so you can delete the secret afterwards.

   **Later operators:** an existing operator runs `flyylink-cli operators:add <name> <their public key>`.

3. Check with `flyylink-cli status` and `flyylink-cli stats`.

The key lives in `~/.config/flyylink-cli/config.json` (mode 600). Set `FLYYLINK_CLI_CONFIG` to keep another file, such as one per site.

## Use

```sh
flyylink-cli wizard                          # pick a task from a menu
flyylink-cli commands                        # every command
flyylink-cli users:list --search=gmail       # pick a user, then what to do with them
flyylink-cli licences:grant ada@example.com
flyylink-cli settings:set max_computers 6
flyylink-cli notify:send licensed --channel=both --subject="New in 2.0" --message="Line one\nLine two"
flyylink-cli <command> --help
```

On a terminal, missing input is asked for and listings let you pick a row and an action. Pass `-n` to skip questions, or `--json` for the raw result.

| Area      | Commands                                                                                  |
| --------- | ----------------------------------------------------------------------------------------- |
| Users     | `users:list`, `users:show`, `users:sign-out`, `users:delete`, `computers:remove`          |
| Licences  | `licences:list`, `licences:grant`, `licences:revoke`, `licences:restore`                  |
| Settings  | `settings:list`, `settings:set <key> <value>`, `settings:unset <key>`                     |
| Notices   | `notify:send <to>`, `notify:continue <id>`, `notify:list`                                 |
| Operators | `operators:add`, `operators:list`, `operators:revoke`, `audit:list`, `stats`              |

`users:delete` cancels the user's Paddle subscriptions straight away, then deletes them. Their Paddle orders don't come back from a late webhook or a later sign-in, but a new purchase works as usual. If Paddle won't cancel a subscription, the user isn't deleted.

## Settings

Settings are stored in D1 and override the worker's defaults until unset.

| Key                  | Default                    |
| -------------------- | -------------------------- |
| `max_computers`      | 5                          |
| `removals_per_month` | 3                          |
| `review_emails`      | `REVIEW_EMAILS`            |

Trial length is set on the Paddle prices, not here.

## Notices

`notify:send <to>` takes `all`, `licensed`, `unlicensed`, or emails separated by commas. `--channel` picks how it goes:

- `email` (default): the designed email template, sent through SendPulse in batches of 20. The CLI keeps going until everyone has it; if it stops, `notify:continue <id>` carries on.
- `app`: a notification in the Mac and Windows apps, shown when each computer next checks its licence (every few hours). Licensed computers count as `licensed`; signed-out computers count as `unlicensed`. Notices older than 30 days, or sent before the app was installed, aren't shown. Phones never get them.
- `both`: each of the above.

`--link` adds a button. `--dry-run` counts recipients without sending.

## Security

Each request is signed with the machine's Ed25519 key over the method, path, time, a single-use nonce and the body. The worker refuses requests more than 5 minutes off its clock, reused nonces, revoked keys and unknown commands. Every command is recorded under the operator's name (`audit:list`).
