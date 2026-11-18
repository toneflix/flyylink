import { AdminApi } from "./admin/api";
import { AuditLog } from "./admin/audit";
import { AdminAuthenticator } from "./admin/authenticator";
import { LicenceCommands } from "./admin/commands/licences";
import { NotifyCommands } from "./admin/commands/notify";
import { OperatorCommands } from "./admin/commands/operators";
import { SystemCommands } from "./admin/commands/system";
import { UserCommands } from "./admin/commands/users";
import { Operators } from "./admin/operators";
import { Downloads } from "./downloads";
import { HttpError } from "./http";
import { LicenceApi } from "./licence/api";
import { SignInCodes } from "./licence/codes";
import { Emails } from "./licence/emails";
import { Licensing } from "./licence/licensing";
import { LogMailer, SendPulseMailer } from "./licence/mailer";
import { PaddleApi, PaddleWebhook } from "./licence/paddle";
import { Plans } from "./licence/plans";
import { ReviewAccess } from "./licence/review";
import { Sessions } from "./licence/sessions";
import { TokenSigner } from "./licence/signer";
import { LicenceStore } from "./licence/store";
import { Notices } from "./notices";
import { Router } from "./router";
import { Settings } from "./settings";

export interface Env {
  ASSETS: Fetcher;
  RELEASES: R2Bucket;
  LICENCES: D1Database;

  PADDLE_CLIENT_TOKEN?: string;
  PADDLE_PRICE_MONTHLY?: string;
  PADDLE_PRICE_ANNUAL?: string;
  PADDLE_PRICE_LIFETIME?: string;
  /** The lifetime price from before monthly and annual plans; PADDLE_PRICE_LIFETIME replaces it. */
  PADDLE_PRICE_ID?: string;
  PADDLE_SANDBOX?: string;
  /** Trial length in days, set in the dashboard. */
  TRIAL_DAYS?: string;
  MAIL_FROM: string;
  /** The site's address, for links and images in emails. */
  SITE_URL: string;
  /** Set to "log" in .dev.vars to print sign-in codes instead of emailing them. */
  MAIL_MODE?: string;

  LICENCE_SIGNING_KEY: string;
  CODE_SECRET: string;
  PADDLE_WEBHOOK_SECRET: string;
  /** Comma-separated reviewer emails that sign in with REVIEW_CODE, until the review_emails setting replaces them. */
  REVIEW_EMAILS?: string;
  REVIEW_CODE?: string;
  /** The public key `flyylink-cli login` printed, accepted only until the first operator is added. */
  ADMIN_BOOTSTRAP_KEY?: string;
  PADDLE_API_KEY: string;
  SENDPULSE_API_KEY: string;
}

function router(env: Env): Router {
  const router = new Router();
  const store = new LicenceStore(env.LICENCES);
  const sandbox = env.PADDLE_SANDBOX === "true";
  const mailer = env.MAIL_MODE === "log" ? new LogMailer() : new SendPulseMailer(env.SENDPULSE_API_KEY, env.MAIL_FROM);
  const emails = new Emails(env.SITE_URL);
  const settings = new Settings(env.LICENCES, {
    trial_days: String(Number(env.TRIAL_DAYS) || Licensing.defaultTrialDays),
    max_computers: String(Licensing.maxComputers),
    removals_per_month: String(Licensing.removalsPerMonth),
    review_emails: env.REVIEW_EMAILS ?? "",
  });
  const licensing = new Licensing(store, new TokenSigner(env.LICENCE_SIGNING_KEY), settings);
  const notices = new Notices(env.LICENCES, store, mailer, emails);
  const operators = new Operators(env.LICENCES);
  const audit = new AuditLog(env.LICENCES);
  const plans = new Plans({ monthly: env.PADDLE_PRICE_MONTHLY, annual: env.PADDLE_PRICE_ANNUAL, lifetime: env.PADDLE_PRICE_LIFETIME || env.PADDLE_PRICE_ID });
  const paddleConfig = { webhookSecret: env.PADDLE_WEBHOOK_SECRET, apiKey: env.PADDLE_API_KEY, sandbox };
  const paddleApi = new PaddleApi(paddleConfig);

  new Downloads(env.RELEASES).register(router);
  new LicenceApi(
    store,
    licensing,
    new SignInCodes(env.LICENCES, mailer, emails, env.CODE_SECRET),
    new Sessions(env.LICENCES, store),
    new PaddleWebhook(paddleConfig, paddleApi, plans, store),
    paddleApi,
    plans,
    { clientToken: env.PADDLE_CLIENT_TOKEN ?? "", sandbox },
    new ReviewAccess(settings, env.REVIEW_CODE),
  ).register(router);
  notices.register(router);
  new AdminApi(new AdminAuthenticator(operators, env.ADMIN_BOOTSTRAP_KEY), audit, [
    new UserCommands(store, licensing),
    new LicenceCommands(store),
    new SystemCommands(settings, store),
    new NotifyCommands(notices),
    new OperatorCommands(operators, audit),
  ]).register(router);

  return router;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    try {
      return await router(env).handle(request, () => env.ASSETS.fetch(request));
    } catch (error) {
      if (error instanceof HttpError) return error.toResponse();

      console.error(error);

      return Response.json({ error: "server_error" }, { status: 500 });
    }
  },
} satisfies ExportedHandler<Env>;
