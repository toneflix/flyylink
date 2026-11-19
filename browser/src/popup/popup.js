const api = globalThis.browser ?? globalThis.chrome;
const { icons } = globalThis.FlyyLink;

const DOWNLOAD_PAGE = 'https://flyylink.toneflix.net/#download';
const ALL_SITES = { origins: ['<all_urls>'] };

const main = document.getElementById('main');
const status = document.getElementById('status');
let current = null;
let statusTimer = null;

document.querySelectorAll('[data-icon]').forEach((element) => (element.innerHTML = icons[element.dataset.icon]));
document.getElementById('settings').addEventListener('click', () => api.runtime.openOptionsPage());

refresh();
setInterval(() => current?.status !== 'ready' && refresh(), 1500);

async function refresh() {
  const state = await ask({ kind: 'popup' });
  if (!state || sameAs(state)) return;

  current = state;
  document.getElementById('computer').textContent = state.computer ? `On ${state.computer}` : '';
  main.replaceChildren(...(await render(state)));
}

function sameAs(state) {
  return JSON.stringify(state) === JSON.stringify(current);
}

async function render(state) {
  switch (state.status) {
    case 'connecting':
      return [empty('spinner', 'Finding FlyyLink…', 'This only takes a moment.')];
    case 'missing':
      return [empty('laptop', 'Get FlyyLink for your computer', 'The add-on works with the FlyyLink app, so install it first and pair your phone.', button('Get FlyyLink', 'primary', () => api.tabs.create({ url: DOWNLOAD_PAGE })))];
    case 'closed':
      return [empty('plug', "FlyyLink isn't open", 'Open the app and the add-on will connect by itself.', button('Open FlyyLink', 'primary', launch))];
    case 'locked':
      return [empty('key', 'Sign in to FlyyLink', 'Sign in on the app to use your phone from the browser.', button('Open FlyyLink', 'primary', launch))];
  }

  if (state.devices.length === 0) {
    return [empty('smartphone', 'Pair your phone first', "Pair your phone in FlyyLink and it'll show up here.", button('Open FlyyLink', 'secondary', launch))];
  }

  const parts = [];
  if (!(await api.permissions.contains(ALL_SITES))) parts.push(permissionBanner());
  if (state.code) parts.push(codeCard(state.code));
  parts.push(devicePicker(state));
  parts.push(pageTiles(state));
  if (state.downloads.length > 0) parts.push(downloadList(state));

  return parts.filter(Boolean);
}

// Pieces

function empty(icon, title, text, action) {
  const box = element('div', 'empty');
  const art = element('div', 'art');
  if (icon === 'spinner') art.append(element('div', 'spinner'));
  else art.innerHTML = icons[icon];

  box.append(art, element('h1', '', title), element('p', '', text));
  if (action) box.append(action);
  return box;
}

/** Firefox asks before an add-on reads pages, and codes and phone numbers need that. */
function permissionBanner() {
  const box = element('div', 'banner');
  box.append(
    element('p', '', 'Let FlyyLink work on the sites you visit, so it can fill codes and turn phone numbers into calls.'),
    button('Allow on all sites', 'primary', async () => {
      if (await api.permissions.request(ALL_SITES)) box.remove();
    }),
  );
  return box;
}

function codeCard(code) {
  const box = element('div', 'code');
  const digits = element('div', 'digits');
  digits.append(element('span', 'value', code.code), element('span', 'from', code.app ? `From ${code.app} on ${code.device}` : `From ${code.device}`));

  const copy = element('button', 'icon-button');
  copy.title = 'Copy the code';
  copy.innerHTML = icons.copy;
  copy.addEventListener('click', async () => {
    await navigator.clipboard.writeText(code.code);
    copy.innerHTML = icons.check;
    say('Copied', true);
  });

  box.append(digits, copy);
  return box;
}

