"use client";

import { CreditCard, Laptop, LogOut, Monitor } from "lucide-react";
import { type FormEvent, useCallback, useEffect, useState } from "react";
import { type AccountInfo, LicenceClient, LicenceError, type LicenceInfo } from "./api";

const client = new LicenceClient();

type Step = { name: "loading" } | { name: "email" } | { name: "code"; email: string } | { name: "account"; info: AccountInfo };

function date(seconds: number): string {
  return new Date(seconds * 1000).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
}

function planLine(licence: LicenceInfo | null): string {
  if (!licence) return "No plan yet";
  if (licence.plan === "lifetime" || licence.endsAt === null) return "Lifetime licence";

  const name = licence.plan === "monthly" ? "Monthly plan" : "Annual plan";
  if (licence.trial) return `${name}, free until ${date(licence.endsAt)}`;

  return `${name}, ${licence.renews ? "renews" : "ends"} ${date(licence.endsAt)}`;
}

export function AccountPanel() {
  const [step, setStep] = useState<Step>({ name: "loading" });
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setStep({ name: "account", info: await client.account() });
    } catch {
      setStep({ name: "email" });
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(undefined);

    try {
      await action();
    } catch (e) {
      setError(e instanceof LicenceError ? e.message : "Something went wrong. Try again.");
    } finally {
      setBusy(false);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const field = new FormData(event.currentTarget);

    if (step.name === "email") {
      const email = String(field.get("email"));
      run(async () => {
        await client.sendCode(email);
        setStep({ name: "code", email });
      });
    } else if (step.name === "code") {
      run(async () => {
        await client.signIn(step.email, String(field.get("code")));
        await load();
      });
    }
  }

  if (step.name === "loading") return <div className="licence-panel" />;

  if (step.name === "email" || step.name === "code") {
    return (
      <form className="licence-panel" onSubmit={submit}>
        <h2>Sign in</h2>
        {step.name === "email" ? (
          <input className="input" name="email" type="email" placeholder="Email" autoComplete="email" required />
        ) : (
          <>
            <p className="lead">We sent a code to {step.email}.</p>
            <input className="input" name="code" inputMode="numeric" placeholder="Code" autoComplete="one-time-code" required />
          </>
        )}
        <button className="button button-primary" disabled={busy}>
          {step.name === "email" ? "Send code" : "Sign in"}
        </button>
        {error && <p className="form-error">{error}</p>}
      </form>
    );
  }

  const { info } = step;

  return (
    <div className="licence-panel">
      <div className="account-head">
        <div>
          <h2>{info.email}</h2>
          <p className="price-note">{planLine(info.licence)}</p>
        </div>
        <button className="button button-ghost" onClick={() => run(async () => { await client.signOut(); setStep({ name: "email" }); })}>
          <LogOut size={16} />
          Sign out
        </button>
      </div>

      {!info.licensed && (
        <a className="button button-primary" href="/buy">
          Buy FlyyLink
        </a>
      )}

      {info.licence?.manageable && (
        <button className="button button-ghost" disabled={busy} onClick={() => run(async () => { window.location.assign(await client.billing()); })}>
          <CreditCard size={16} />
          Manage plan
        </button>
      )}

      {info.licensed && (
        <>
          <h3>
            Computers <span className="price-note">{info.computers.length} of {info.limit}</span>
          </h3>
          {info.computers.length === 0 && <p className="price-note">Sign in on a Mac or PC to add it.</p>}
          <ul className="computers">
            {info.computers.map((computer) => (
              <li key={computer.machine}>
                {computer.platform === "mac" ? <Laptop size={22} /> : <Monitor size={22} />}
                <div>
                  <strong>{computer.name}</strong>
                  <span>Last seen {date(computer.seenAt)}</span>
                </div>
                <button
                  className="button button-ghost"
                  disabled={busy || info.removalsLeft === 0}
                  onClick={() => run(async () => { await client.remove(computer.machine); await load(); })}
                >
                  Remove
                </button>
              </li>
            ))}
          </ul>
          <p className="price-note">{info.removalsLeft} removals left this month.</p>
        </>
      )}

      {error && <p className="form-error">{error}</p>}
    </div>
  );
}
