import { base64url, fromBase64 } from "./crypto";

export type TokenKind = "licence";

/** What a computer app checks offline. Times are Unix seconds. */
export interface TokenClaims {
  v: 1;
  kind: TokenKind;
  machine: string;
  email?: string;
  issued: number;
  /** After this the app asks to go online, but keeps working. */
  renew: number;
  /** After this the app locks until it gets a new token. */
  expires: number;
}

/**
 * Signs tokens with ECDSA P-256 so the Mac (CryptoKit) and Windows (.NET) apps can check them
 * with a public key built into the app. A token is `base64url(json).base64url(r||s)`.
 */
export class TokenSigner {
  private key?: Promise<CryptoKey>;

  constructor(private readonly privateKeyPkcs8: string) {}

  async sign(claims: TokenClaims): Promise<string> {
    const body = base64url(new TextEncoder().encode(JSON.stringify(claims)));
    const signature = await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, await this.privateKey(), new TextEncoder().encode(body));

    return `${body}.${base64url(signature)}`;
  }

  private privateKey(): Promise<CryptoKey> {
    this.key ??= crypto.subtle.importKey("pkcs8", fromBase64(this.privateKeyPkcs8), { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);

    return this.key;
  }
}
