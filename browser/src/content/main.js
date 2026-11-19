(function (root) {
  const flyylink = root.FlyyLink;
  const api = root.browser ?? root.chrome;
  if (flyylink.contentStarted) return;
  flyylink.contentStarted = true;

  const card = new flyylink.Card();
  const media = new flyylink.MediaWatcher();
  const isTop = window === window.top;
  let settings = { ...flyylink.settings.defaults };
  let phones = [];
  let lastNumber = null;

  flyylink.settings.load().then((loaded) => (settings = loaded));
  flyylink.settings.watch((loaded) => {
    settings = loaded;
    report(media.players());
  });

  ask({ kind: 'phones' }).then((list) => (phones = list ?? []));

  media.onChange = report;
  media.start();

  function report(players) {
    send({ kind: 'media', players: settings.media ? players : [] });
  }

  function send(message) {
    try {
      api.runtime.sendMessage(message).catch(() => {});
    } catch {
      // The add-on was updated or turned off; this page keeps its old copy until it reloads.
    }
  }

  async function ask(message) {
    try {
      return await api.runtime.sendMessage(message);
    } catch {
      return null;
    }
  }

  api.runtime.onMessage.addListener((message, _sender, reply) => {
    switch (message.kind) {
      case 'phones':
        phones = message.phones;
        break;
      case 'code':
        offerCode(message);
        break;
      case 'control':
        media.control(message.key, message.action, message.seekTo);
        break;
      case 'call':
        if (message.active) media.pauseForCall();
        else media.resumeAfterCall();
        break;
      case 'number':
        if (isTop) card.showNumber(message, message.phones, sendNumber);
        break;
      case 'toast':
        if (isTop) card.toast(message.text, message.ok);
        break;
      case 'position':
        if (isTop) {
          reply(position());
          return false;
        }
        break;
      case 'video': {
        const video = describeVideo();
        if (video) {
          reply(video);
          return false;
        }
        break;
      }
    }

    return false;
  });

  // Calls and texts from tel: and sms: links, while a phone that can is connected.
  document.addEventListener(
    'click',
    (event) => {
      if (!settings.links || event.defaultPrevented || event.button !== 0) return;

      const link = event.target instanceof Element ? event.target.closest('a[href]') : null;
      const parsed = link ? flyylink.numbers.fromLink(link.getAttribute('href')) : null;
      if (!parsed) return;

      const able = phones.filter((phone) => (parsed.scheme === 'sms' ? phone.canText : phone.canCall || phone.canText));
      if (able.length === 0) return;

      event.preventDefault();
      event.stopPropagation();
      if (isTop) card.showNumber({ number: parsed.number, body: parsed.body, startWithText: parsed.scheme === 'sms' }, able, sendNumber);
      else send({ kind: 'showNumber', number: parsed.number, body: parsed.body, startWithText: parsed.scheme === 'sms' });
    },
    true,
  );

  async function sendNumber(request) {
    const result = await ask({ kind: 'dial', ...request });
    const phone = phones.find((item) => item.id === request.device)?.name ?? 'your phone';
    if (result?.ok) card.toast(request.action === 'call' ? `Calling from ${phone}` : `Sent from ${phone}`);
    else card.toast(`We couldn't reach ${phone}. Check that it's connected.`, false);
  }

  // Tells the add-on when the selected text is a phone number, so its menu can offer to call it.
  let selectionTimer = null;
  document.addEventListener('selectionchange', () => {
    clearTimeout(selectionTimer);
    selectionTimer = setTimeout(() => {
      const number = flyylink.numbers.find(String(document.getSelection() ?? ''));
      if (number === lastNumber) return;

      lastNumber = number;
      send({ kind: 'selection', number });
    }, 150);
  });

  // A code box got focus: offer the code that arrived on the phone in the last few minutes.
  document.addEventListener('focusin', async (event) => {
    if (settings.codes === 'off' || !(event.target instanceof HTMLInputElement)) return;
    if (flyylink.otp.score(flyylink.otp.describe(event.target)) < 50 && event.target.maxLength !== 1) return;

    const code = await ask({ kind: 'recentCode' });
    if (code) offerCode(code);
  });

  function offerCode(code) {
    if (settings.codes === 'off') return;

    const target = flyylink.otp.find(document);
    if (!target || !flyylink.otp.isEmpty(target)) return;

    const fill = () => {
      flyylink.otp.fill(target, code.code);
      send({ kind: 'codeUsed', code: code.code });
      card.toast(code.app ? `Filled in your code from ${code.app}` : 'Filled in your code from your phone');
    };

    if (settings.codes === 'auto') fill();
    else card.showCode(code, target.inputs[0], fill);
  }

  /** Where the reader is: the main video's second, and the first words on screen. */
  function position() {
    const video = media.mainVideo();
    const seconds = video && video.currentTime > 0 ? video.currentTime : 0;

    return { seconds, text: seconds ? '' : firstWordsOnScreen() };
  }

  function firstWordsOnScreen() {
    const blocks = document.querySelectorAll('p, h1, h2, h3, h4, li, blockquote, td, figcaption');
    for (const block of blocks) {
      const box = block.getBoundingClientRect();
      if (box.bottom < 40 || box.top > window.innerHeight * 0.6 || box.height === 0) continue;

      const text = block.innerText?.trim() ?? '';
      if (text.split(/\s+/).length >= 4) return text;
    }

    return '';
  }

  function describeVideo() {
    const video = media.mainVideo();
    if (!video) return null;

    const src = video.currentSrc || video.src;
    return {
      src: flyylink.page.isPlayableByTv(src) ? src : null,
      mime: flyylink.page.mimeFor(src),
      title: navigator.mediaSession?.metadata?.title || document.title,
      seconds: video.currentTime,
      isTop,
    };
  }
})(globalThis);
