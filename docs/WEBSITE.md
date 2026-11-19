# Website hosting

The site (`web/`) is a static Next.js export served by a Cloudflare Worker named `flyylink-web`. For local work, see [web/README.md](../web/README.md).

## Deploying with Cloudflare

Cloudflare builds and deploys the site on every push to `main`. To set it up, open **Workers & Pages > Create > Import a repository** in the Cloudflare dashboard, pick `toneflix/flyylink`, and fill in:

| Setting        | Value                                                                                |
| -------------- | ------------------------------------------------------------------------------------ |
| Project name   | `flyylink-web` (must match `name` in `wrangler.jsonc`)                               |
| Build command  | `npm run build`                                                                      |
| Deploy command | `npx wrangler d1 migrations apply flyylink-licences --remote && npx wrangler deploy`  |
| Root directory | `web`                                                                                |

Under **Advanced settings**, set **Build watch paths** to `web/*` so changes to the apps don't rebuild the site. Pull requests get a preview build automatically.

When it's live, add a custom domain under the Worker's **Settings > Domains & Routes**, then update `url` in `content/site.ts`.

## The worker

The worker in `web/worker/` runs before the static files for `/download/*`, `/releases/*`, `/updates/*` and `/api/*`.

- **Downloads** come from the `flyylink-releases` R2 bucket, which the [release workflow](RELEASING.md) fills.
- **Licences** use the `flyylink-licences` D1 database (`web/migrations/`) and the `/api/*` routes. See [LICENCES.md](LICENCES.md).
- **Admin** commands arrive at `/api/admin/run` from `flyylink-cli`, and the computer apps fetch notices from `/api/notices`. See [ADMIN.md](ADMIN.md).
