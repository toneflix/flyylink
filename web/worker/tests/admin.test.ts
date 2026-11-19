import { beforeEach, describe, expect, it, vi } from "vitest";
import worker, { type Env } from "../index";
import { LogMailer } from "../licence/mailer";
import { AdminCommands } from "../admin/protocol/commands";
import type { AdminResult } from "../admin/protocol/result";
import { AdminHeaders, AdminSignature } from "../admin/protocol/signature";
import { testDatabase } from "./d1";

const site = "https://flyylink.test";

let env: Env;
let mails: { to: string; subject: string }[];

async function signingKey(): Promise<string> {
  const pair = (await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"])) as CryptoKeyPair;

  return Buffer.from((await crypto.subtle.exportKey("pkcs8", pair.privateKey)) as ArrayBuffer).toString("base64");
}

function call(path: string, body: unknown): Promise<Response> {
  return worker.fetch(new Request(`${site}${path}`, { method: "POST", body: JSON.stringify(body), headers: { "content-type": "application/json" } }), env);
}

/** An operator's CLI: signs each command with its key. */
class Operator {
  private constructor(readonly keys: { publicKey: string; privateKey: string }) {}

  static async create(): Promise<Operator> {
    return new Operator(await AdminSignature.generateKeys());
  }

  async request(command: string, args: Record<string, string> = {}, options: Record<string, string | boolean> = {}, nonce = crypto.randomUUID().replace(/-/g, "")): Promise<Response> {
    const body = JSON.stringify({ command, arguments: args, options });
    const timestamp = String(Math.floor(Date.now() / 1000));
    const signature = await AdminSignature.sign(this.keys.privateKey, { method: "POST", path: "/api/admin/run", timestamp, nonce, body });

    return worker.fetch(
      new Request(`${site}/api/admin/run`, {
        method: "POST",
        body,
        headers: { [AdminHeaders.key]: this.keys.publicKey, [AdminHeaders.timestamp]: timestamp, [AdminHeaders.nonce]: nonce, [AdminHeaders.signature]: signature },
      }),
      env,
    );
  }

  async run(command: string, args: Record<string, string> = {}, options: Record<string, string | boolean> = {}): Promise<AdminResult> {
    const response = await this.request(command, args, options);

    expect(response.status).toBe(200);

    return (await response.json()) as AdminResult;
  }
}

async function firstOperator(): Promise<Operator> {
  const operator = await Operator.create();

  env.ADMIN_BOOTSTRAP_KEY = operator.keys.publicKey;
  expect((await operator.run("operators:add", { name: "obi", key: operator.keys.publicKey })).exit).toBe(0);

  return operator;
}

beforeEach(async () => {
  mails = [];
  vi.spyOn(LogMailer.prototype, "send").mockImplementation(async (to, email) => void mails.push({ to, subject: email.subject }));
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
    SENDPULSE_API_KEY: "",
  };
});

describe("signed requests", () => {
  it("lets the bootstrap key in only until the first operator exists", async () => {
    const obi = await firstOperator();
    const stranger = await Operator.create();

    env.ADMIN_BOOTSTRAP_KEY = stranger.keys.publicKey;

    expect((await stranger.request("stats")).status).toBe(401);
    expect((await obi.run("stats")).exit).toBe(0);
  });

  it("refuses unknown keys, replays and tampered bodies", async () => {
    const obi = await firstOperator();
    const nonce = "replayed-nonce-1234";

    expect((await (await Operator.create()).request("stats")).status).toBe(401);
    expect((await obi.request("stats", {}, {}, nonce)).status).toBe(200);
    expect((await obi.request("stats", {}, {}, nonce)).status).toBe(401);

    const signed = await obi.request("stats");
    const forged = await worker.fetch(new Request(`${site}/api/admin/run`, { method: "POST", body: JSON.stringify({ command: "users:delete", arguments: { email: "a@b.co" }, options: { force: true } }), headers: signed.headers }), env);

    expect(forged.status).toBe(401);
  });

  it("stops a revoked operator", async () => {
    const obi = await firstOperator();
    const ada = await Operator.create();

    await obi.run("operators:add", { name: "ada", key: ada.keys.publicKey });
    expect((await ada.run("stats")).exit).toBe(0);
    await obi.run("operators:revoke", { name: "ada" });

    expect((await ada.request("stats")).status).toBe(401);
  });

  it("has a handler for every command", async () => {
    const obi = await firstOperator();

    for (const command of AdminCommands) expect((await obi.request(command.name)).status, command.name).toBe(200);
  });

  it("refuses missing arguments and records commands in the audit log", async () => {
    const obi = await firstOperator();
    const refused = await obi.run("users:show");

    expect(refused.exit).toBe(1);
    expect(refused.notes[0].text).toContain("Missing <email>");

    const audit = await obi.run("audit:list");

    expect(audit.listing!.rows.map((row) => `${row.actor} ${row.command} ${row.result}`)).toEqual(["obi users:show failed", "bootstrap operators:add ok"]);
  });
});

