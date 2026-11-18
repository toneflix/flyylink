import { readJson, requireString } from "./http";
import { sha256 } from "./licence/crypto";
import type { Emails } from "./licence/emails";
import type { Mailer } from "./licence/mailer";
import { Plans } from "./licence/plans";
import type { Found, LicenceStore, Paging } from "./licence/store";
import type { Router } from "./router";

export type Audience = "all" | "licensed" | "unlicensed" | "emails";

/** Where a notice goes: by email, to the Mac and Windows apps, or both. Phones never get them. */
export type Channel = "email" | "app" | "both";

export const channels: readonly Channel[] = ["email", "app", "both"];

export interface Notice {
  id: number;
  channel: Channel;
  audience: Audience;
  emails: string | null;
  subject: string;
  message: string;
  link: string | null;
  link_text: string | null;
  status: "sending" | "sent";
  cursor: string;
  sent: number;
  failed: number;
  total: number;
  created_by: string;
  created_at: number;
}

export interface Draft {
  to: string;
  channel: Channel;
  subject: string;
  message: string;
  link?: { url: string; text: string };
}

/** A notice as the computer apps show it. */
export interface AppNotice {
  id: number;
  title: string;
  message: string;
  link: string | null;
  linkText: string | null;
}

const day = 24 * 60 * 60;
const licensed = `EXISTS (SELECT 1 FROM licences l WHERE l.account_id = a.id AND ${Plans.current("l")})`;
const conditions: Record<Exclude<Audience, "emails">, string> = { all: "1", licensed, unlicensed: `NOT ${licensed}` };

/**
 * Notices operators send to users. Email goes out in batches small enough for one worker request, each
 * moving the notice's cursor on, so a send that stops part way carries on from where it stopped. The
 * Mac and Windows apps fetch app notices from `/api/notices` when they check their licence.
 */
export class Notices {
  static readonly batchSize = 20;
  /** How long an app notice stays available to computers that haven't checked in yet. */
  static readonly appDays = 30;
  static readonly perFetch = 10;

  constructor(
    private readonly db: D1Database,
    private readonly store: LicenceStore,
    private readonly mailer: Mailer,
    private readonly emails: Emails,
  ) {}

  register(router: Router): void {
    router.post("/api/notices", (request) => this.fetchForComputer(request, Math.floor(Date.now() / 1000)));
  }

  /** Reads `all`, `licensed`, `unlicensed` or a comma-separated list of emails. Returns null when it is none of them. */
  static audience(to: string): { audience: Audience; emails: string[] } | null {
    const word = to.trim().toLowerCase();
    if (word in conditions) return { audience: word as Audience, emails: [] };

    const emails = [...new Set(word.split(",").map((email) => email.trim()).filter(Boolean))].sort();
    if (!emails.length || emails.some((email) => !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))) return null;