function devicePicker(state) {
  if (state.devices.length < 2) return null;

  const row = element('div', 'devices');
  for (const device of state.devices) {
    const chip = element('button', 'chip');
    chip.innerHTML = icons[deviceIcon(device)];
    chip.append(device.name);
    chip.setAttribute('aria-pressed', String(device.id === state.device));
    chip.addEventListener('click', async () => {
      await ask({ kind: 'choose', device: device.id });
      refresh();
    });
    row.append(chip);
  }
  return row;
}

function pageTiles(state) {
  const device = state.devices.find((item) => item.id === state.device) ?? state.devices[0];
  const box = element('div', 'tiles');

  if (!state.page) {
    box.append(element('p', 'note', 'Open a web page to send it to your phone.'));
    return box;
  }

  box.append(
    tile(deviceIcon(device), `Open on ${device.name}`, `Right where you are on ${state.page.host}`, () =>
      ask({ kind: 'openHere', device: device.id }),
    ),
  );

  if (state.page.hasVideo) {
    for (const tv of state.tvs) {
      box.append(tile('cast', `Play on ${tv.name}`, 'The video on this page', () => ask({ kind: 'playHere', target: tv.id }), true));
    }
    for (const screen of state.devices.filter((item) => item.type === 'tv')) {
      box.append(tile('tv', `Watch on ${screen.name}`, 'Opens this page on the TV', () => ask({ kind: 'playHere', target: screen.id }), true));
    }
  }

  return box;
}

function downloadList(state) {
  const box = element('div', 'downloads');
  box.append(element('p', 'eyebrow', 'Recent downloads'));

  for (const item of state.downloads) {
    const row = element('div', 'download');
    const glyph = element('span', 'glyph');
    glyph.innerHTML = icons.file;

    const send = button('Send', 'secondary', async () => {
      send.disabled = true;
      const ok = await ask({ kind: 'sendDownload', id: item.id, device: state.device });
      say(ok ? 'Sending it to your phone' : "We couldn't send that file.", ok);
      send.disabled = false;
    });
    row.append(glyph, element('span', 'name', item.name), element('span', 'size', size(item.size)), send);
    box.append(row);
  }
  return box;
}

function tile(icon, title, detail, action, gold = false) {
  const item = element('button', 'tile');
  const glyph = element('span', gold ? 'glyph gold' : 'glyph');
  glyph.innerHTML = icons[icon];

  const words = element('span', 'words');
  words.append(element('strong', '', title), element('span', '', detail));
  item.append(glyph, words);

  item.addEventListener('click', async () => {
    if (item.getAttribute('aria-busy') === 'true') return;

    item.setAttribute('aria-busy', 'true');
    const result = await action();
    item.removeAttribute('aria-busy');
    if (result) say(result.text, result.ok);
  });
  return item;
}

function button(label, kind, action) {
  const item = element('button', `button ${kind}`, label);
  item.addEventListener('click', action);
  return item;
}

// Helpers

function launch() {
  ask({ kind: 'launch' });
  main.replaceChildren(empty('spinner', 'Opening FlyyLink…', 'The add-on connects as soon as the app is ready.'));
  current = { ...current, status: 'opening' };
}

function say(text, ok) {
  clearTimeout(statusTimer);
  status.className = ok ? 'status' : 'status bad';
  status.innerHTML = ok ? icons.check : icons.alert;
  status.append(text);
  status.hidden = false;
  statusTimer = setTimeout(() => (status.hidden = true), 2600);
}

function deviceIcon(device) {
  return { tablet: 'tablet', tv: 'tv', desktop: 'laptop', laptop: 'laptop' }[device?.type] ?? 'smartphone';
}

function size(bytes) {
  if (!bytes) return '';
  const units = ['B', 'KB', 'MB', 'GB'];
  const power = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  return `${(bytes / 1024 ** power).toFixed(power > 1 ? 1 : 0)} ${units[power]}`;
}

function element(tag, className, text) {
  const item = document.createElement(tag);
  if (className) item.className = className;
  if (text !== undefined) item.textContent = text;
  return item;
}

function ask(message) {
  return api.runtime.sendMessage(message).catch(() => null);
}
