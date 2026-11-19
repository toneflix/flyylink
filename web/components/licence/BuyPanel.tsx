"use client";

import { Check, ShoppingBag } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { type LicenceInfo, LicenceClient, type Plan } from "./api";
import { BuySkeleton } from "./Loaders";
import { useAccount } from "./session";
import { PaddleCheckout, type PlanPrice } from "./paddle";

const plans: Record<Plan, { name: string; per: string; perks: string[] }> = {
  monthly: { name: "Monthly", per: "a month", perks: ["Phones and TVs are always free", "Cancel whenever you like"] },
  annual: { name: "Annual", per: "a year", perks: ["Phones and TVs are always free", "Cancel whenever you like"] },
  lifetime: { name: "Lifetime", per: "once", perks: ["Phones and TVs are always free", "Every future update included"] },
};

function computersPerk(limit: number): string {
  return limit === 1 ? "Use it on one Mac or PC" : `Use it on up to ${limit} Macs and PCs`;
}

function date(seconds: number): string {
  return new Date(seconds * 1000).toLocaleDateString(undefined, { day: "numeric", month: "long", year: "numeric" });
}

/** Why someone who is signed in can't buy `plan`, or null when they can. */
function blocked(licence: LicenceInfo | null | undefined, plan: Plan): { label: string; reason: string; action?: string } | null {
  if (!licence) return null;
  if (licence.plan === "lifetime") return { label: "You own FlyyLink", reason: "You already have FlyyLink for life, so there's nothing more to buy." };

  const name = plans[licence.plan].name.toLowerCase();
  if (licence.trial) {
    const until = licence.endsAt ? ` until ${date(licence.endsAt)}` : "";

    return { label: "You're on a free trial", reason: `You're trying the ${name} plan for free${until}.`, action: "change or cancel it" };
  }
  if (licence.plan === plan) return { label: "Your current plan", reason: `You're already on the ${name} plan.`, action: "manage it" };

  return null;
}

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
  const [limit, setLimit] = useState<number>();
  const [boughtBy, setBoughtBy] = useState<string | null>(null);
  const [error, setError] = useState<string>();
  const [loaded, setLoaded] = useState(false);
  const account = useAccount();

  useEffect(() => {
    new LicenceClient()
      .checkout()
      .then((config) => {
        checkout.current = new PaddleCheckout(config);
        setLimit(config.limit);
        const available = checkout.current.plans();
        setOnSale(available);
        setPlan((chosen) => (available.includes(chosen) ? chosen : available[0]));

        return checkout.current.prices();
      })
      .then(setPrices)
      .catch(() => setError("Buying isn't open yet. Check back soon."))
      .finally(() => setLoaded(true));
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

  if (!loaded || account === undefined) return <BuySkeleton />;

  const saving = annualSaving(prices);
  const details = plans[plan];
  const price = prices[plan];
  const perks = limit ? [computersPerk(limit), ...details.perks] : details.perks;
  const block = blocked(account?.licence, plan);

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
      {price?.trial && <p className="price-note">Your first {price.trial} are free</p>}

      <ul className="checks">
        {perks.map((perk) => (
          <li key={perk}>
            <Check size={18} />
            {perk}
          </li>
        ))}
      </ul>

      <button
        className="button button-primary"
        disabled={!checkout.current || onSale.length === 0 || !!error || !!block}
        onClick={() => checkout.current?.open(plan, (email) => setBoughtBy(email ?? ""))}
      >
        <ShoppingBag size={18} />
        {block?.label ?? (plan === "lifetime" ? "Buy FlyyLink" : price?.trial ? "Try it free" : "Subscribe")}
      </button>
      {block && (
        <p className="price-note">
          {block.reason}
          {block.action && (
            <>
              {" "}
              You can {block.action} from <Link href="/account">your account</Link>.
            </>
          )}
        </p>
      )}
      {error && <p className="form-error">{error}</p>}
    </div>
  );
}
