"use client";

import { Check, ShoppingBag } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { LicenceClient, type Plan } from "./api";
import { PaddleCheckout, type PlanPrice } from "./paddle";

const plans: Record<Plan, { name: string; per: string; perks: string[] }> = {
  monthly: { name: "Monthly", per: "a month", perks: ["Up to 5 Macs and PCs", "Phones are always free", "Cancel any time"] },
  annual: { name: "Annual", per: "a year", perks: ["Up to 5 Macs and PCs", "Phones are always free", "Cancel any time"] },
  lifetime: { name: "Lifetime", per: "once", perks: ["Up to 5 Macs and PCs", "Phones are always free", "Every future update"] },
};

/** How much a year on the annual plan saves over twelve months, as a whole percentage. */
function annualSaving(prices: Partial<Record<Plan, PlanPrice>>): number {
  const { monthly, annual } = prices;
  if (!monthly || !annual || monthly.amount <= 0) return 0;

  return Math.round((1 - annual.amount / (monthly.amount * 12)) * 100);
}

export function BuyPanel() {
  const checkout = useRef<PaddleCheckout | null>(null);
  const [onSale, setOnSale] = useState<Plan[]>([]);
  const [plan, setPlan] = useState<Plan>("annual");
  const [prices, setPrices] = useState<Partial<Record<Plan, PlanPrice>>>({});
  const [boughtBy, setBoughtBy] = useState<string | null>(null);
  const [error, setError] = useState<string>();

  useEffect(() => {
    new LicenceClient()
      .checkout()
      .then((config) => {
        checkout.current = new PaddleCheckout(config);
        const available = checkout.current.plans();
        setOnSale(available);
        setPlan((chosen) => (available.includes(chosen) ? chosen : available[0]));

        return checkout.current.prices();
      })
      .then(setPrices)
      .catch(() => setError("Buying isn't open yet. Check back soon."));
  }, []);

  if (boughtBy !== null) {
    return (
      <div className="licence-panel">
        <h2>Thank you!</h2>
        <p className="lead">
          Open FlyyLink on your computer and sign in with {boughtBy || "the email you used"}.
        </p>
      </div>
    );
  }

  const saving = annualSaving(prices);
  const details = plans[plan];
  const price = prices[plan];

  return (
    <div className="licence-panel">
      {onSale.length > 1 && (
        <div className="plan-switch" role="radiogroup" aria-label="Plan">
          {onSale.map((option) => (
            <button key={option} role="radio" aria-checked={option === plan} className={option === plan ? "active" : ""} onClick={() => setPlan(option)}>
              {plans[option].name}
              {option === "annual" && saving > 0 && <span className="plan-saving">-{saving}%</span>}
            </button>
          ))}
        </div>
      )}

      {onSale.length <= 1 && <span className="eyebrow">{details.name}</span>}
      <div className="price-row">
        {price && <p className="price">{price.label}</p>}
        {price && <p className="price-note">{details.per}</p>}
      </div>

      <ul className="checks">
        {details.perks.map((perk) => (
          <li key={perk}>
            <Check size={18} />
            {perk}
          </li>
        ))}
      </ul>

      <button
        className="button button-primary"
        disabled={!checkout.current || onSale.length === 0 || !!error}
        onClick={() => checkout.current?.open(plan, (email) => setBoughtBy(email ?? ""))}
      >
        <ShoppingBag size={18} />
        {plan === "lifetime" ? "Buy FlyyLink" : "Subscribe"}
      </button>
      {error && <p className="form-error">{error}</p>}
    </div>
  );
}
