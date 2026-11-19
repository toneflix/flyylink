(function (root) {
  const flyylink = (root.FlyyLink ??= {});

  /** Phone numbers as people write them on pages: "+234 803 123 4567", "(555) 010-9999", tel: and sms: links. */
  flyylink.numbers = {
    /** The number to dial when `text` is a phone number and nothing else, or null. */
    find(text) {
      const trimmed = String(text ?? '').trim().replace(/^(tel|sms|callto):/i, '');
      if (trimmed.length < 3 || trimmed.length > 32) return null;
      if (!/^\+?[\d\s().\-/]+$/.test(trimmed)) return null;
      if (/^\d{4}[-/.]\d{1,2}[-/.]\d{1,2}$|^\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4}$/.test(trimmed)) return null;

      const digits = trimmed.replace(/\D/g, '');
      if (digits.length < 5 || digits.length > 15) return null;

      return (trimmed.startsWith('+') ? '+' : '') + digits;
    },

    /** The number in a tel: or sms: link, and for sms: the message it starts with. */
    fromLink(href) {
      const match = /^(tel|sms|callto):([^?;]*)(?:[?;](.*))?$/i.exec(String(href ?? '').trim());
      if (!match) return null;

      const number = this.find(safeDecode(match[2]).split(',')[0]);
      if (!number) return null;

      const body = new URLSearchParams(match[3] ?? '').get('body');

      return { scheme: match[1].toLowerCase(), number, body: body ?? '' };
    },

    /** Spaces a number out so it is easy to read back: "+234 803 123 4567". */
    pretty(number) {
      const plus = number.startsWith('+') ? '+' : '';
      const digits = number.replace(/\D/g, '');
      if (digits.length <= 7) return plus + digits;

      const groups = [];
      let rest = digits;
      if (plus && rest.length > 10) {
        groups.push(rest.slice(0, rest.length - 10));
        rest = rest.slice(rest.length - 10);
      }
      groups.push(rest.slice(0, rest.length - 7), rest.slice(-7, -4), rest.slice(-4));

      return plus + groups.filter(Boolean).join(' ');
    },
  };

  function safeDecode(text) {
    try {
      return decodeURIComponent(text);
    } catch {
      return text;
    }
  }
})(globalThis);
