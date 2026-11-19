export type Plan = "monthly" | "annual" | "lifetime";

/** Paddle subscription statuses that let computers in. */
const paying = ["active", "trialing", "past_due"];

/** The plans on sale and the Paddle price each one sells through. A plan without a price isn't on sale. */
export class Plans {
  static readonly all: Plan[] = ["monthly", "annual", "lifetime"];

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

  /**
   * Whether a subscription in Paddle's `status` lets computers in. Paddle runs trials and payment retries, so its
   * status decides, except that a trial whose first payment failed hasn't converted and doesn't count.
   */
  static covers(status: string, unpaidTrial: boolean): boolean {
    return paying.includes(status) && !(status === "past_due" && unpaidTrial);
  }

  /** SQL that is true while the licence row under `alias` lets computers in. */
  static current(alias = ""): string {
    return `${alias ? `${alias}.` : ""}status = 'active'`;
  }
}
