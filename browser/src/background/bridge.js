const api = globalThis.browser ?? globalThis.chrome;

/** The native messaging host's name, which the FlyyLink app registers with each browser. */
export const HOST = 'com.toneflix.flyylink';

/**
 * The line to the FlyyLink app on this computer, through the host the app installs. Reconnects by itself and
 * reports whether the app is missing, closed, signed out or ready.
 */
export class Bridge {
  /** Called with each event the app sends, such as a new state, a code or a media button. */
  onEvent = () => {};

  /** Called with "connecting", "missing" (no host installed) or "closed" (the app isn't running). */
  onStatus = () => {};

  #port = null;
  #pending = new Map();
  #nextId = 1;
  #retry = null;

  constructor(browserName) {
    this.browserName = browserName;
  }

  connect() {
    clearTimeout(this.#retry);
    this.onStatus('connecting');

    try {
      this.#port = api.runtime.connectNative(HOST);
    } catch {
      this.#lost('missing');
      return;
    }

    this.#port.onMessage.addListener((message) => this.#receive(message));
    this.#port.onDisconnect.addListener((port) => {
      const error = port.error?.message ?? api.runtime.lastError?.message ?? '';
      this.#lost(/not found|no such native|not registered|forbidden|access/i.test(error) ? 'missing' : 'closed');
    });
    this.post({ type: 'hello', browser: this.browserName });
  }

  post(message) {
    try {
      this.#port?.postMessage(message);
      return Boolean(this.#port);
    } catch {
      return false;
    }
  }

  /** Sends a request and waits for the app's answer: { ok, error }. */
  request(message, timeout = 20_000) {
    const id = String(this.#nextId++);

    return new Promise((resolve) => {
      const timer = setTimeout(() => {
        this.#pending.delete(id);
        resolve({ ok: false, error: 'timeout' });
      }, timeout);

      this.#pending.set(id, (result) => {
        clearTimeout(timer);
        resolve(result);
      });
      if (!this.post({ ...message, id })) this.#pending.get(id)?.({ ok: false, error: 'closed' });
    });
  }

  #receive(message) {
    if (message?.type === 'result') {
      const resolve = this.#pending.get(message.id);
      this.#pending.delete(message.id);
      resolve?.({ ok: Boolean(message.ok), error: message.error });
      return;
    }

    this.onEvent(message);
  }

  #lost(status) {
    this.#port = null;
    for (const resolve of this.#pending.values()) resolve({ ok: false, error: 'closed' });
    this.#pending.clear();

    this.onStatus(status);
    this.#retry = setTimeout(() => this.connect(), status === 'missing' ? 30_000 : 5_000);
  }
}

/** The browser's own name, as the app shows it on phones: "Chrome", "Edge", "Firefox"… */
export function browserName() {
  const agent = navigator.userAgent;
  if (/Firefox\//.test(agent)) return 'Firefox';
  if (/Edg\//.test(agent)) return 'Edge';
  if (/OPR\//.test(agent)) return 'Opera';
  if (/Vivaldi\//.test(agent)) return 'Vivaldi';
  if (navigator.brave) return 'Brave';

  return 'Chrome';
}
