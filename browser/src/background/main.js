import '../shared/numbers.js';
import '../shared/page.js';
import '../shared/settings.js';
import { Bridge, browserName } from './bridge.js';

const api = globalThis.browser ?? globalThis.chrome;
const { numbers, page, settings: store } = globalThis.FlyyLink;

/** How long a code from the phone stays on offer. */
const CODE_LIFETIME = 3 * 60 * 1000;

/** What the add-on knows, for the popup and the menus. */
const state = {
  /** "connecting", "missing", "closed", "locked" or "ready". */
  status: 'connecting',
  computer: '',
  platform: '',
  devices: [],
  tvs: [],
};

let settings = { ...store.defaults };
let lastCode = null;
let selectedNumber = null;
const players = new Map();
const askedDownloads = new Map();
let mediaTimer = null;

const bridge = new Bridge(browserName());
bridge.onStatus = (status) => update({ status, ...(status === 'ready' ? {} : { devices: [], tvs: [] }) });
bridge.onEvent = handleEvent;

store.load().then((loaded) => (settings = loaded));
store.watch((loaded) => (settings = loaded));
bridge.connect();

function handleEvent(event) {
  switch (event.type) {
    case 'state':
      update({
        status: event.status,
        computer: event.computer ?? '',
        platform: event.platform ?? '',
        devices: event.devices ?? [],
        tvs: event.tvs ?? [],
      });
      if (event.status === 'ready') sendMedia();
      break;
    case 'code':
      lastCode = { code: event.code, app: event.app, device: event.device, at: Date.now() };
      activeTab().then((tab) => tab && message(tab.id, { kind: 'code', ...lastCode }));
      break;
    case 'call':
      if (settings.pauseForCalls) tabsWithMedia().forEach((tabId) => message(tabId, { kind: 'call', active: event.active }));
      break;
    case 'control': {
      const [tabId, frameId, key] = String(event.player).split(':');
      message(Number(tabId), { kind: 'control', key, action: event.action, seekTo: event.seekTo }, { frameId: Number(frameId) });
      break;
    }
  }
}

function update(changes) {
  Object.assign(state, changes);
  buildMenus();
  updateBadge();
  broadcastPhones();
}

// Devices

/** The device the user picked in the popup, or else the first phone, or else anything. */
async function chosenDevice() {
  const { device } = await api.storage.local.get('device');
  return state.devices.find((d) => d.id === device) ?? state.devices.find(isPhone) ?? state.devices[0] ?? null;
}

function isPhone(device) {
  return device.type === 'phone' || device.type === 'tablet';
}

function talkers() {
  return state.devices.filter((device) => device.canCall || device.canText);
}

function broadcastPhones() {
  api.tabs.query({}).then((tabs) => tabs.forEach((tab) => message(tab.id, { kind: 'phones', phones: talkers() })));
}

// Sending

async function share(device, item) {
  const result = await bridge.request({ type: 'share', device: device.id, ...item });
  return result.ok;
}

/** Sends the tab to a device, opening where the user is. */
async function sendTab(tab, device) {
  const where = await message(tab.id, { kind: 'position' }, { frameId: 0 });
  const link = tab.url && /^https?:/.test(tab.url) ? page.positionUrl(tab.url, where ?? {}) : null;
  if (!link) return { ok: false, text: "This page can't be opened on another device." };

  const ok = await share(device, { link });
  return { ok, text: ok ? `Opened on ${device.name}` : `We couldn't reach ${device.name}.` };
}

/** Plays the tab's video on a TV: straight from its address on a network TV, or as the page on a FlyyLink TV. */
async function playTab(tab, target) {
  const tv = state.tvs.find((item) => item.id === target);
  if (!tv) {
    const device = state.devices.find((item) => item.id === target);
    return device ? sendTab(tab, device) : { ok: false, text: 'That TV is no longer around.' };
  }

  const video = await message(tab.id, { kind: 'video' });
  if (!video?.src) return { ok: false, text: "This site streams its video in a way TVs can't play directly. Send the page to a FlyyLink TV instead." };

  const result = await bridge.request({ type: 'playOnTv', tv: tv.id, url: video.src, title: video.title || tab.title, mime: video.mime });
  return { ok: result.ok, text: result.ok ? `Playing on ${tv.name}` : `We couldn't reach ${tv.name}.` };
}

async function sendDownload(id, device) {
  const [item] = await api.downloads.search({ id });
  if (!item?.filename || item.state !== 'complete') return false;

  return share(device, { file: item.filename });
}

// Menus

const MENU_CONTEXTS = ['page', 'link', 'image', 'video', 'audio', 'selection'];

