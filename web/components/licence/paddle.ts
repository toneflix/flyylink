import type { CheckoutConfig, Plan } from "./api";

interface PaddleEvent {
  name: string;
  data?: { customer?: { email?: string } };
}

interface LineItem {
  price: { id: string };
  totals: { total: string };
  formattedTotals: { total: string };
}

interface PaddleGlobal {
  Environment: { set(env: string): void };
  Initialize(options: { token: string; eventCallback(event: PaddleEvent): void }): void;
  Checkout: { open(options: { items: { priceId: string; quantity: number }[] }): void };
  PricePreview(request: { items: { priceId: string; quantity: number }[] }): Promise<{
    data: { currencyCode: string; details: { lineItems: LineItem[] } };
  }>;
}

declare global {
  interface Window {
    Paddle?: PaddleGlobal;
  }
}

/** A plan's price in the visitor's currency. `amount` is in the currency's smallest unit. */
export interface PlanPrice {
  label: string;
  amount: number;
}

let loading: Promise<PaddleGlobal> | undefined;

/** Loads Paddle.js once, prices the plans on sale and opens the checkout for one of them. */
export class PaddleCheckout {
  private onDone: (email?: string) => void = () => {};

  constructor(private readonly config: CheckoutConfig) {}

  plans(): Plan[] {
    return (["monthly", "annual", "lifetime"] as Plan[]).filter((plan) => this.config.prices[plan]);
  }

  async prices(): Promise<Partial<Record<Plan, PlanPrice>>> {
    const paddle = await this.load();
    const items = this.plans().map((plan) => ({ priceId: this.config.prices[plan]!, quantity: 1 }));
    const preview = await paddle.PricePreview({ items });
    const byPrice = new Map(preview.data.details.lineItems.map((item) => [item.price.id, item]));

    return Object.fromEntries(
      this.plans().flatMap((plan) => {
        const item = byPrice.get(this.config.prices[plan]!);

        return item ? [[plan, { label: item.formattedTotals.total, amount: Number(item.totals.total) }]] : [];
      }),
    );
  }

  async open(plan: Plan, onDone: (email?: string) => void): Promise<void> {
    const priceId = this.config.prices[plan];
    if (!priceId) return;

    this.onDone = onDone;
    (await this.load()).Checkout.open({ items: [{ priceId, quantity: 1 }] });
  }

  private load(): Promise<PaddleGlobal> {
    loading ??= new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = "https://cdn.paddle.com/paddle/v2/paddle.js";
      script.onerror = () => reject(new Error("Paddle didn't load"));
      script.onload = () => {
        const paddle = window.Paddle!;
        if (this.config.sandbox) paddle.Environment.set("sandbox");
        paddle.Initialize({
          token: this.config.clientToken,
          eventCallback: (event) => {
            if (event.name === "checkout.completed") this.onDone(event.data?.customer?.email);
          },
        });
        resolve(paddle);
      };
      document.head.append(script);
    });

    return loading;
  }
}
