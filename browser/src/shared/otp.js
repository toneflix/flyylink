(function (root) {
  const flyylink = (root.FlyyLink ??= {});

  const codeWords = /\b(otp|one[\s_-]?time|verif\w*|code|token|2fa|mfa|totp|pin|passcode|security[\s_-]?code|sms)\b|otp|verif|passcode/i;
  const otherWords = /zip|postal|post[\s_-]?code|phone|mobile|card|cvc|cvv|promo|coupon|voucher|discount|gift|captcha|country|area|referr?al|invite|search|email|user/i;
  const blockedTypes = new Set(['password', 'email', 'search', 'hidden', 'checkbox', 'radio', 'submit', 'button', 'file', 'date', 'url']);

  /** Finds the box a site wants a sign-in code in, and fills it the way a person typing would. */
  flyylink.otp = {
    /**
     * How sure we are that a field is for a sign-in code, from its attributes: 100 or more is certain,
     * 50 or more is likely, below that it is something else.
     */
    score(field) {
      const type = String(field.type ?? 'text').toLowerCase();
      if (blockedTypes.has(type)) return 0;

      const words = [field.name, field.id, field.placeholder, field.ariaLabel, field.label, field.className]
        .filter(Boolean)
        .join(' ');
      if (otherWords.test(words) && !/otp|one[\s_-]?time|verif/i.test(words)) return 0;

      let score = 0;
      if (String(field.autocomplete ?? '').toLowerCase().includes('one-time-code')) score += 100;
      if (codeWords.test(words)) score += 50;
      if (field.inputMode === 'numeric' || type === 'number' || type === 'tel') score += 10;
      if (field.maxLength >= 4 && field.maxLength <= 10) score += 15;
      if (field.pattern && /\\d|\[0-9\]/.test(field.pattern)) score += 10;

      return score;
    },

    /** What `score` looks at, read from an input element. */
    describe(input) {
      const labels = input.labels ? Array.from(input.labels, (label) => label.textContent) : [];

      return {
        type: input.type,
        name: input.name,
        id: input.id,
        placeholder: input.placeholder,
        ariaLabel: input.getAttribute('aria-label'),
        label: labels.join(' '),
        className: typeof input.className === 'string' ? input.className : '',
        autocomplete: input.getAttribute('autocomplete'),
        inputMode: input.inputMode || input.getAttribute('inputmode'),
        maxLength: input.maxLength,
        pattern: input.pattern,
      };
    },

    /**
     * The code fields on a page: one box for the whole code, or a row of single-character boxes.
     * Returns null when there is none, or more than one could be meant.
     */
    find(doc) {
      const inputs = Array.from(doc.querySelectorAll('input')).filter((input) => visible(input) && !input.disabled && !input.readOnly);

      const boxes = singleCharacterRow(inputs);
      if (boxes) return { kind: 'boxes', inputs: boxes };

      const ranked = inputs
        .map((input) => ({ input, score: this.score(this.describe(input)) }))
        .filter((entry) => entry.score >= 50)
        .sort((a, b) => b.score - a.score);
      if (ranked.length === 0) return null;
      if (ranked.length > 1 && ranked[1].score === ranked[0].score && ranked[0].score < 100) return null;

      return { kind: 'single', inputs: [ranked[0].input] };
    },

    isEmpty(target) {
      return target.inputs.every((input) => input.value.trim() === '');
    },

    /** Types the code in, so sites built with React and the like notice it. */
    fill(target, code) {
      if (target.kind === 'single') {
        setValue(target.inputs[0], code);
        return;
      }

      const characters = Array.from(code);
      target.inputs.forEach((input, index) => setValue(input, characters[index] ?? ''));
      target.inputs[Math.min(characters.length, target.inputs.length) - 1]?.focus();
    },
  };

  /** Four to ten boxes of one character each, side by side in the same form. */
  function singleCharacterRow(inputs) {
    const single = inputs.filter((input) => input.maxLength === 1 && !blockedTypes.has(String(input.type).toLowerCase()));
    if (single.length < 4) return null;

    const groups = new Map();
    for (const input of single) {
      const row = rowOf(input);
      if (!groups.has(row)) groups.set(row, []);
      groups.get(row).push(input);
    }

    const rows = Array.from(groups.values()).filter((row) => row.length >= 4 && row.length <= 10);
    return rows.length === 1 ? rows[0] : null;
  }

  /** The nearest element holding more inputs than this one: the row of boxes, past each box's own wrapper. */
  function rowOf(input) {
    let node = input.parentElement;
    while (node?.parentElement && node.querySelectorAll('input').length === 1) node = node.parentElement;

    return node;
  }

  function visible(input) {
    if (input.type === 'hidden') return false;

    const box = input.getBoundingClientRect();
    if (box.width < 8 || box.height < 8) return false;

    const style = input.ownerDocument.defaultView.getComputedStyle(input);
    return style.visibility !== 'hidden' && style.display !== 'none' && Number(style.opacity) > 0.05;
  }

  function setValue(input, value) {
    const prototype = Object.getPrototypeOf(input);
    const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set;

    input.focus();
    if (setter) setter.call(input, value);
    else input.value = value;
    input.dispatchEvent(new InputEvent('input', { bubbles: true, data: value, inputType: 'insertText' }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  }
})(globalThis);
