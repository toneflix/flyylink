import { type Plan, Plans } from "./plans";

export type LicenceStatus = "active" | "ended" | "revoked";

export interface Account {
  id: string;
  email: string;
}

export interface Computer {
  machine: string;
  name: string;
  platform: string;
  secret_hash: string;
  activated_at: number;
  seen_at: number;
  removed_at: number | null;
}

export interface Licence {
  id: string;
  account_id: string;
  email: string;
  provider: string;
  order_id: string;
  status: LicenceStatus;
  plan: Plan;
  /** When the paid period ends, or null for a lifetime licence. */
  ends_at: number | null;
  /** 1 while a subscription will renew at `ends_at`. */
  renews: number;
  /** 1 while a subscription is in its Paddle trial and nothing has been paid yet. */
  trial: number;
  customer_id: string | null;
  created_at: number;
}

/** A Paddle subscription as its latest webhook describes it. */
export interface Subscription {
  accountId: string;
  orderId: string;
  customerId: string;
  plan: Plan;
  status: LicenceStatus;
  /** Paddle's trial hasn't turned into a paid period yet. */
  trial: boolean;
  endsAt: number | null;
  renews: boolean;
  /** When Paddle sent the event, so an older event never undoes a newer one. */
  syncedAt: number;
}

export interface AccountSummary {
  id: string;
  email: string;
  created_at: number;
  licensed: number;
  computers: number;
}

/** A page of rows and how many there are in all. */
export interface Found<T> {
  rows: T[];
  total: number;
}

export interface Paging {
  offset: number;
  limit: number;
}

/** Reads and writes the licence database. Holds no rules; `Licensing` does. */
export class LicenceStore {
  constructor(private readonly db: D1Database) {}

  findAccount(email: string): Promise<Account | null> {
    return this.db.prepare("SELECT id, email FROM accounts WHERE email = ?").bind(email).first<Account>();
  }

  findAccountById(id: string): Promise<Account | null> {
    return this.db.prepare("SELECT id, email FROM accounts WHERE id = ?").bind(id).first<Account>();
  }

  async accountFor(email: string, now: number): Promise<Account> {
    await this.db
      .prepare("INSERT INTO accounts (id, email, created_at) VALUES (?, ?, ?) ON CONFLICT (email) DO NOTHING")
      .bind(crypto.randomUUID(), email, now)
      .run();

    return (await this.findAccount(email))!;
  }

  async addLicence(accountId: string, provider: string, orderId: string, now: number): Promise<void> {
    await this.db
      .prepare(
        "INSERT INTO licences (id, account_id, provider, order_id, status, created_at) VALUES (?, ?, ?, ?, 'active', ?) " +
          "ON CONFLICT (provider, order_id) DO NOTHING",
      )
      .bind(crypto.randomUUID(), accountId, provider, orderId, now)
      .run();
  }

  /** Adds or updates a subscription's licence. A revoked one stays revoked. */
  async saveSubscription(subscription: Subscription, now: number): Promise<void> {
    const { accountId, orderId, customerId, plan, status, trial, endsAt, renews, syncedAt } = subscription;

    await this.db
      .prepare(
        "INSERT INTO licences (id, account_id, provider, order_id, status, plan, trial, ends_at, renews, customer_id, synced_at, created_at) " +
          "VALUES (?, ?, 'paddle', ?, ?, ?, ?, ?, ?, ?, ?, ?) " +
          "ON CONFLICT (provider, order_id) DO UPDATE SET plan = excluded.plan, trial = excluded.trial, ends_at = excluded.ends_at, renews = excluded.renews, " +
          "customer_id = excluded.customer_id, synced_at = excluded.synced_at, " +
          "status = CASE WHEN licences.status = 'revoked' THEN 'revoked' ELSE excluded.status END " +
          "WHERE licences.synced_at IS NULL OR licences.synced_at <= excluded.synced_at",
      )
      .bind(crypto.randomUUID(), accountId, orderId, status, plan, trial ? 1 : 0, endsAt, renews ? 1 : 0, customerId, syncedAt, now)
      .run();
  }

  findLicenceByOrder(provider: string, orderId: string): Promise<Licence | null> {
    return this.db
      .prepare("SELECT l.*, a.email FROM licences l JOIN accounts a ON a.id = l.account_id WHERE l.provider = ? AND l.order_id = ?")
      .bind(provider, orderId)
      .first<Licence>();
  }

