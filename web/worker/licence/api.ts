import { HttpError, readJson, requireString } from "../http";
import type { Router } from "../router";
import { SignInCodes } from "./codes";
import { Licensing, type Machine } from "./licensing";
import type { PaddleApi, PaddleWebhook } from "./paddle";
import type { Plan, Plans } from "./plans";
import type { ReviewAccess } from "./review";
import type { Sessions } from "./sessions";
import type { Account, LicenceStore } from "./store";

export interface CheckoutConfig {
  clientToken: string;
  sandbox: boolean;
}

const now = () => Math.floor(Date.now() / 1000);
const platforms = ["mac", "windows"];

/** The licence endpoints the computer apps and the website's buy and account pages call. */
export class LicenceApi {
  constructor(
    private readonly store: LicenceStore,
    private readonly licensing: Licensing,
    private readonly codes: SignInCodes,
    private readonly sessions: Sessions,
    private readonly paddle: PaddleWebhook,
    private readonly paddleApi: PaddleApi,
    private readonly plans: Plans,
    private readonly checkout: CheckoutConfig,
    private readonly review: ReviewAccess,
  ) {}

  register(router: Router): void {
    router
      .get("/api/checkout", () => this.checkoutConfig())
      .post("/api/paddle/webhook", (request) => this.paddle.handle(request, now()))
      .post("/api/code", (request) => this.sendCode(request))
      .post("/api/activate", (request) => this.activate(request))
      .post("/api/refresh", (request) => this.refresh(request))
      .post("/api/trial", () => this.trial())
      .post("/api/session", (request) => this.signIn(request))
      .post("/api/session/end", (request) => this.signOut(request))
      .get("/api/account", (request) => this.account(request))
      .post("/api/account/computers/:machine/remove", (request, { machine }) => this.remove(request, machine))
      .post("/api/account/billing", (request) => this.billing(request));
  }

  private async checkoutConfig(): Promise<Response> {
    const prices: Partial<Record<Plan, string>> = this.plans.onSale();
    if (!this.checkout.clientToken || Object.keys(prices).length === 0) throw new HttpError(503, "not_open");

    return Response.json({ ...this.checkout, prices, limit: await this.licensing.limit() });
  }

  private async sendCode(request: Request): Promise<Response> {
    const { email } = await readJson<{ email?: unknown }>(request);
    const address = SignInCodes.normalise(requireString(email, "email"));
    if (!(await this.review.covers(address))) await this.codes.send(address, now());

    return new Response(null, { status: 204 });
  }

  private async activate(request: Request): Promise<Response> {
    const body = await readJson<{ email?: unknown; code?: unknown; machine?: unknown }>(request);
    const account = await this.verify(body.email, body.code);

    return Response.json(await this.licensing.activate(account, LicenceApi.machine(body.machine), now()));
  }

  private async refresh(request: Request): Promise<Response> {
    const { machine, secret } = await readJson<{ machine?: unknown; secret?: unknown }>(request);
    const token = await this.licensing.refresh(requireString(machine, "machine", 128), requireString(secret, "secret"), now());

    return Response.json({ token });
  }

  /** Older apps ask for a trial of their own; trials now start with a Paddle checkout. */
  private trial(): Response {
    throw new HttpError(402, "trial_over");
  }

  private async signIn(request: Request): Promise<Response> {
    const { email, code } = await readJson<{ email?: unknown; code?: unknown }>(request);
    const account = await this.verify(email, code);

    return new Response(null, { status: 204, headers: { "Set-Cookie": await this.sessions.start(account, now()) } });
  }

  private async signOut(request: Request): Promise<Response> {
    return new Response(null, { status: 204, headers: { "Set-Cookie": await this.sessions.end(request) } });
  }

  private async account(request: Request): Promise<Response> {
    const account = await this.sessions.account(request, now());
    await this.catchUp(account);
    const licence = await this.licensing.licence(account);

    return Response.json({
      email: account.email,
      licensed: licence !== null,
      licence,
      computers: await this.licensing.computers(account),
      limit: await this.licensing.limit(),
      removalsLeft: await this.licensing.removalsLeft(account, now()),
    });
  }

  private async remove(request: Request, machine: string): Promise<Response> {
    const account = await this.sessions.account(request, now());
    await this.licensing.remove(account, machine, now());

    return new Response(null, { status: 204 });
  }

  /** Sends the person to Paddle to cancel their subscription or change how they pay. */
  private async billing(request: Request): Promise<Response> {
    const account = await this.sessions.account(request, now());
    const licence = await this.store.currentLicence(account.id);
    if (!licence?.customer_id || licence.plan === "lifetime" || licence.provider !== "paddle") throw new HttpError(404, "no_subscription");

    return Response.json({ url: await this.paddleApi.portal(licence.customer_id, licence.order_id) });
  }

  /** Checks the emailed code, or a reviewer's fixed one; an email that never bought anything still gets an account. */
  private async verify(email: unknown, code: unknown) {
    const address = SignInCodes.normalise(requireString(email, "email"));
    const entered = requireString(code, "code", 64);
    if (!(await this.review.accepts(address, entered))) await this.codes.verify(address, entered, now());

    const account = await this.store.accountFor(address, now());
    await this.catchUp(account);

    return account;
  }

  /** Asks Paddle for an unlicensed account's plans, so a missed webhook doesn't keep someone who paid locked out. */
  private async catchUp(account: Account): Promise<void> {
    if (await this.store.currentLicence(account.id)) return;

    try {
      await this.paddle.catchUp(account, now());
    } catch (error) {
      console.error(error);
    }
  }

  private static machine(value: unknown): Machine {
    const machine = (value ?? {}) as Record<string, unknown>;
    const platform = requireString(machine.platform, "platform", 16);
    if (!platforms.includes(platform)) throw new HttpError(400, "bad_request", { field: "platform" });

    return { id: requireString(machine.id, "machine", 128), name: requireString(machine.name, "name", 100), platform };
  }
}
