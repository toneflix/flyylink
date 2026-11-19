/** Builds the FlyyLink Android screens as HTML, mirroring the Compose layouts in android/app. */
class AndroidApp {
  constructor(assets, { dark = false, time = '19:31', roomy = false } = {}) {
    this.assets = assets;
    this.dark = dark;
    this.time = time;
    this.roomy = roomy;
  }

  icon(name) {
    return `<span class="ic">${this.assets.icons[name]}</span>`;
  }

  screen(content, overlay = '') {
    return `<div class="app ${this.dark ? 'dark' : ''}">${this.statusBar()}${content}${overlay}</div>`;
  }

  statusBar() {
    const bars = [5, 7, 9, 12].map((h) => `<i style="height:${h}px"></i>`).join('');

    return `<div class="status"><span class="time">${this.time}</span><span class="icons">${this.icon('wifi')}<span class="signal">${bars}</span><span class="battery"></span></span></div>`;
  }

  /** The home column. A roomy (tablet) screen also shows the FlyyDrop card under the tiles, as the app always does. */
  home(sections) {
    const tiles = sections.findIndex((html) => html.includes('class="tiles"'));
    const hasCard = sections.some((html) => html.includes('class="flyy-card"'));

    if (this.roomy && tiles >= 0 && !hasCard) {
      sections = [...sections.slice(0, tiles + 1), this.flyyDropCard(), ...sections.slice(tiles + 1)];
    }

    return `<div class="home">${this.header()}${sections.join('')}</div>`;
  }

  header() {
    return `<div class="header"><img src="${this.assets.mark}"><span class="name">FlyyLink</span>${this.icon('settings')}</div>`;
  }

  laptop(width, menu = false) {
    const pad = width * 0.028;

    return `<div class="laptop">
      <div class="lid" style="width:${width}px;height:${width * 0.63}px;padding:${pad}px;border-radius:${width * 0.06}px">
        <div class="glass" style="border-radius:${width * 0.035}px">${menu ? '<div class="menu"></div>' : ''}</div>
      </div>
      <div class="base" style="width:${width * 1.17}px;height:${width * 0.055}px;border-radius:0 0 ${width * 0.05}px ${width * 0.05}px"></div>
    </div>`;
  }

  hero(name, text, extraClass = '') {
    return `<div class="hero ${extraClass}">
      <div class="row"><span class="pill"><span class="dot"></span>Connected</span>${this.icon('wifi')}</div>
      <div class="laptop">${this.laptop(180, true)}</div>
      <div><div class="title">${name}</div><div class="text">${text}</div></div>
    </div>`;
  }

  carousel(names) {
    const text = 'Syncing with all your devices';

    return `<div class="carousel">${this.hero(names[0], text)}${this.hero(names[1], text, 'peek')}</div>`;
  }

  tiles() {
    const tiles = [['file-up', 'Send files'], ['clipboard', 'Clipboard'], ['link', 'Open link'], ['cast', 'Mirror']];

    return `<div class="tiles">${tiles.map(([icon, label]) => `<div class="tile"><span class="chip">${this.icon(icon)}</span><span class="label">${label}</span></div>`).join('')}</div>`;
  }

  setupCard(icon, title, hint) {
    return `<div class="setup"><span class="chip">${this.icon(icon)}</span><div class="body"><div class="title">${title}</div><div class="hint">${hint}</div></div><span class="button">Allow</span></div>`;
  }

  recent(items) {
    const rows = items.map((item) => {
      const progress = item.progress != null;
      const bar = progress ? `<div class="progress"><i style="width:${item.progress}%"></i></div>` : '';
      const trailing = progress ? `<span class="percent">${item.progress}%</span>` : `<span class="when">${item.when}</span>`;

      return `<div class="recent"><span class="chip">${this.icon(item.icon)}</span><div class="body"><div class="title">${item.title}</div>${bar}<div class="detail">${item.detail}</div></div>${trailing}</div>`;
    });

    return `<div><div class="section"><span>Recent</span><span class="action">Clear</span></div><div class="list">${rows.join('')}</div></div>`;
  }

  footnote(phone) {
    return `<div class="footnote">This phone: ${phone}</div>`;
  }

  sheet(content) {
    return `<div class="scrim"></div><div class="sheet"><div class="handle"></div>${content}</div>`;
  }