describe("users and licences", () => {
  it("grants, lists, revokes and restores a free licence", async () => {
    const obi = await firstOperator();

    expect((await obi.run("licences:grant", { email: "Ada@Example.com" })).exit).toBe(0);

    const users = await obi.run("users:list", {}, { licence: "with" });

    expect(users.listing!.rows).toMatchObject([{ email: "ada@example.com", licensed: "yes" }]);

    const id = String((await obi.run("licences:list")).listing!.rows[0].id);

    await obi.run("licences:revoke", { id });
    expect((await obi.run("users:list", {}, { licence: "without" })).listing!.total).toBe(1);

    await obi.run("licences:restore", { id });
    expect((await obi.run("users:show", { email: "ada@example.com" })).fields).toContainEqual(["Licensed", "yes"]);
  });

  it("deletes an account only with --force", async () => {
    const obi = await firstOperator();

    await obi.run("licences:grant", { email: "ada@example.com" });

    expect((await obi.run("users:delete", { email: "ada@example.com" })).exit).toBe(1);
    expect((await obi.run("users:delete", { email: "ada@example.com" }, { force: true })).exit).toBe(0);
    expect((await obi.run("users:list")).listing!.total).toBe(0);
  });
});

describe("settings", () => {
  it("changes the computer limit and reviewer emails the licence API uses", async () => {
    const obi = await firstOperator();
    const activate = (machine: string) => call("/api/activate", { email: "review@example.com", code: "fixed-code-1", machine: { id: machine, name: machine, platform: "mac" } });

    env.REVIEW_CODE = "fixed-code-1";
    await obi.run("licences:grant", { email: "review@example.com" });

    expect((await activate("machine-1")).status).toBe(401);

    await obi.run("settings:set", { key: "review_emails", value: "Review@Example.com" });
    await obi.run("settings:set", { key: "max_computers", value: "1" });

    expect((await activate("machine-1")).status).toBe(200);
    expect((await activate("machine-2")).status).toBe(409);

    await obi.run("settings:unset", { key: "max_computers" });

    expect((await activate("machine-2")).status).toBe(200);
  });

  it("refuses values out of range", async () => {
    const obi = await firstOperator();

    expect((await obi.run("settings:set", { key: "max_computers", value: "0" })).exit).toBe(1);
    expect((await obi.run("settings:set", { key: "colour", value: "1" })).exit).toBe(1);
  });
});

describe("notices", () => {
  it("emails in batches the CLI keeps asking for", async () => {
    const obi = await firstOperator();

    for (let i = 0; i < 25; i++) await obi.run("licences:grant", { email: `user${String(i).padStart(2, "0")}@example.com` });

    const first = await obi.run("notify:send", { to: "licensed" }, { subject: "Hello", message: "One\\nTwo", force: true });

    expect(first.next).toEqual({ command: "notify:continue", arguments: { id: "1" } });
    expect(mails).toHaveLength(20);

    const rest = await obi.run(first.next!.command, first.next!.arguments);

    expect(rest.next).toBeUndefined();
    expect(new Set(mails.map((mail) => mail.to)).size).toBe(25);
  });

  it("counts without sending on a dry run, and needs --force to send", async () => {
    const obi = await firstOperator();

    await obi.run("licences:grant", { email: "ada@example.com" });

    expect((await obi.run("notify:send", { to: "all" }, { subject: "Hi", message: "Hi", "dry-run": true })).notes[0].text).toContain("1 account");
    expect((await obi.run("notify:send", { to: "all" }, { subject: "Hi", message: "Hi" })).exit).toBe(1);
    expect(mails).toHaveLength(0);
  });

  it("gives app notices to the computers they are for, and never by email", async () => {
    const obi = await firstOperator();
    const notices = async (body: object) => (await (await call("/api/notices", body)).json()) as { notices: { title: string }[]; latest: number };

    await obi.run("settings:set", { key: "review_emails", value: "ada@example.com" });
    env.REVIEW_CODE = "fixed-code-1";
    await obi.run("licences:grant", { email: "ada@example.com" });

    const { secret } = (await (await call("/api/activate", { email: "ada@example.com", code: "fixed-code-1", machine: { id: "mac-1", name: "Mac", platform: "mac" } })).json()) as { secret: string };

    expect(await notices({ machine: "mac-1", secret })).toEqual({ notices: [], latest: 0 });

    await obi.run("notify:send", { to: "licensed" }, { channel: "app", subject: "For buyers", message: "Thanks", force: true });
    await obi.run("notify:send", { to: "unlicensed" }, { channel: "app", subject: "For trials", message: "Buy", force: true });
    await obi.run("notify:send", { to: "ada@example.com" }, { channel: "both", subject: "For Ada", message: "Hi", force: true });

    expect(mails.map((mail) => mail.subject)).toEqual(["For Ada"]);
    expect((await notices({ machine: "mac-1", secret, after: 0 })).notices.map((notice) => notice.title)).toEqual(["For buyers", "For Ada"]);
    expect((await notices({ machine: "pc-2", after: 0 })).notices.map((notice) => notice.title)).toEqual(["For trials"]);
    expect(await notices({ machine: "pc-2", after: 3 })).toEqual({ notices: [], latest: 3 });
  });
});
