/** Builds the FlyyLink TV screens as HTML, mirroring the Compose layouts in android/app/.../ui/Tv.kt. */
class TvApp {
  constructor(assets) {
    this.assets = assets;
  }

  icon(name) {
    return `<span class="ic">${this.assets.icons[name]}</span>`;
  }

  /** The 960x540 dp TV screen, scaled to the canvas. */
  screen(content, overlay = '') {
    return `<div class="tv-app">${content}${overlay}</div>`;
  }

  tvArt(width, inner = '') {
    return `<div class="tv-art">
      <div class="frame" style="width:${width}px;height:${width * 0.58}px;padding:${width * 0.022}px;border-radius:${width * 0.04}px">
        <div class="glass" style="border-radius:${width * 0.025}px">${inner}</div>
      </div>
      <div class="neck" style="width:${width * 0.08}px;height:${width * 0.05}px"></div>
      <div class="foot" style="width:${width * 0.36}px;height:${width * 0.025}px"></div>
    </div>`;
  }

  deviceArt(type, width) {
    if (type === 'phone') {
      const height = width * 0.75;
      return `<div class="phone-art" style="width:${height * 0.5}px;height:${height}px;border-radius:${height * 0.1}px;padding:${height * 0.035}px"><div class="glass" style="border-radius:${height * 0.075}px"></div></div>`;
    }
    const pad = width * 0.028;
    return `<div class="laptop">
      <div class="lid" style="width:${width}px;height:${width * 0.63}px;padding:${pad}px;border-radius:${width * 0.06}px"><div class="glass" style="border-radius:${width * 0.035}px"></div></div>
      <div class="base" style="width:${width * 1.17}px;height:${width * 0.055}px;border-radius:0 0 ${width * 0.05}px ${width * 0.05}px"></div>
    </div>`;
  }

  home({ name, connected, devices, received, focus }) {
    const status = connected.length ? `Connected to ${connected.join(', ')}` : 'Ready';
    const dot = connected.length ? 'ok' : 'gold';

    const deviceCards = devices.map((d, i) => this.card(`dev-${i}`, focus, 'device', `
      <div class="art">${this.deviceArt(d.type, 92)}</div>
      <div class="name">${d.name}</div>
      <div class="status">${d.paired ? '<span class="dot ok"></span>' : ''}${d.status}</div>`));

    const receivedCards = received.map((r, i) => this.card(`rec-${i}`, focus, 'received', `
      <div class="thumb ${r.picture ? 'picture ' + r.picture : ''}">${r.picture ? '' : this.icon(r.icon)}</div>
      <div class="body"><div class="name">${r.title}</div><div class="detail">${r.detail}</div></div>`));

    const settings = [
      ['tv', 'Remote', 'On', true],
      ['bell', 'Notification pop-ups', 'On', true],
      ['download', 'Open right away', 'On', true],
      ['music', "Show what's playing", 'Turn on', false],
    ].map(([icon, title, value, on], i) => this.card(`set-${i}`, focus, 'setting', `
      <span class="chip ${on ? 'on' : ''}">${this.icon(icon)}</span>
      <div><div class="name">${title}</div><div class="status ${on ? 'ok' : ''}">${value}</div></div>`));

    return `<div class="tv-home">
      <div class="intro">
        <div class="brand"><img src="${this.assets.mark}"><span>FLYYLINK</span></div>
        <div class="hero-art">${this.tvArt(220, `<div class="tv-glow"><img src="${this.assets.mark}"></div>`)}</div>
        <div class="tv-name">${name}</div>
        <div class="tv-status"><span class="dot ${dot}"></span>${status}</div>
      </div>
      <div class="rows">
        ${devices.length ? `<div class="row"><div class="title">Devices</div><div class="cards">${deviceCards.join('')}</div></div>` : ''}
        ${received.length ? `<div class="row"><div class="title">Received</div><div class="cards">${receivedCards.join('')}</div></div>` : ''}
        <div class="row"><div class="title">Settings</div><div class="cards">${settings.join('')}</div></div>
      </div>
    </div>`;
  }

