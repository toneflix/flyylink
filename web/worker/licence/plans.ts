export type Plan = "monthly" | "annual" | "lifetime";

const day = 24 * 60 * 60;

/** The plans on sale and the Paddle price each one sells through. A plan without a price isn't on sale. */
export class Plans {
  static readonly all: Plan[] = ["monthly", "annual", "lifetime"];
  /** Days a subscription keeps working after its paid period ends, while Paddle retries the payment. */
  static readonly lapseDays = 3;

  constructor(private readonly prices: Partial<Record<Plan, string>>) {}

  /** The price of each plan on sale. */
  onSale(): Partial<Record<Plan, string>> {
    return Object.fromEntries(Plans.all.filter((plan) => this.prices[plan]).map((plan) => [plan, this.prices[plan]]));
  }

  planFor(priceId: string | undefined): Plan | null {
    return Plans.all.find((plan) => priceId && this.prices[plan] === priceId) ?? null;
  }

  static isPlan(value: string): value is Plan {
    return (Plans.all as string[]).includes(value);
  }

  /** SQL that is true while the licence row under `alias` lets computers in. */
  static current(alias = ""): string {
    const column = (name: string) => (alias ? `${alias}.${name}` : name);

    return `(${column("status")} = 'active' AND (${column("ends_at")} IS NULL OR ${column("ends_at")} + ${Plans.lapseDays * day} > unixepoch()))`;
  }

  /** When a licence that ends at `endsAt` stops working, or null for one that never ends. */
  static lapsesAt(endsAt: number | null): number | null {
    return endsAt === null ? null : endsAt + Plans.lapseDays * day;
  }
}