    return { audience: "emails", emails };
  }

  /** Splits a message into paragraphs at line breaks or a typed `\n`. */
  static paragraphs(message: string): string[] {
    return message
      .replace(/\\n/g, "\n")
      .split(/\n+/)
      .map((paragraph) => paragraph.trim())
      .filter(Boolean);
  }

  /** How many accounts the audience covers. */
  async count(to: { audience: Audience; emails: string[] }): Promise<number> {
    if (to.audience === "emails") return to.emails.length;

    const row = await this.db.prepare(`SELECT COUNT(*) AS n FROM accounts a WHERE ${conditions[to.audience]}`).first<{ n: number }>();

    return row?.n ?? 0;
  }

  async create(draft: Draft, actor: string, now: number): Promise<Notice> {
    const to = Notices.audience(draft.to)!;
    const emailing = draft.channel !== "app";
    const row = await this.db
      .prepare(
        "INSERT INTO notifications (channel, audience, emails, subject, message, link, link_text, status, total, created_by, created_at, finished_at) " +
          "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) RETURNING *",
      )
      .bind(
        draft.channel,
        to.audience,
        to.audience === "emails" ? JSON.stringify(to.emails) : null,
        draft.subject,
        draft.message,
        draft.link?.url ?? null,
        draft.link?.text ?? null,
        emailing ? "sending" : "sent",
        await this.count(to),
        actor,
        now,
        emailing ? null : now,
      )
      .first<Notice>();

    return row!;
  }

  find(id: number): Promise<Notice | null> {
    return this.db.prepare("SELECT * FROM notifications WHERE id = ?").bind(id).first<Notice>();
  }

  async list(paging: Paging): Promise<Found<Notice>> {
    const { results } = await this.db.prepare("SELECT * FROM notifications ORDER BY id DESC LIMIT ? OFFSET ?").bind(paging.limit, paging.offset).all<Notice>();
    const count = await this.db.prepare("SELECT COUNT(*) AS n FROM notifications").first<{ n: number }>();

    return { rows: results, total: count?.n ?? 0 };
  }

  /**
   * Emails the next batch and returns the notice as it now stands. The batch is claimed by moving the
   * cursor first, so two runs at once never email the same people.
   */
  async sendBatch(notice: Notice, now: number): Promise<Notice> {
    if (notice.status === "sent") return notice;

    const recipients = await this.recipients(notice);
    const done = recipients.length < Notices.batchSize;
    const cursor = recipients.at(-1) ?? notice.cursor;
    const claim = await this.db
      .prepare("UPDATE notifications SET cursor = ?, status = ?, finished_at = ? WHERE id = ? AND cursor = ? AND status = 'sending'")
      .bind(cursor, done ? "sent" : "sending", done ? now : null, notice.id, notice.cursor)
      .run();
    if (!claim.meta.changes) return (await this.find(notice.id))!;

    const email = this.emails.notice(notice.subject, Notices.paragraphs(notice.message), notice.link ? { url: notice.link, text: notice.link_text ?? "Open" } : undefined);
    let failed = 0;

    for (const to of recipients) {
      try {
        await this.mailer.send(to, email);
      } catch (error) {
        failed++;
        console.error(`Notice ${notice.id} to ${to} failed`, error);
      }
    }

    await this.db
      .prepare("UPDATE notifications SET sent = sent + ?, failed = failed + ? WHERE id = ?")
      .bind(recipients.length - failed, failed, notice.id)
      .run();

    return (await this.find(notice.id))!;
  }

  /**
   * The app notices for a computer newer than `after`. A computer that has never asked gets none, only
   * the newest id, so a new install doesn't show old notices.
   */
  async forComputer(machine: string, secret: string | null, after: number | null, now: number): Promise<{ notices: AppNotice[]; latest: number }> {
    const newest = await this.db.prepare("SELECT COALESCE(MAX(id), 0) AS id FROM notifications WHERE channel != 'email'").first<{ id: number }>();
    if (after === null) return { notices: [], latest: newest?.id ?? 0 };

    const computer = secret ? await this.store.findComputerBySecret(machine, await sha256(secret)) : null;
    const account = computer ? await this.store.findAccountById(computer.account_id) : null;
    const hasLicence = account ? await this.store.hasActiveLicence(account.id) : false;
    const { results } = await this.db
      .prepare("SELECT * FROM notifications WHERE channel != 'email' AND id > ? AND created_at > ? ORDER BY id")
      .bind(after, now - Notices.appDays * day)
      .all<Notice>();
    const reaches = (notice: Notice) =>
      notice.audience === "all" ||
      (notice.audience === "licensed" && hasLicence) ||
      (notice.audience === "unlicensed" && !hasLicence) ||
      (notice.audience === "emails" && account !== null && (JSON.parse(notice.emails ?? "[]") as string[]).includes(account.email));
    const notices = results.filter(reaches).slice(-Notices.perFetch);

    return {
      notices: notices.map((notice) => ({ id: notice.id, title: notice.subject, message: Notices.paragraphs(notice.message).join("\n\n"), link: notice.link, linkText: notice.link_text })),
      latest: Math.max(after, results.at(-1)?.id ?? 0),
    };
  }

  private async fetchForComputer(request: Request, now: number): Promise<Response> {
    const body = await readJson<{ machine?: unknown; secret?: unknown; after?: unknown }>(request);
    const machine = requireString(body.machine, "machine", 128);
    const secret = typeof body.secret === "string" && body.secret ? body.secret : null;
    const after = typeof body.after === "number" && Number.isInteger(body.after) && body.after >= 0 ? body.after : null;

    return Response.json(await this.forComputer(machine, secret, after, now));
  }

  private async recipients(notice: Notice): Promise<string[]> {
    if (notice.audience === "emails") {
      return (JSON.parse(notice.emails ?? "[]") as string[]).filter((email) => email > notice.cursor).slice(0, Notices.batchSize);
    }

    const { results } = await this.db
      .prepare(`SELECT a.email FROM accounts a WHERE ${conditions[notice.audience]} AND a.email > ? ORDER BY a.email LIMIT ?`)
      .bind(notice.cursor, Notices.batchSize)
      .all<{ email: string }>();

    return results.map((row) => row.email);
  }
}