  shareSheet({ title, size, targets }) {
    const rows = targets.map((target) => {
      if (target.progress == null) {
        return `<div class="target">${this.laptop(44)}<div class="body"><div class="row"><span class="name">${target.name}</span></div><div class="detail">Tap to send</div></div></div>`;
      }

      return `<div class="target active">${this.laptop(44)}<div class="body">
        <div class="row"><span class="name">${target.name}</span><span class="percent">${target.progress}%</span></div>
        <div class="progress"><i style="width:${target.progress}%"></i></div>
        <div class="detail">${target.detail}</div>
      </div></div>`;
    });

    return this.sheet(`<div class="share">
      <div class="summary"><span class="chip">${this.icon('image')}</span><div><div class="title">${title}</div><div class="sub">${size}</div></div></div>
      <div class="section" style="padding:0"><span>Send to</span>${targets.some((t) => t.progress == null) ? '<span class="action">Send to all</span>' : ''}</div>
      ${rows.join('')}
      <div class="outlined">Hide</div>
    </div>`);
  }

  pairSheet(name, code) {
    const half = Math.ceil(code.length / 2);
    const digits = [...code].map((c, i) => `${i === half ? '<span class="gap"></span>' : ''}<b>${c}</b>`).join('');

    return this.sheet(`<div class="pair">
      ${this.laptop(150)}
      <div><div class="title">Pair with ${name}?</div><div class="hint">Do you see the same code on both screens?</div></div>
      <div class="digits">${digits}</div>
      <div class="buttons"><div class="filled">Pair</div><div class="outlined">Not now</div></div>
    </div>`);
  }

  phoneArt(height) {
    const width = height * 0.52;

    return `<div class="phone-art" style="width:${width}px;height:${height}px;border-radius:${width * 0.2}px;padding:${width * 0.07}px"><div class="glass" style="border-radius:${width * 0.14}px"></div></div>`;
  }

  flyyDropCard(selected = 1) {
    const modes = ['Paired only', '10 minutes', 'Everyone'];
    const segments = modes.map((mode, i) => `<span class="${i === selected ? 'on' : ''}">${mode}</span>`).join('');

    return `<div class="flyy-card">
      <div class="row"><span class="chip">${this.icon('radar')}</span><div><div class="title">Who can FlyyDrop to you</div><div class="hint">9 min left</div></div></div>
      <div class="segments">${segments}</div>
    </div>`;
  }

  flyyDropSheet(name, { title, detail }) {
    return this.sheet(`<div class="flyy">
      <div class="radar"><i></i><i></i><span class="core">${this.phoneArt(52)}</span></div>
      <div><div class="title">${name} wants to FlyyDrop</div><div class="hint">Not paired</div></div>
      <div class="offer"><span class="chip">${this.icon('image')}</span><div><div class="name">${title}</div><div class="detail">${detail}</div></div></div>
      <div class="buttons"><div class="outlined">Decline</div><div class="filled">Accept</div></div>
    </div>`);
  }

  /** The TV remote from ui/Remote.kt: what's playing, the D-pad, Back, Home and Options, volume and typing. */
  remote(name, { title, artist }) {
    const arrows = [['chevron-up', 'up'], ['chevron-down', 'down'], ['chevron-left', 'left'], ['chevron-right', 'right']]
      .map(([icon, side]) => `<span class="arrow ${side}">${this.icon(icon)}</span>`).join('');
    const key = (icon, label, solid = false) => `<div class="key"><span class="round ${solid ? 'solid' : ''}">${this.icon(icon)}</span><span>${label}</span></div>`;

    return `<div class="remote">
      <div class="top">${this.icon('x')}<div><div class="name">${name}</div><div class="state"><span class="dot"></span>Connected</div></div></div>
      <div class="playing"><span class="art">${this.icon('music')}</span><div class="body"><div class="title">${title}</div><div class="artist">${artist}</div></div>${this.icon('skip-back')}${this.icon('pause')}${this.icon('skip-forward')}</div>
      <div class="modes"><span class="on">Buttons</span><span>Touchpad</span></div>
      <div class="dpad">${arrows}<span class="ok">OK</span></div>
      <div class="keys">${key('arrow-left', 'Back')}${key('house', 'Home')}${key('ellipsis', 'Options')}</div>
      <div class="keys"><div class="key"><span class="volume">${this.icon('volume-1')}${this.icon('volume-x')}${this.icon('volume-2')}</span><span>Volume</span></div>${key('play', 'Play', true)}</div>
      <div class="type">${this.icon('keyboard')}<span>Type on TV</span>${this.icon('corner-down-left')}</div>
    </div>`;
  }

  toast(text) {
    return `<div class="toast"><img src="${this.assets.appIcon}">${text}</div>`;
  }
}