  async setLicenceStatus(provider: string, orderId: string, status: LicenceStatus): Promise<void> {
    await this.db.prepare("UPDATE licences SET status = ? WHERE provider = ? AND order_id = ?").bind(status, provider, orderId).run();
  }

  /** The licence that lets the account's computers in the longest: a lifetime one first, then the latest to end. */
  currentLicence(accountId: string): Promise<Licence | null> {
    return this.db
      .prepare(
        "SELECT l.*, a.email FROM licences l JOIN accounts a ON a.id = l.account_id " +
          `WHERE l.account_id = ? AND ${Plans.current("l")} ORDER BY l.ends_at IS NULL DESC, l.ends_at DESC LIMIT 1`,
      )
      .bind(accountId)
      .first<Licence>();
  }

  async hasActiveLicence(accountId: string): Promise<boolean> {
    return (await this.currentLicence(accountId)) !== null;
  }

  async computers(accountId: string): Promise<Computer[]> {
    const { results } = await this.db
      .prepare("SELECT * FROM computers WHERE account_id = ? ORDER BY activated_at")
      .bind(accountId)
      .all<Computer>();

    return results;
  }

  async saveComputer(accountId: string, machine: string, name: string, platform: string, secretHash: string, now: number): Promise<void> {
    await this.db
      .prepare(
        "INSERT INTO computers (account_id, machine, name, platform, secret_hash, activated_at, seen_at) VALUES (?, ?, ?, ?, ?, ?, ?) " +
          "ON CONFLICT (account_id, machine) DO UPDATE SET name = excluded.name, platform = excluded.platform, " +
          "secret_hash = excluded.secret_hash, seen_at = excluded.seen_at, removed_at = NULL, " +
          "activated_at = CASE WHEN computers.removed_at IS NULL THEN computers.activated_at ELSE excluded.activated_at END",
      )
      .bind(accountId, machine, name, platform, secretHash, now, now)
      .run();
  }

  async touchComputer(accountId: string, machine: string, now: number): Promise<void> {
    await this.db.prepare("UPDATE computers SET seen_at = ? WHERE account_id = ? AND machine = ?").bind(now, accountId, machine).run();
  }

  async removeComputer(accountId: string, machine: string, now: number): Promise<void> {
    await this.db
      .prepare("UPDATE computers SET removed_at = ? WHERE account_id = ? AND machine = ? AND removed_at IS NULL")
      .bind(now, accountId, machine)
      .run();
  }

  findComputerBySecret(machine: string, secretHash: string): Promise<(Computer & { account_id: string }) | null> {
    return this.db
      .prepare("SELECT * FROM computers WHERE machine = ? AND secret_hash = ? AND removed_at IS NULL")
      .bind(machine, secretHash)
      .first();
  }

  async findAccounts(filter: { search: string; licence: string }, paging: Paging): Promise<Found<AccountSummary>> {
    const where = ["a.email LIKE ?"];
    const licensed = `EXISTS (SELECT 1 FROM licences l WHERE l.account_id = a.id AND ${Plans.current("l")})`;
    if (filter.licence === "with") where.push(licensed);
    if (filter.licence === "without") where.push(`NOT ${licensed}`);

    const clause = where.join(" AND ");
    const pattern = `%${filter.search.toLowerCase()}%`;
    const { results } = await this.db
      .prepare(
        `SELECT a.id, a.email, a.created_at, ${licensed} AS licensed, ` +
          "(SELECT COUNT(*) FROM computers c WHERE c.account_id = a.id AND c.removed_at IS NULL) AS computers " +
          `FROM accounts a WHERE ${clause} ORDER BY a.created_at DESC, a.email LIMIT ? OFFSET ?`,
      )
      .bind(pattern, paging.limit, paging.offset)
      .all<AccountSummary>();
    const count = await this.db.prepare(`SELECT COUNT(*) AS total FROM accounts a WHERE ${clause}`).bind(pattern).first<{ total: number }>();

    return { rows: results, total: count?.total ?? 0 };
  }

