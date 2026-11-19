import { beforeEach, describe, expect, it, vi } from "vitest";
import worker, { type Env } from "../index";
import { hmac } from "../licence/crypto";
import { LicenceStore } from "../licence/store";
import type { TokenClaims } from "../licence/signer";
import { testDatabase } from "./d1";

const site = "https://flyylink.test";
const day = 24 * 60 * 60;
const email = "ada@example.com";
const code = "review-code-1234";

let env: Env;
let portals: unknown[];
/** What Paddle's API lists for ctm_1 when asked, as if its webhooks never arrived. */
let listed: { subscriptions: unknown[]; transactions: unknown[] };

async function signingKey(): Promise<string> {
  const pair = (await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"])) as CryptoKeyPair;

  return Buffer.from((await crypto.subtle.exportKey("pkcs8", pair.privateKey)) as ArrayBuffer).toString("base64");
}

function call(path: string, body?: unknown, headers: Record<string, string> = {}): Promise<Response> {
  return worker.fetch(
    new Request(`${site}${path}`, { method: body === undefined ? "GET" : "POST", body: body === undefined ? undefined : JSON.stringify(body), headers: { "content-type": "application/json", ...headers } }),
    env,
  );
}

/** Sends a webhook the way Paddle signs it. */
async function paddle(eventType: string, data: Record<string, unknown>, occurredAt = new Date()): Promise<Response> {
  const body = JSON.stringify({ event_type: eventType, occurred_at: occurredAt.toISOString(), data });
  const ts = String(Math.floor(Date.now() / 1000));

  return worker.fetch(
    new Request(`${site}/api/paddle/webhook`, { method: "POST", body, headers: { "Paddle-Signature": `ts=${ts};h1=${await hmac("test", `${ts}:${body}`)}` } }),
    env,
  );
}

function subscription(status: string, price: string, endsAt: Date | null, extra: Record<string, unknown> = {}) {
  return {
    id: "sub_1",
    status,
    customer_id: "ctm_1",
    items: [{ price: { id: price } }],
    current_billing_period: endsAt ? { starts_at: new Date().toISOString(), ends_at: endsAt.toISOString() } : null,
    ...extra,
  };
}

async function activate(): Promise<Response> {
  return call("/api/activate", { email, code, machine: { id: "mac-1", name: "Ada's Mac", platform: "mac" } });
}

function claims(token: string): TokenClaims {
  return JSON.parse(Buffer.from(token.split(".")[0], "base64url").toString()) as TokenClaims;
}

async function signedIn(): Promise<Record<string, string>> {
  const response = await call("/api/session", { email, code });

  return { cookie: response.headers.get("set-cookie")!.split(";")[0] };
}

beforeEach(async () => {
  portals = [];
  listed = { subscriptions: [], transactions: [] };
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/customers/ctm_1")) return Response.json({ data: { email: "Ada@Example.com" } });
    if (url.endsWith(`/customers?email=${encodeURIComponent(email)}`)) return Response.json({ data: [{ id: "ctm_1" }] });
    if (url.endsWith("/subscriptions?customer_id=ctm_1&status=active,trialing,past_due")) return Response.json({ data: listed.subscriptions });
    if (url.endsWith("/transactions?customer_id=ctm_1&status=completed")) return Response.json({ data: listed.transactions });
    if (url.endsWith("/customers/ctm_1/portal-sessions")) {
      portals.push(JSON.parse(String(init?.body)));

      return Response.json({ data: { urls: { general: { overview: "https://customer-portal.paddle.com/ctm_1" } } } });
    }

    return new Response(null, { status: 404 });
  });
  env = {
    ASSETS: { fetch: async () => new Response("asset") } as unknown as Fetcher,
    RELEASES: {} as R2Bucket,
    LICENCES: testDatabase(),
    MAIL_FROM: "FlyyLink <hello@flyylink.test>",
    SITE_URL: site,
    MAIL_MODE: "log",
    LICENCE_SIGNING_KEY: await signingKey(),
    CODE_SECRET: "test",
    PADDLE_WEBHOOK_SECRET: "test",
    PADDLE_API_KEY: "",
    PADDLE_CLIENT_TOKEN: "test_token",
    PADDLE_PRICE_MONTHLY: "pri_month",
    PADDLE_PRICE_ANNUAL: "pri_year",
    PADDLE_PRICE_LIFETIME: "pri_life",
    SENDPULSE_API_KEY: "",
    REVIEW_EMAILS: email,
    REVIEW_CODE: code,
  };
});

