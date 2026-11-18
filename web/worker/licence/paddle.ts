import { HttpError } from "../http";
import { hmac, sameText } from "./crypto";
import { Plans } from "./plans";
import type { LicenceStore } from "./store";

const provider = "paddle";
const maxSkew = 5 * 60;
/** Paddle subscription statuses that still let computers in. */
const paying = ["active", "trialing", "past_due"];

interface PaddleItem {
  price?: { id?: string };
  price_id?: string;
}

interface PaddleEvent {
  event_type: string;
  occurred_at?: string;
  data: {
    id: string;
    status?: string;
    action?: string;
    customer_id?: string;
    transaction_id?: string;
    subscription_id?: string | null;
    items?: PaddleItem[];
    current_billing_period?: { ends_at?: string } | null;
    scheduled_change?: { action?: string } | null;
  };
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
 * licence in step with the subscription; a refund or chargeback revokes either.
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
    if (event.event_type === "transaction.completed") await this.purchased(event, now);
    else if (event.event_type.startsWith("subscription.")) await this.subscribed(event, now);
    else if (event.event_type === "adjustment.created" || event.event_type === "adjustment.updated") await this.adjusted(event);

    return new Response(null, { status: 204 });
  }

  private async purchased(event: PaddleEvent, now: number): Promise<void> {
    const { id, customer_id: customerId, items = [] } = event.data;
    if (!customerId || !items.some((item) => this.plans.planFor(PaddleWebhook.priceId(item)) === "lifetime")) return;

    const account = await this.store.accountFor(await this.api.customerEmail(customerId), now);
    await this.store.addLicence(account.id, provider, id, now);
  }

  private async subscribed(event: PaddleEvent, now: number): Promise<void> {
    const { id, status = "", customer_id: customerId, items = [], current_billing_period: period, scheduled_change: change } = event.data;
    const plan = items.map((item) => this.plans.planFor(PaddleWebhook.priceId(item))).find((found) => found !== null);
    if (!customerId || !plan || plan === "lifetime") return;

    const known = await this.store.findLicenceByOrder(provider, id);
    const accountId = known?.account_id ?? (await this.store.accountFor(await this.api.customerEmail(customerId), now)).id;
    const active = paying.includes(status);

    await this.store.saveSubscription(
      {
        accountId,
        orderId: id,
        customerId,
        plan,
        status: active ? "active" : "ended",
        endsAt: period?.ends_at ? PaddleWebhook.seconds(period.ends_at) : (known?.ends_at ?? now),
        renews: active && change?.action !== "cancel",
        syncedAt: event.occurred_at ? PaddleWebhook.seconds(event.occurred_at) : now,
      },
      now,
    );
  }

  private async adjusted(event: PaddleEvent): Promise<void> {
    const { action, status, transaction_id: transactionId, subscription_id: subscriptionId } = event.data;
    if (status !== "approved" || (action !== "refund" && action !== "chargeback")) return;

    for (const order of [transactionId, subscriptionId]) {
      if (order) await this.store.setLicenceStatus(provider, order, "revoked");
    }
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