  card(id, focus, kind, inner) {
    return `<div class="card ${kind} ${id === focus ? 'focused' : ''}">${inner}</div>`;
  }

  dialog(inner) {
    return `<div class="tv-scrim"></div><div class="tv-dialog">${inner}</div>`;
  }

  pairDialog(name, code) {
    const half = Math.ceil(code.length / 2);
    const digits = [...code].map((c, i) => `${i === half ? '<span class="gap"></span>' : ''}<b>${c}</b>`).join('');

    return this.dialog(`${this.deviceArt('phone', 120)}
      <div class="title">Pair with ${name}?</div>
      <div class="hint">Check the code matches</div>
      <div class="digits">${digits}</div>
      <div class="buttons"><span class="button">Not now</span><span class="button focused">Pair</span></div>`);
  }

  popup(label, title, text) {
    return `<div class="tv-popup"><img src="${this.assets.appIcon}"><div><div class="label">${label}</div><div class="title">${title}</div><div class="text">${text}</div></div></div>`;
  }

  /** A received photo, full screen, with who sent it. */
  viewer(picture, title, detail) {
    return `<div class="tv-viewer picture ${picture}"><div class="caption"><div class="title">${title}</div><div class="detail">${detail}</div></div></div>`;
  }

  /** Something else playing on the TV, behind a popup. */
  film() {
    return `<div class="tv-film"><div class="sun"></div><div class="hills"></div><div class="bar"><i style="width:38%"></i></div></div>`;
  }
}

/** A 1920x1080 Play Store TV screenshot: the TV screen, full bleed. */
class TvShot {
  constructor({ id, screen }) {
    this.id = id;
    this.screen = screen;
    this.width = 1920;
    this.height = 1080;
  }

  render(assets) {
    return `<div class="canvas tv-shot"><div class="tv-scale">${this.screen(new TvApp(assets))}</div></div>`;
  }
}

/** The TV banner: the Play Store one at 1280x720, and the launcher one inside the app at 320x180. */
class TvBanner {
  constructor(id, width) {
    this.id = id;
    this.width = width;
    this.height = width * 9 / 16;
  }

  render(assets) {
    return `<div class="canvas tv-banner" style="width:${this.width}px;height:${this.height}px"><div class="tv-banner-scale" style="transform:scale(${this.width / 1280})">
      <div class="glow"></div>
      <img src="${assets.mark}"><span>FlyyLink</span>
    </div></div>`;
  }
}

const TV_HOME = {
  name: 'Living Room TV',
  connected: ["Ada's phone", 'MacBook Pro'],
  devices: [
    { type: 'phone', name: "Ada's phone", status: 'Connected', paired: true },
    { type: 'laptop', name: 'MacBook Pro', status: 'Connected', paired: true },
  ],
  received: [
    { picture: 'sunset', title: 'IMG_2207.jpg', detail: "From Ada's phone · Saved to Downloads" },
    { icon: 'film', title: 'Birthday.mp4', detail: 'From MacBook Pro · Saved to Downloads' },
    { icon: 'link', title: 'flyylink.toneflix.net', detail: "Link from Ada's phone" },
  ],
};

const TV_GRAPHICS = [
  new TvBanner('tv-banner', 1280),
  new TvBanner('tv-banner-app', 320),
  new TvShot({ id: 'tv-01-home', screen: (tv) => tv.screen(tv.home({ ...TV_HOME, focus: 'rec-0' })) }),
  new TvShot({ id: 'tv-02-photo', screen: (tv) => tv.screen(tv.viewer('sunset', 'IMG_2207.jpg', "From Ada's phone · Saved to Downloads")) }),
  new TvShot({ id: 'tv-03-notifications', screen: (tv) => tv.screen(tv.film(), tv.popup("Messages · Ada's phone", 'Mum', 'On my way home')) }),
  new TvShot({
    id: 'tv-04-pair',
    screen: (tv) => tv.screen(
      tv.home({ name: 'Living Room TV', connected: [], devices: [{ type: 'phone', name: "Ada's phone", status: 'Wants to pair' }], received: [] }),
      tv.pairDialog("Ada's phone", '4F7A2C91'),
    ),
  }),
];