describe("checkout", () => {
  it("offers the plans that have a price", async () => {
    delete env.PADDLE_PRICE_ANNUAL;

    expect(await (await call("/api/checkout")).json()).toEqual({ clientToken: "test_token", sandbox: false, prices: { monthly: "pri_month", lifetime: "pri_life" }, limit: 5 });
  });

  it("shows how many computers a licence covers right now", async () => {
    await env.LICENCES.prepare("INSERT INTO settings (key, value, updated_at) VALUES ('max_computers', '3', 0)").run();

    expect(((await (await call("/api/checkout")).json()) as { limit: unknown }).limit).toBe(3);
  });

  it("treats the old PADDLE_PRICE_ID as the lifetime price", async () => {
    delete env.PADDLE_PRICE_MONTHLY;
    delete env.PADDLE_PRICE_ANNUAL;
    delete env.PADDLE_PRICE_LIFETIME;
    env.PADDLE_PRICE_ID = "pri_old";

    expect(((await (await call("/api/checkout")).json()) as { prices: unknown }).prices).toEqual({ lifetime: "pri_old" });
  });
});

describe("subscriptions", () => {
  it("licenses computers until the paid period ends", async () => {
    const ends = new Date(Date.now() + 10 * day * 1000);

    expect((await paddle("subscription.created", subscription("active", "pri_month", ends))).status).toBe(204);

    const { token } = (await (await activate()).json()) as { token: string };

    expect(claims(token).renew).toBe(Math.floor(ends.getTime() / 1000));

    const account = await (await call("/api/account", undefined, await signedIn())).json();

    expect(account).toMatchObject({ licensed: true, licence: { plan: "monthly", endsAt: Math.floor(ends.getTime() / 1000), trial: false, renews: true, manageable: true } });
  });

  it("locks computers once the subscription is cancelled", async () => {
    const ends = new Date(Date.now() + 10 * day * 1000);

    await paddle("subscription.created", subscription("active", "pri_year", ends));
    const { secret } = (await (await activate()).json()) as { secret: string };

    await paddle("subscription.updated", subscription("active", "pri_year", ends, { scheduled_change: { action: "cancel" } }));
    expect(await (await call("/api/account", undefined, await signedIn())).json()).toMatchObject({ licence: { plan: "annual", renews: false } });

    await paddle("subscription.canceled", subscription("canceled", "pri_year", null));

    expect((await call("/api/refresh", { machine: "mac-1", secret })).status).toBe(402);
  });

  it("ignores an event older than the one already applied", async () => {
    const ends = new Date(Date.now() + 10 * day * 1000);

    await paddle("subscription.canceled", subscription("canceled", "pri_month", null));
    await paddle("subscription.updated", subscription("active", "pri_month", ends), new Date(Date.now() - 60_000));

    expect((await activate()).status).toBe(402);
  });

  it("keeps computers in while Paddle retries a renewal, until it cancels", async () => {
    const ends = new Date(Date.now() + 30 * day * 1000);

    await paddle("subscription.activated", subscription("active", "pri_month", ends), new Date(Date.now() - 2000));
    await paddle("subscription.past_due", subscription("past_due", "pri_month", ends), new Date(Date.now() - 1000));
    const { secret } = (await (await activate()).json()) as { secret: string };

    await paddle("subscription.canceled", subscription("canceled", "pri_month", null));

    expect((await call("/api/refresh", { machine: "mac-1", secret })).status).toBe(402);
  });

  it("follows Paddle's status after the period it last sent", async () => {
    await paddle("subscription.updated", subscription("active", "pri_month", new Date(Date.now() - 4 * day * 1000)));

    const { token } = (await (await activate()).json()) as { token: string };
    const { renew, issued } = claims(token);

    expect(renew - issued).toBe(day);
  });

  it("revokes a refunded subscription", async () => {
    await paddle("subscription.created", subscription("active", "pri_month", new Date(Date.now() + 10 * day * 1000)));
    await paddle("adjustment.created", { id: "adj_1", action: "refund", status: "approved", transaction_id: "txn_9", subscription_id: "sub_1" });

    expect((await activate()).status).toBe(402);
  });

  it("opens Paddle's page to cancel or change how to pay", async () => {
    await paddle("subscription.created", subscription("active", "pri_month", new Date(Date.now() + 10 * day * 1000)));

    const response = await call("/api/account/billing", {}, await signedIn());

    expect(await response.json()).toEqual({ url: "https://customer-portal.paddle.com/ctm_1" });
    expect(portals).toEqual([{ subscription_ids: ["sub_1"] }]);
  });
});

