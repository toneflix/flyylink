import type { Metadata } from "next";
import { BuyPanel } from "@/components/licence/BuyPanel";

export const metadata: Metadata = {
  title: "Buy",
  description: "Pay monthly, yearly or once, and use FlyyLink on all your computers.",
};

export default function BuyPage() {
  return (
    <main className="licence-page">
      <div className="container">
        <h1>Buy FlyyLink</h1>
        <p className="lead">Pay monthly, yearly or once.</p>
        <BuyPanel />
        <p className="price-note">
          Already have a plan? <a href="/account">Manage your computers</a>.
        </p>
      </div>
    </main>
  );
}
