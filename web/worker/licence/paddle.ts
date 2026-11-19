import { HttpError } from "../http";
import { hmac, sameText } from "./crypto";
import { Plans } from "./plans";
import type { Account, LicenceStore } from "./store";

const provider = "paddle";
const maxSkew = 5 * 60;

interface PaddleItem {
  price?: { id?: string };
  price_id?: string;
}

/** A subscription, transaction or adjustment, as webhooks and the API both describe it. */
interface PaddleEntity {
  id: string;
  status?: string;
  action?: string;
  customer_id?: string;
  transaction_id?: string;
  subscription_id?: string | null;
  items?: PaddleItem[];
  current_billing_period?: { ends_at?: string } | null;
  scheduled_change?: { action?: string } | null;
  updated_at?: string;
  /** Refunds and chargebacks, on transactions listed with `include=adjustments`. */
  adjustments?: { action?: string; status?: string }[];
}

interface PaddleEvent {
  event_type: string;
  occurred_at?: string;
  data: PaddleEntity;
}

export interface PaddleConfig {
  webhookSecret: string;
  apiKey: string;
  sandbox: boolean;
}

/** The parts of Paddle's API the licence server calls. */
export class PaddleApi {
  constructor(private readonly config: PaddleConfig) {}

  async customerEmail(customerId: string): Promise<string> {
    const { email } = await this.call<{ email: string }>("GET", `/customers/${customerId}`);

    return email.trim().toLowerCase();
  }

  async customerIds(email: string): Promise<string[]> {
    return (await this.call<{ id: string }[]>("GET", `/customers?email=${encodeURIComponent(email)}`)).map((customer) => customer.id);
  }

  /** The customer's subscriptions that still let computers in, trials included. */
  subscriptions(customerId: string): Promise<PaddleEntity[]> {
    return this.call("GET", `/subscriptions?customer_id=${customerId}&status=active,trialing,past_due`);
  }

  /** The customer's completed transactions that weren't refunded or charged back. */
  async purchases(customerId: string): Promise<PaddleEntity[]> {
    const transactions = await this.call<PaddleEntity[]>("GET", `/transactions?customer_id=${customerId}&status=completed&include=adjustments`);

    return transactions.filter((transaction) => !transaction.adjustments?.some(PaddleWebhook.reverses));
  }

  /** Cancels a subscription straight away. One Paddle already ended counts as cancelled. */
  async cancel(subscriptionId: string): Promise<void> {
    const { status } = await this.call<{ status: string }>("GET", `/subscriptions/${subscriptionId}`);
    if (status === "canceled") return;

    await this.call("POST", `/subscriptions/${subscriptionId}/cancel`, { effective_from: "immediately" });
  }

  /** A link to Paddle's page where the customer cancels a subscription or changes how they pay. */
  async portal(customerId: string, subscriptionId: string): Promise<string> {
    const { urls } = await this.call<{ urls: { general: { overview: string } } }>("POST", `/customers/${customerId}/portal-sessions`, {
      subscription_ids: [subscriptionId],
    });

    return urls.general.overview;
  }

  private async call<T>(method: string, path: string, body?: unknown): Promise<T> {
    const host = this.config.sandbox ? "https://sandbox-api.paddle.com" : "https://api.paddle.com";
    const response = await fetch(`${host}${path}`, {
      method,
      headers: { Authorization: `Bearer ${this.config.apiKey}`, ...(body ? { "Content-Type": "application/json" } : {}) },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!response.ok) throw new Error(`Paddle answered ${response.status} for ${method} ${path}`);

    return ((await response.json()) as { data: T }).data;
  }
}

/**
 * Turns Paddle's webhooks into licences. A lifetime purchase adds one for good; a subscription keeps its
 * licence in step with the subscription, trial included; a refund or chargeback revokes either.
 */
export class PaddleWebhook {
  constructor(
    private readonly config: PaddleConfig,
    private readonly api: PaddleApi,
    private readonly plans: Plans,
    private readonly store: LicenceStore,
  ) {}

