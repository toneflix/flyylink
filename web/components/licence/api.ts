export interface Computer {
  machine: string;
  name: string;
  platform: "mac" | "windows";
  activatedAt: number;
  seenAt: number;
}

export type Plan = "monthly" | "annual" | "lifetime";

export interface LicenceInfo {
  plan: Plan;
  endsAt: number | null;
  trial: boolean;
  renews: boolean;
  manageable: boolean;
}

export interface AccountInfo {
  email: string;
  licensed: boolean;
  licence: LicenceInfo | null;
  computers: Computer[];
  limit: number;
  removalsLeft: number;
}

export interface CheckoutConfig {
  clientToken: string;
  sandbox: boolean;
  prices: Partial<Record<Plan, string>>;
}

const messages: Record<string, string> = {
  bad_email: "That email doesn't look right.",
  bad_code: "That code didn't work. Ask for a new one.",
  too_many_codes: "That's a lot of codes. Please try again in an hour.",
  removal_limit: "You've already removed 3 computers this month. You can remove more next month.",
  signed_out: "You're signed out.",
  no_subscription: "You don't have a plan to manage yet.",
};

/** Calls the licence API on this site. Errors carry the server's code, worded for people. */
export class LicenceClient {
  async checkout(): Promise<CheckoutConfig> {
    return this.call("GET", "/api/checkout");
  }

  async sendCode(email: string): Promise<void> {
    await this.call("POST", "/api/code", { email });
  }

  async signIn(email: string, code: string): Promise<void> {
    await this.call("POST", "/api/session", { email, code });
  }

  async signOut(): Promise<void> {
    await this.call("POST", "/api/session/end");
  }

  async account(): Promise<AccountInfo> {
    return this.call("GET", "/api/account");
  }

  async remove(machine: string): Promise<void> {
    await this.call("POST", `/api/account/computers/${encodeURIComponent(machine)}/remove`);
  }

  /** Paddle's page for cancelling the subscription or changing how to pay. */
  async billing(): Promise<string> {
    return (await this.call<{ url: string }>("POST", "/api/account/billing")).url;
  }

  private async call<T>(method: string, path: string, body?: unknown): Promise<T> {
    const response = await fetch(path, {
      method,
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });

    if (!response.ok) {
      const { error } = (await response.json().catch(() => ({}))) as { error?: string };
      throw new LicenceError(error ?? "server_error", messages[error ?? ""] ?? "Something went wrong. Try again.");
    }

    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  }
}

export class LicenceError extends Error {
  constructor(
    readonly code: string,
    message: string,
  ) {
    super(message);
  }
}