describe("trials", () => {
  it("licenses computers during Paddle's trial", async () => {
    const ends = new Date(Date.now() + 7 * day * 1000);

    await paddle("subscription.trialing", subscription("trialing", "pri_month", ends));

    const { token } = (await (await activate()).json()) as { token: string };

    expect(claims(token).renew).toBe(Math.floor(ends.getTime() / 1000));
    expect(await (await call("/api/account", undefined, await signedIn())).json()).toMatchObject({ licensed: true, licence: { plan: "monthly", trial: true } });
  });

  it("counts a trial as paid once Paddle activates it", async () => {
    await paddle("subscription.trialing", subscription("trialing", "pri_year", new Date(Date.now() + day * 1000)), new Date(Date.now() - 2000));
    await paddle("subscription.activated", subscription("active", "pri_year", new Date(Date.now() + 365 * day * 1000)), new Date(Date.now() - 1000));
    await paddle("subscription.past_due", subscription("past_due", "pri_year", new Date(Date.now() + 365 * day * 1000)));

    expect(await (await call("/api/account", undefined, await signedIn())).json()).toMatchObject({ licensed: true, licence: { plan: "annual", trial: false } });
  });

  it("locks computers when the first payment after a trial fails", async () => {
    await paddle("subscription.trialing", subscription("trialing", "pri_month", new Date(Date.now() + day * 1000)), new Date(Date.now() - 1000));
    const { secret } = (await (await activate()).json()) as { secret: string };

    await paddle("subscription.past_due", subscription("past_due", "pri_month", new Date(Date.now() + 30 * day * 1000)));

    expect((await call("/api/refresh", { machine: "mac-1", secret })).status).toBe(402);
  });

  it("locks computers when a trial is cancelled", async () => {
    await paddle("subscription.trialing", subscription("trialing", "pri_month", new Date(Date.now() + day * 1000)), new Date(Date.now() - 1000));
    const { secret } = (await (await activate()).json()) as { secret: string };

    await paddle("subscription.canceled", subscription("canceled", "pri_month", null));

    expect((await call("/api/refresh", { machine: "mac-1", secret })).status).toBe(402);
  });

  it("tells older apps that ask for a trial of their own that there isn't one", async () => {
    const response = await call("/api/trial", { machine: "mac-1" });

    expect(response.status).toBe(402);
    expect(await response.json()).toMatchObject({ error: "trial_over" });
  });
});

describe("lifetime", () => {
  it("licenses computers for good, with the usual renewal", async () => {
    await paddle("transaction.completed", { id: "txn_1", customer_id: "ctm_1", items: [{ price: { id: "pri_life" } }] });

    const { token } = (await (await activate()).json()) as { token: string };
    const { renew, issued } = claims(token);

    expect(renew - issued).toBe(30 * day);
    expect((await call("/api/account/billing", {}, await signedIn())).status).toBe(404);
  });

  it("ignores the transactions that pay for a subscription", async () => {
    await paddle("transaction.completed", { id: "txn_2", customer_id: "ctm_1", subscription_id: "sub_1", items: [{ price: { id: "pri_month" } }] });

    expect((await activate()).status).toBe(402);
  });
});

describe("missed webhooks", () => {
  it("finds a trial in Paddle when the webhook never arrived", async () => {
    listed.subscriptions = [subscription("trialing", "pri_month", new Date(Date.now() + 7 * day * 1000))];

    expect((await activate()).status).toBe(200);
    expect(await (await call("/api/account", undefined, await signedIn())).json()).toMatchObject({ licensed: true, licence: { plan: "monthly", trial: true } });
  });

  it("finds a lifetime purchase in Paddle when the webhook never arrived", async () => {
    listed.transactions = [{ id: "txn_1", customer_id: "ctm_1", items: [{ price: { id: "pri_life" } }] }];

    expect((await activate()).status).toBe(200);
  });

  it("still says there's no plan when Paddle has none", async () => {
    expect((await activate()).status).toBe(402);
  });
});

describe("deleted accounts", () => {
  it("don't get their Paddle orders back from a late webhook or a sign-in", async () => {
    await paddle("subscription.activated", subscription("active", "pri_month", new Date(Date.now() + 30 * day * 1000)), new Date(Date.now() - 1000));
    await paddle("transaction.completed", { id: "txn_1", customer_id: "ctm_1", items: [{ price: { id: "pri_life" } }] });
    const store = new LicenceStore(env.LICENCES);
    await store.deleteAccount((await store.findAccount(email))!, Math.floor(Date.now() / 1000));

    await paddle("subscription.updated", subscription("active", "pri_month", new Date(Date.now() + 30 * day * 1000)));
    listed.subscriptions = [subscription("active", "pri_month", new Date(Date.now() + 30 * day * 1000))];
    listed.transactions = [{ id: "txn_1", customer_id: "ctm_1", items: [{ price: { id: "pri_life" } }] }];

    expect((await activate()).status).toBe(402);
  });

  it("can buy again with a new order", async () => {
    await paddle("transaction.completed", { id: "txn_1", customer_id: "ctm_1", items: [{ price: { id: "pri_life" } }] });
    const store = new LicenceStore(env.LICENCES);
    await store.deleteAccount((await store.findAccount(email))!, Math.floor(Date.now() / 1000));

    await paddle("transaction.completed", { id: "txn_2", customer_id: "ctm_1", items: [{ price: { id: "pri_life" } }] });

    expect((await activate()).status).toBe(200);
  });
});