  async findLicences(filter: { search: string; status: string; provider: string; plan: string }, paging: Paging): Promise<Found<Licence>> {
    const where = ["a.email LIKE ?"];
    const binds: unknown[] = [`%${filter.search.toLowerCase()}%`];
    for (const [column, value] of [["l.status", filter.status], ["l.provider", filter.provider], ["l.plan", filter.plan]]) {
      if (!value) continue;

      where.push(`${column} = ?`);
      binds.push(value);
    }

    const from = `FROM licences l JOIN accounts a ON a.id = l.account_id WHERE ${where.join(" AND ")}`;
    const { results } = await this.db
      .prepare(`SELECT l.*, a.email ${from} ORDER BY l.created_at DESC, l.id LIMIT ? OFFSET ?`)
      .bind(...binds, paging.limit, paging.offset)
      .all<Licence>();
    const count = await this.db.prepare(`SELECT COUNT(*) AS total ${from}`).bind(...binds).first<{ total: number }>();

    return { rows: results, total: count?.total ?? 0 };
  }

  async licences(accountId: string): Promise<Licence[]> {
    const { results } = await this.db
      .prepare("SELECT l.*, a.email FROM licences l JOIN accounts a ON a.id = l.account_id WHERE l.account_id = ? ORDER BY l.created_at")
      .bind(accountId)
      .all<Licence>();

    return results;
  }

  findLicence(id: string): Promise<Licence | null> {
    return this.db.prepare("SELECT l.*, a.email FROM licences l JOIN accounts a ON a.id = l.account_id WHERE l.id = ?").bind(id).first<Licence>();
  }

  async setLicenceStatusById(id: string, status: LicenceStatus): Promise<void> {
    await this.db.prepare("UPDATE licences SET status = ? WHERE id = ?").bind(status, id).run();
  }

  /** Ends every website session of the account and returns how many there were. */
  async endSessions(accountId: string): Promise<number> {
    const result = await this.db.prepare("DELETE FROM sessions WHERE account_id = ?").bind(accountId).run();

    return result.meta.changes ?? 0;
  }

  async deleteAccount(account: Account): Promise<void> {
    await this.db.batch([
      this.db.prepare("DELETE FROM sessions WHERE account_id = ?").bind(account.id),
      this.db.prepare("DELETE FROM computers WHERE account_id = ?").bind(account.id),
      this.db.prepare("DELETE FROM licences WHERE account_id = ?").bind(account.id),
      this.db.prepare("DELETE FROM codes WHERE email = ?").bind(account.email),
      this.db.prepare("DELETE FROM accounts WHERE id = ?").bind(account.id),
    ]);
  }

  /** Counts for `stats`. */
  async totals(since: number): Promise<Record<string, number>> {
    const count = async (sql: string, ...binds: unknown[]) =>
      (await this.db.prepare(sql).bind(...binds).first<{ n: number }>())?.n ?? 0;

    return {
      accounts: await count("SELECT COUNT(*) AS n FROM accounts"),
      newAccounts: await count("SELECT COUNT(*) AS n FROM accounts WHERE created_at >= ?", since),
      licensed: await count(`SELECT COUNT(DISTINCT account_id) AS n FROM licences WHERE ${Plans.current()}`),
      monthly: await count(`SELECT COUNT(*) AS n FROM licences WHERE ${Plans.current()} AND provider = 'paddle' AND plan = 'monthly'`),
      annual: await count(`SELECT COUNT(*) AS n FROM licences WHERE ${Plans.current()} AND provider = 'paddle' AND plan = 'annual'`),
      lifetime: await count(`SELECT COUNT(*) AS n FROM licences WHERE ${Plans.current()} AND provider = 'paddle' AND plan = 'lifetime'`),
      free: await count(`SELECT COUNT(*) AS n FROM licences WHERE ${Plans.current()} AND provider = 'manual'`),
      ended: await count("SELECT COUNT(*) AS n FROM licences WHERE status = 'ended'"),
      revoked: await count("SELECT COUNT(*) AS n FROM licences WHERE status = 'revoked'"),
      newLicences: await count("SELECT COUNT(*) AS n FROM licences WHERE created_at >= ?", since),
      macs: await count("SELECT COUNT(*) AS n FROM computers WHERE removed_at IS NULL AND platform = 'mac'"),
      pcs: await count("SELECT COUNT(*) AS n FROM computers WHERE removed_at IS NULL AND platform = 'windows'"),
      activeComputers: await count("SELECT COUNT(*) AS n FROM computers WHERE removed_at IS NULL AND seen_at >= ?", since),
      trialing: await count(`SELECT COUNT(*) AS n FROM licences WHERE ${Plans.current()} AND trial = 1`),
    };
  }
}