function buildMenus() {
  api.contextMenus.removeAll(() => {
    if (state.status !== 'ready' || state.devices.length === 0) return;

    if (state.devices.length === 1) {
      api.contextMenus.create({ id: `send:${state.devices[0].id}`, title: `Send to ${state.devices[0].name}`, contexts: MENU_CONTEXTS });
    } else {
      api.contextMenus.create({ id: 'send', title: 'Send to', contexts: MENU_CONTEXTS });
      for (const device of state.devices) {
        api.contextMenus.create({ id: `send:${device.id}`, parentId: 'send', title: device.name, contexts: MENU_CONTEXTS });
      }
    }

    for (const tv of state.tvs) {
      api.contextMenus.create({ id: `tv:${tv.id}`, title: `Play on ${tv.name}`, contexts: ['video'] });
    }

    const phone = talkers()[0];
    if (phone) {
      api.contextMenus.create({ id: 'call', title: 'Call “%s” from your phone', contexts: ['selection'], visible: Boolean(selectedNumber) });
      api.contextMenus.create({ id: 'text', title: 'Text “%s” from your phone', contexts: ['selection'], visible: Boolean(selectedNumber) });
    }
  });
}

api.contextMenus.onClicked.addListener(async (info, tab) => {
  const id = String(info.menuItemId);
  let result = null;

  if (id.startsWith('send:')) {
    const device = state.devices.find((item) => item.id === id.slice(5));
    if (device) result = await sendFromMenu(info, tab, device);
  } else if (id.startsWith('tv:')) {
    result = await playFromMenu(info, tab, id.slice(3));
  } else if (id === 'call' || id === 'text') {
    const number = numbers.find(info.selectionText);
    if (number) showNumber(tab.id, { number, startWithText: id === 'text' });
  }

  if (result) toast(tab, result);
});

async function sendFromMenu(info, tab, device) {
  const media = ['image', 'video', 'audio'].includes(info.mediaType) ? info.srcUrl : null;
  let item;

  if (media && /^https?:/.test(media)) item = { fetch: media, name: page.fileName(media, info.mediaType) };
  else if (info.linkUrl && /^https?:/.test(info.linkUrl)) item = { link: info.linkUrl };
  else if (info.selectionText) item = { text: info.selectionText };
  else return sendTab(tab, device);

  const ok = await share(device, item);
  const what = item.fetch ? `${info.mediaType === 'image' ? 'Picture' : 'File'} sent` : item.link ? 'Link opened' : 'Text sent';

  return { ok, text: ok ? `${what} on ${device.name}` : `We couldn't reach ${device.name}.` };
}

async function playFromMenu(info, tab, tvId) {
  const tv = state.tvs.find((item) => item.id === tvId);
  if (!tv) return { ok: false, text: 'That TV is no longer around.' };
  if (!page.isPlayableByTv(info.srcUrl)) return playTab(tab, tvId);

  const result = await bridge.request({ type: 'playOnTv', tv: tv.id, url: info.srcUrl, title: tab.title, mime: page.mimeFor(info.srcUrl, info.mediaType === 'audio') });
  return { ok: result.ok, text: result.ok ? `Playing on ${tv.name}` : `We couldn't reach ${tv.name}.` };
}

function showNumber(tabId, request) {
  const phones = talkers();
  if (phones.length > 0) message(tabId, { kind: 'number', phones, ...request }, { frameId: 0 });
}

// Messages from pages and the popup

api.runtime.onMessage.addListener((request, sender, reply) => {
  const answer = (promise) => {
    Promise.resolve(promise).then(reply, () => reply(null));
    return true;
  };

  switch (request.kind) {
    case 'phones':
      return answer(talkers());
    case 'media':
      tabMedia(sender, request.players);
      return false;
    case 'selection':
      selectedNumber = request.number;
      ['call', 'text'].forEach((id) => api.contextMenus.update(id, { visible: Boolean(request.number) }, () => void api.runtime.lastError));
      return false;
    case 'recentCode':
      return answer(freshCode());
    case 'codeUsed':
      lastCode = null;
      return false;
    case 'showNumber':
      showNumber(sender.tab.id, request);
      return false;
    case 'dial':
      return answer(
        bridge.request(
          request.action === 'call'
            ? { type: 'call', number: request.number, device: request.device }
            : { type: 'text', number: request.number, device: request.device, body: request.body },
        ),
      );
    case 'popup':
      return answer(popupState());
    case 'choose':
      return answer(api.storage.local.set({ device: request.device }));
    case 'openHere':
      return answer(onActiveTab((tab) => chooseById(request.device).then((device) => (device ? sendTab(tab, device) : null))));
    case 'playHere':
      return answer(onActiveTab((tab) => playTab(tab, request.target)));
    case 'sendDownload':
      return answer(chooseById(request.device).then((device) => (device ? sendDownload(request.id, device) : false)));
    case 'launch':
      bridge.post({ type: 'launch' });
      return false;
    case 'forgetCode':
      lastCode = null;
      return false;
  }

  return false;
});

