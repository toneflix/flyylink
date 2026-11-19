import { HttpError } from "../http";
import { randomToken, sha256 } from "./crypto";
import type { Settings } from "../settings";
import type { Plan } from "./plans";
import type { Account, Licence, LicenceStore } from "./store";
import type { TokenSigner } from "./signer";

const day = 24 * 60 * 60;

export interface Machine {
  id: string;
  name: string;
  platform: string;
}

export interface LicenceView {
  plan: Plan;
  /** When the paid period or the trial ends, or null for a lifetime licence. */
  endsAt: number | null;
  /** Paddle's free trial, before the first payment. */
  trial: boolean;
  renews: boolean;
  /** Whether the account page can send the person to Paddle to cancel or change how they pay. */
  manageable: boolean;
}

export interface ComputerView {
  machine: string;
  name: string;
  platform: string;
  activatedAt: number;
  seenAt: number;
}

/**
 * The rules of a licence: a monthly, annual or lifetime plan covers an account's computers up to a limit,
 * phones are free, and computers can only be swapped a few times a month. The limits come from `Settings`;
 * these are their defaults. Trials and payment retries are Paddle's, through the subscription's status.
 */
export class Licensing {
  static readonly maxComputers = 5;
  static readonly removalsPerMonth = 3;
  static readonly renewDays = 30;
  static readonly graceDays = 7;

  constructor(
    private readonly store: LicenceStore,
    private readonly signer: TokenSigner,
    private readonly settings: Settings,
  ) {}

  limit(): Promise<number> {
    return this.settings.number("max_computers");
  }

  /** Activates a computer for a signed-in account and returns its token and refresh secret. */
  async activate(account: Account, machine: Machine, now: number): Promise<{ token: string; secret: string }> {
    const licence = await this.store.currentLicence(account.id);
    if (!licence) throw new HttpError(402, "no_licence");

    const active = (await this.store.computers(account.id)).filter((c) => c.removed_at === null);
    const known = active.some((c) => c.machine === machine.id);
    const limit = await this.limit();
    if (!known && active.length >= limit) {
      throw new HttpError(409, "computer_limit", { computers: active.map(Licensing.view), limit });
    }

    const secret = randomToken();
    await this.store.saveComputer(account.id, machine.id, machine.name, machine.platform, await sha256(secret), now);

    return { token: await this.token(licence, machine.id, now, account.email), secret };
  }

  /** Issues a fresh token to a computer that is still active on a licensed account. */
  async refresh(machine: string, secret: string, now: number): Promise<string> {
    const computer = await this.store.findComputerBySecret(machine, await sha256(secret));
    if (!computer) throw new HttpError(401, "not_activated");

    const account = await this.store.findAccountById(computer.account_id);
    const licence = account ? await this.store.currentLicence(account.id) : null;
    if (!account || !licence) throw new HttpError(402, "no_licence");

    await this.store.touchComputer(account.id, machine, now);

    return this.token(licence, machine, now, account.email);
  }

  async licence(account: Account): Promise<LicenceView | null> {
    const licence = await this.store.currentLicence(account.id);
    if (!licence) return null;

    return {
      plan: licence.plan,
      endsAt: licence.ends_at,
      trial: licence.trial === 1,
      renews: licence.renews === 1,
      manageable: licence.provider === "paddle" && licence.plan !== "lifetime" && licence.customer_id !== null,
    };
  }

  async computers(account: Account): Promise<ComputerView[]> {
    return (await this.store.computers(account.id)).filter((c) => c.removed_at === null).map(Licensing.view);
  }

  async removalsLeft(account: Account, now: number): Promise<number> {
    const since = now - 30 * day;
    const recent = (await this.store.computers(account.id)).filter((c) => c.removed_at !== null && c.removed_at > since).length;

    return Math.max(0, (await this.settings.number("removals_per_month")) - recent);
  }

  async remove(account: Account, machine: string, now: number): Promise<void> {
    if ((await this.removalsLeft(account, now)) === 0) throw new HttpError(429, "removal_limit");

    await this.store.removeComputer(account.id, machine, now);
  }

  /** A subscription's token asks to renew when its Paddle period ends, or within a day once that has passed. */
  private token(licence: Licence, machine: string, now: number, email: string): Promise<string> {
    const renew = Math.min(now + Licensing.renewDays * day, Math.max(licence.ends_at ?? Infinity, now + day));

    return this.signer.sign({ v: 1, kind: "licence", machine, email, issued: now, renew, expires: renew + Licensing.graceDays * day });
  }

  private static view(c: { machine: string; name: string; platform: string; activated_at: number; seen_at: number }): ComputerView {
    return { machine: c.machine, name: c.name, platform: c.platform, activatedAt: c.activated_at, seenAt: c.seen_at };
  }
}
