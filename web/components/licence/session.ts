"use client";

import { useEffect, useState } from "react";
import { type AccountInfo, LicenceClient } from "./api";

type Listener = (account: AccountInfo | null) => void;

/** The signed-in account, fetched once per page and shared by the header and the panels. */
export class Session {
  private static loading: Promise<AccountInfo | null> | null = null;
  private static readonly listeners = new Set<Listener>();

  static account(): Promise<AccountInfo | null> {
    Session.loading ??= new LicenceClient().account().catch(() => null);

    return Session.loading;
  }

  /** Tells everything on the page that the person signed in or out. */
  static changed(account: AccountInfo | null): void {
    Session.loading = Promise.resolve(account);
    Session.listeners.forEach((listener) => listener(account));
  }

  static subscribe(listener: Listener): () => void {
    Session.listeners.add(listener);

    return () => Session.listeners.delete(listener);
  }
}

/** The signed-in account, null when signed out, or undefined while it loads. */
export function useAccount(): AccountInfo | null | undefined {
  const [account, setAccount] = useState<AccountInfo | null>();

  useEffect(() => {
    Session.account().then(setAccount);

    return Session.subscribe(setAccount);
  }, []);

  return account;
}