async function chooseById(id) {
  return state.devices.find((device) => device.id === id) ?? chosenDevice();
}

async function onActiveTab(action) {
  const tab = await activeTab();
  return tab ? action(tab) : null;
}

async function popupState() {
  const tab = await activeTab();
  const video = tab ? await message(tab.id, { kind: 'video' }) : null;
  const downloads = await api.downloads.search({ state: 'complete', orderBy: ['-startTime'], limit: 8 });
  const device = await chosenDevice();

  return {
    ...state,
    device: device?.id ?? null,
    code: freshCode(),
    page: tab && /^https?:/.test(tab.url ?? '') ? { title: tab.title, host: new URL(tab.url).hostname, hasVideo: Boolean(video) } : null,
    downloads: downloads
      .filter((item) => item.filename && item.exists !== false)
      .slice(0, 3)
      .map((item) => ({ id: item.id, name: item.filename.split(/[\\/]/).pop(), size: item.fileSize ?? item.totalBytes ?? 0 })),
  };
}

function freshCode() {
  if (lastCode && Date.now() - lastCode.at > CODE_LIFETIME) lastCode = null;
  return lastCode;
}

// Media in tabs

function tabMedia(sender, list) {
  if (!sender.tab) return;

  const key = `${sender.tab.id}:${sender.frameId ?? 0}`;
  if (list?.length) players.set(key, list);
  else players.delete(key);

  clearTimeout(mediaTimer);
  mediaTimer = setTimeout(sendMedia, 250);
}

function sendMedia() {
  const all = [];
  for (const [key, list] of players) {
    for (const player of list) all.push({ ...player, id: `${key}:${player.key}` });
  }

  bridge.post({ type: 'media', players: settings.media ? all : [] });
}

function tabsWithMedia() {
  return [...new Set(Array.from(players.keys(), (key) => Number(key.split(':')[0])))];
}

api.tabs.onRemoved.addListener((tabId) => {
  for (const key of players.keys()) if (key.startsWith(`${tabId}:`)) players.delete(key);
  sendMedia();
});

// Downloads

api.downloads.onChanged.addListener(async (delta) => {
  if (delta.state?.current !== 'complete' || settings.downloads === 'off' || state.status !== 'ready') return;

  const device = await chosenDevice();
  if (!device) return;

  if (settings.downloads === 'always') {
    await sendDownload(delta.id, device);
    return;
  }

  const [item] = await api.downloads.search({ id: delta.id });
  const name = item?.filename?.split(/[\\/]/).pop() ?? 'your download';
  const notification = `download:${delta.id}`;
  askedDownloads.set(notification, { id: delta.id, device: device.id });

  const options = {
    type: 'basic',
    iconUrl: api.runtime.getURL('icons/icon-128.png'),
    title: `Send to ${device.name}?`,
    message: `${name} finished downloading. Click to send it to ${device.name}.`,
  };
  if (!/Firefox/.test(navigator.userAgent)) options.buttons = [{ title: `Send to ${device.name}` }];
  api.notifications.create(notification, options);
});

async function sendAskedDownload(notification) {
  const asked = askedDownloads.get(notification);
  if (!asked) return;

  askedDownloads.delete(notification);
  api.notifications.clear(notification);
  const device = state.devices.find((item) => item.id === asked.device) ?? (await chosenDevice());
  if (device) await sendDownload(asked.id, device);
}

api.notifications.onClicked.addListener(sendAskedDownload);
api.notifications.onButtonClicked?.addListener(sendAskedDownload);
api.notifications.onClosed.addListener((notification) => askedDownloads.delete(notification));

// Helpers

async function activeTab() {
  const [tab] = await api.tabs.query({ active: true, lastFocusedWindow: true });
  return tab ?? null;
}

/** Messages a tab's content script; null when the page has none, such as the browser's own pages. */
async function message(tabId, body, options) {
  try {
    return (await api.tabs.sendMessage(tabId, body, options)) ?? null;
  } catch {
    return null;
  }
}

function toast(tab, { ok, text }) {
  message(tab.id, { kind: 'toast', ok, text }, { frameId: 0 }).then((shown) => {
    if (shown !== null) return;

    api.notifications.create({ type: 'basic', iconUrl: api.runtime.getURL('icons/icon-128.png'), title: 'FlyyLink', message: text });
  });
}

/** A dot on the toolbar button while FlyyLink can't be reached, or someone needs to sign in. */
function updateBadge() {
  const trouble = state.status === 'missing' || state.status === 'closed' || state.status === 'locked';
  api.action.setBadgeText({ text: trouble ? '!' : '' });
  api.action.setBadgeBackgroundColor({ color: '#BB8C01' });
}
