"use client";

import Link from "next/link";
import { useAccount } from "./licence/session";

/** Buy for visitors, Account once they're signed in. */
export function AccountLink() {
  return useAccount() ? <Link href="/account">Account</Link> : <Link href="/buy">Buy</Link>;
}