  async handle(request: Request, now: number): Promise<Response> {
    const body = await request.text();
    await this.verify(request.headers.get("Paddle-Signature") ?? "", body, now);

    const event = JSON.parse(body) as PaddleEvent;
    if (event.event_type === "transaction.completed") await this.purchased(event.data, now);
    else if (event.event_type.startsWith("subscription.")) await this.subscribed(event.data, event.occurred_at, now);
    else if (event.event_type === "adjustment.created" || event.event_type === "adjustment.updated") await this.adjusted(event.data);

    return new Response(null, { status: 204 });
  }

  /** Reads the account's plans straight from Paddle, for when a webhook was missed or turned away. */
  async catchUp(account: Account, now: number): Promise<void> {
    for (const customerId of await this.api.customerIds(account.email)) {
      for (const subscription of await this.api.subscriptions(customerId)) await this.subscribed(subscription, subscription.updated_at, now, account);
      for (const transaction of await this.api.purchases(customerId)) await this.purchased(transaction, now, account);
    }
  }

  private async purchased(data: PaddleEntity, now: number, account?: Account): Promise<void> {
    const { id, customer_id: customerId, items = [] } = data;
    if (!customerId || !items.some((item) => this.plans.planFor(PaddleWebhook.priceId(item)) === "lifetime")) return;

    const accountId = account?.id ?? (await this.accountFor(customerId, now));
    await this.store.addLicence(accountId, provider, id, now);
  }

  private async subscribed(data: PaddleEntity, occurredAt: string | undefined, now: number, account?: Account): Promise<void> {
    const { id, status = "", customer_id: customerId, items = [], current_billing_period: period, scheduled_change: change } = data;
    const plan = items.map((item) => this.plans.planFor(PaddleWebhook.priceId(item))).find((found) => found !== null);
    if (!customerId || !plan || plan === "lifetime") {
      if (!plan) console.warn(`Paddle subscription ${id} has no price FlyyLink sells: ${items.map(PaddleWebhook.priceId).join(", ")}`);

      return;
    }

    const known = await this.store.findLicenceByOrder(provider, id);
    const accountId = known?.account_id ?? account?.id ?? (await this.accountFor(customerId, now));
    const unpaidTrial = known?.trial === 1;
    const trial = status === "trialing" || (status === "past_due" && unpaidTrial);
    const active = Plans.covers(status, unpaidTrial);

    await this.store.saveSubscription(
      {
        accountId,
        orderId: id,
        customerId,
        plan,
        status: active ? "active" : "ended",
        trial,
        endsAt: period?.ends_at ? PaddleWebhook.seconds(period.ends_at) : (known?.ends_at ?? now),
        renews: active && change?.action !== "cancel",
        syncedAt: occurredAt ? PaddleWebhook.seconds(occurredAt) : now,
      },
      now,
    );
  }

  /** Whether an adjustment takes the payment back, which revokes what it paid for. */
  static reverses(adjustment: { action?: string; status?: string }): boolean {
    return adjustment.status === "approved" && (adjustment.action === "refund" || adjustment.action === "chargeback");
  }

  private async adjusted(data: PaddleEntity): Promise<void> {
    const { transaction_id: transactionId, subscription_id: subscriptionId } = data;
    if (!PaddleWebhook.reverses(data)) return;

    for (const order of [transactionId, subscriptionId]) {
      if (order) await this.store.setLicenceStatus(provider, order, "revoked");
    }
  }

  private async accountFor(customerId: string, now: number): Promise<string> {
    return (await this.store.accountFor(await this.api.customerEmail(customerId), now)).id;
  }

  private async verify(header: string, body: string, now: number): Promise<void> {
    const parts = Object.fromEntries(header.split(";").map((part) => part.split("=") as [string, string]));
    const timestamp = Number(parts.ts);
    if (!parts.h1 || !Number.isFinite(timestamp) || Math.abs(now - timestamp) > maxSkew) throw new HttpError(401, "bad_signature");

    const expected = await hmac(this.config.webhookSecret, `${parts.ts}:${body}`);
    if (!sameText(expected, parts.h1)) throw new HttpError(401, "bad_signature");
  }

  private static priceId(item: PaddleItem): string | undefined {
    return item.price?.id ?? item.price_id;
  }

  private static seconds(iso: string): number {
    return Math.floor(Date.parse(iso) / 1000);
  }
}
