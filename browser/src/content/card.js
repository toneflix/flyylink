(function (root) {
  const flyylink = (root.FlyyLink ??= {});
  const icons = flyylink.icons;

  const style = `
    :host { all: initial; }
    * { box-sizing: border-box; font-family: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
    .theme {
      --surface: #ffffff; --surface-2: #f3eee2; --text: #17140d; --muted: #5f5a4f; --line: #e7e0cf;
      --cyan: #00afff; --cyan-ink: #0077b6; --gold: #f6c101; --gold-2: #bb8c01; --on-gold: #241a00;
      --shadow: 0 24px 60px -18px rgb(60 40 0 / 0.35), 0 2px 6px rgb(60 40 0 / 0.08);
      color: var(--text);
    }
    @media (prefers-color-scheme: dark) {
      .theme { --surface: #161a1f; --surface-2: #1d2228; --text: #f2efe8; --muted: #a5a39c; --line: #2a3038; --cyan-ink: #45ccff;
        --shadow: 0 24px 60px -18px rgb(0 0 0 / 0.7), 0 2px 6px rgb(0 0 0 / 0.3); }
    }
    .card {
      position: fixed; right: 20px; bottom: 20px; z-index: 2147483647; width: 340px; max-width: calc(100vw - 32px);
      background: var(--surface); border: 1px solid var(--line); border-radius: 22px; box-shadow: var(--shadow);
      padding: 16px; animation: rise .22s cubic-bezier(.2,.8,.2,1);
    }
    .top { display: flex; align-items: center; gap: 8px; color: var(--muted); font-size: 12px; font-weight: 600; letter-spacing: .02em; }
    .top .grow { flex: 1; }
    .close { all: unset; cursor: pointer; display: grid; place-items: center; width: 28px; height: 28px; border-radius: 999px; color: var(--muted); }
    .close:hover { background: var(--surface-2); color: var(--text); }
    .close svg { width: 16px; height: 16px; }
    .number { margin: 10px 0 2px; font-size: 24px; font-weight: 750; letter-spacing: -.01em; font-variant-numeric: tabular-nums; }
    .hint { margin: 0 0 14px; color: var(--muted); font-size: 13px; line-height: 1.4; }
    .phones { display: flex; gap: 6px; flex-wrap: wrap; margin: -4px 0 14px; }
    .phones button { all: unset; cursor: pointer; display: inline-flex; align-items: center; gap: 6px; padding: 6px 12px; border-radius: 999px;
      border: 1px solid var(--line); font-size: 12.5px; font-weight: 600; color: var(--muted); }
    .phones button[aria-pressed="true"] { border-color: transparent; background: color-mix(in srgb, var(--cyan) 16%, transparent); color: var(--cyan-ink); }
    .phones svg { width: 14px; height: 14px; }
    .actions { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .actions.one { grid-template-columns: 1fr; }
    .button { all: unset; cursor: pointer; display: inline-flex; align-items: center; justify-content: center; gap: 8px; height: 42px;
      border-radius: 14px; font-size: 14px; font-weight: 700; transition: transform .12s ease, filter .12s ease; }
    .button:active { transform: scale(.97); }
    .button:focus-visible { outline: 2px solid var(--cyan); outline-offset: 2px; }
    .primary { background: linear-gradient(180deg, var(--gold), var(--gold-2)); color: var(--on-gold); box-shadow: 0 6px 16px -8px rgb(187 140 1 / .9); }
    .primary:hover { filter: brightness(1.05); }
    .secondary { background: var(--surface-2); color: var(--text); }
    .secondary:hover { filter: brightness(.97); }
    .button[disabled] { opacity: .55; cursor: default; }
    textarea { width: 100%; min-height: 84px; resize: vertical; margin: 0 0 10px; padding: 10px 12px; border-radius: 14px; border: 1px solid var(--line);
      background: var(--surface-2); color: var(--text); font-size: 14px; line-height: 1.4; outline: none; }
    textarea:focus { border-color: var(--cyan); }
    .chip {
      position: absolute; z-index: 2147483647; display: inline-flex; align-items: center; gap: 8px; padding: 6px 6px 6px 10px;
      background: var(--surface); border: 1px solid var(--line); border-radius: 999px; box-shadow: var(--shadow); animation: rise .18s ease;
      font-size: 13px; color: var(--muted); white-space: nowrap;
    }
    .chip .fill { all: unset; cursor: pointer; display: inline-flex; align-items: center; gap: 6px; padding: 6px 12px; border-radius: 999px;
      background: linear-gradient(180deg, var(--gold), var(--gold-2)); color: var(--on-gold); font-weight: 700; font-variant-numeric: tabular-nums; letter-spacing: .04em; }
    .chip .close { width: 24px; height: 24px; }
    .toast {
      position: fixed; left: 50%; bottom: 28px; z-index: 2147483647; transform: translateX(-50%); display: inline-flex; align-items: center; gap: 8px;
      padding: 10px 16px; border-radius: 999px; background: #17140d; color: #f2efe8; font-size: 13.5px; font-weight: 600;
      box-shadow: 0 16px 40px -12px rgb(0 0 0 / .5); animation: rise .2s ease;
    }
    .toast svg { width: 16px; height: 16px; color: #00eaff; }
    .toast.bad svg { color: #f6c101; }
    @keyframes rise { from { opacity: 0; transform: translateY(8px); } }
    .toast { animation-name: toast; }
    @keyframes toast { from { opacity: 0; transform: translate(-50%, 8px); } }
  `;

  /** The add-on's own small pieces of UI on a page, kept in a shadow root so the page's styles can't reach them. */
  class Card {
    #host = null;
    #shadow = null;
    #card = null;
    #chip = null;
    #toastTimer = null;

    #root() {
      if (this.#shadow && this.#host.isConnected) return this.#shadow;

      this.#host = document.createElement('flyylink-card');
      this.#shadow = this.#host.attachShadow({ mode: 'closed' });
      this.#shadow.innerHTML = `<style>${style}</style><div class="theme"></div>`;
      document.documentElement.append(this.#host);

      return this.#shadow;
    }

    #theme() {
      return this.#root().querySelector('.theme');
    }

    /** A small button by a code box: "Fill 482913". */
    showCode({ code, app }, field, onFill) {
      this.#chip?.remove();

      const chip = document.createElement('div');
      chip.className = 'chip';
      chip.innerHTML = `${icons.mark}<span></span><button class="fill" type="button">${icons.key}<span></span></button><button class="close" type="button" aria-label="Dismiss">${icons.x}</button>`;
      chip.querySelector('span').textContent = app ? `From ${app}` : 'From your phone';
      chip.querySelector('.fill span').textContent = `Fill ${code}`;
      chip.querySelector('.fill').addEventListener('click', () => {
        chip.remove();
        onFill();
      });
      chip.querySelector('.close').addEventListener('click', () => chip.remove());

      const box = field.getBoundingClientRect();
      chip.style.left = `${Math.max(8, box.left + window.scrollX)}px`;
      chip.style.top = `${box.bottom + window.scrollY + 8}px`;

      this.#theme().append(chip);
      this.#chip = chip;
    }

    hideCode() {
      this.#chip?.remove();
      this.#chip = null;
    }

    /**
     * The card for a phone number someone clicked or picked: call it, or write a text, from one of their phones.
     * `phones` are the devices that can call or text; `send` carries out { action, number, device, body }.
     */
    showNumber({ number, body = '', startWithText = false }, phones, send) {
      this.close();

      let chosen = phones[0];
      const card = document.createElement('div');
      card.className = 'card';
      card.setAttribute('role', 'dialog');
      card.setAttribute('aria-label', 'Call or text with your phone');
      this.#theme().append(card);
      this.#card = card;

      const render = (texting) => {
        const canCall = chosen.canCall;
        const canText = chosen.canText;
        card.innerHTML = `
          <div class="top">${icons.mark}<span class="grow">FlyyLink</span><button class="close" type="button" aria-label="Close">${icons.x}</button></div>
          <div class="number"></div>
          <p class="hint"></p>
          ${phones.length > 1 ? '<div class="phones"></div>' : ''}
          ${texting ? '<textarea placeholder="Write a message"></textarea>' : ''}
          <div class="actions"></div>`;
        card.querySelector('.number').textContent = flyylink.numbers.pretty(number);
        card.querySelector('.hint').textContent = texting
          ? `Your text goes out from ${chosen.name}.`
          : `Call or text this number from ${chosen.name}.`;
        card.querySelector('.close').addEventListener('click', () => this.close());

        const picker = card.querySelector('.phones');
        for (const phone of picker ? phones : []) {
          const button = document.createElement('button');
          button.type = 'button';
          button.innerHTML = `${phone.type === 'tablet' ? icons.tablet : icons.smartphone}<span></span>`;
          button.querySelector('span').textContent = phone.name;
          button.setAttribute('aria-pressed', String(phone.id === chosen.id));
          button.addEventListener('click', () => {
            chosen = phone;
            render(texting);
          });
          picker.append(button);
        }

        const actions = card.querySelector('.actions');
        if (texting) {
          const message = card.querySelector('textarea');
          message.value = body;
          actions.append(
            this.#button('secondary', icons.phone, 'Back', () => render(false)),
            this.#button('primary', icons.send, 'Send', () => {
              if (!message.value.trim()) return message.focus();
              send({ action: 'text', number, device: chosen.id, body: message.value.trim() });
              this.close();
            }),
          );
          message.focus();
          message.addEventListener('keydown', (event) => {
            if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) actions.lastChild.click();
          });
          return;
        }

        if (canCall) {
          actions.append(this.#button('primary', icons.phone, 'Call', () => {
            send({ action: 'call', number, device: chosen.id });
            this.close();
          }));
        }
        if (canText) actions.append(this.#button(canCall ? 'secondary' : 'primary', icons.message, 'Text', () => render(true)));
        if (actions.children.length === 1) actions.classList.add('one');
      };

      render(startWithText && chosen.canText);
    }

    close() {
      this.#card?.remove();
      this.#card = null;
    }

    toast(message, ok = true) {
      clearTimeout(this.#toastTimer);
      this.#theme().querySelector('.toast')?.remove();

      const toast = document.createElement('div');
      toast.className = ok ? 'toast' : 'toast bad';
      toast.setAttribute('role', 'status');
      toast.innerHTML = `${ok ? icons.check : icons.alert}<span></span>`;
      toast.querySelector('span').textContent = message;
      this.#theme().append(toast);
      this.#toastTimer = setTimeout(() => toast.remove(), 3200);
    }

    #button(kind, icon, label, onClick) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = `button ${kind}`;
      button.innerHTML = `${icon}<span></span>`;
      button.querySelector('span').textContent = label;
      button.addEventListener('click', onClick);

      return button;
    }
  }

  flyylink.Card = Card;
})(globalThis);
