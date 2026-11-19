const PHONE = "Ada's phone";

/** One promo message: its headline, a line under it, and the scene of devices drawn on a 1000x720 stage. */
class Theme {
  static STAGE = { width: 1000, height: 680 };

  constructor({ id, lines, sub, scene }) {
    this.id = id;
    this.lines = lines;
    this.sub = sub;
    this.scene = scene;
  }

  stage(assets) {
    const parts = {
      app: (options = {}) => new AndroidApp(assets, { plain: true, ...options }),
      pc: new Computer(assets),
      phone: (width, height, html) => new Phone(width, height, 412, true).render(html),
      tv: new TvApp(assets),
      assets,
    };

    return `<div class="stage" style="width:${Theme.STAGE.width}px;height:${Theme.STAGE.height}px">${this.scene(parts)}</div>`;
  }
}

/**
 * A canvas size for one place the graphics go. The copy sits above the stage, or beside it on wide banners,
 * and `fit()` scales the stage into whatever room the copy leaves.
 */
class Format {
  constructor(name, width, height, { wide = false, safe = 0, inset = 0, text = 1, bleed = 1 } = {}) {
    this.name = name;
    this.bleed = bleed;
    this.text = text;
    this.width = width;
    this.height = height;
    this.wide = wide;
    this.safe = safe;
    this.inset = inset;
  }

  render(theme, assets) {
    const unit = this.wide ? this.height / 100 : this.width / 100;
    const padding = this.wide
      ? `${unit * 8}px ${unit * 6}px ${unit * 8}px ${this.inset + unit * 12}px`
      : `${this.safe + unit * 6}px ${unit * 5}px ${this.safe + unit * 12}px`;
    const url = `<div class="url">flyylink.toneflix.net</div>`;

    return `<div class="canvas promo ${this.wide ? 'wide' : ''}" style="width:${this.width}px;height:${this.height}px;padding:${padding};--u:${unit}px;--t:${this.text};--safe:${this.safe}px" data-bleed="${this.bleed}">
      <div class="glow"></div>
      <div class="copy">
        <div class="brand"><img src="${assets.mark}"><span>FlyyLink</span></div>
        <div class="headline">${theme.lines[0]}<br><span class="accent">${theme.lines[1]}</span></div>
        <div class="sub">${theme.sub}</div>
        ${this.wide ? url : ''}
      </div>
      <div class="stage-area">${theme.stage(assets)}</div>
      ${this.wide ? '' : url}
    </div>`;
  }
}

/** Scales each stage to fill the room the copy leaves (past the left edge on bleeding formats, keeping the phone on the right in view), then shrinks its area so the canvas can centre copy and stage together. */
function fit() {
  for (const area of document.querySelectorAll('.stage-area')) {
    const stage = area.querySelector('.stage');
    const bleed = Number(area.closest('.canvas').dataset.bleed);
    const scale = Math.min(area.clientWidth * bleed / Theme.STAGE.width, area.clientHeight / Theme.STAGE.height);
    const left = bleed > 1 ? area.clientWidth - Theme.STAGE.width * scale : (area.clientWidth - Theme.STAGE.width * scale) / 2;
    const top = (area.clientHeight - Theme.STAGE.height * scale) / 2;

    if (area.closest('.wide')) {
      stage.style.transform = `translate(${left}px, ${top}px) scale(${scale})`;
      continue;
    }

    stage.style.transform = `translate(${left}px, 0) scale(${scale})`;
    area.style.flex = 'none';
    area.style.height = `${Theme.STAGE.height * scale}px`;
  }
}

/** One image to render: a theme in a format. */
class PromoGraphic {
  constructor(theme, format) {
    this.theme = theme;
    this.format = format;
    this.id = `${theme.id}-${format.name}`;
    this.width = format.width;
    this.height = format.height;
  }

  render(assets) {
    return this.format.render(this.theme, assets);
  }
}

const FORMATS = {
  square: new Format('square', 1080, 1080),
  portrait: new Format('portrait', 1080, 1350),
  story: new Format('story', 1080, 1920, { safe: 200, text: 1.15, bleed: 1.4 }),
  link: new Format('link', 1200, 630, { wide: true, text: 0.85 }),
  x: new Format('x-header', 1500, 500, { wide: true, inset: 260 }),
  linkedin: new Format('linkedin-cover', 1584, 396, { wide: true, inset: 300 }),
};

const MUM = {
  threads: [['Mum', 'On my way home', 28], ['Chidi Okeke', 'Sent the files', 205], ['Ada Okafor', 'Call me when you are free', 290], ['Tunde', 'See you at 6', 150]],
  open: 0,
  bubbles: [['in', 'Are you coming for dinner on Sunday?'], ['out', "Yes, I'll bring dessert this time"], ['in', 'On my way home']],
  draft: 'Drive safe, see you soon',
};

const homeScreen = (app, computer = 'MacBook Pro') => app.screen(app.home([app.hero(computer, 'Syncing'), app.tiles(), app.flyyDropCard()]));

const THEMES = [
  new Theme({
    id: 'hero',
    lines: ['Your phone and computer,', 'finally on speaking terms.'],
    sub: 'See your texts, calls and notifications on your Mac or PC, and send files both ways.',
    scene: ({ app, pc, phone }) => `
      <div class="at" style="left:0;top:40px">${pc.laptop(780, pc.desktop([
        pc.messages(PHONE, MUM),
        pc.notification(`Messages · ${PHONE}`, 'Chidi Okeke', 'Sent the files, check your Downloads', { x: 520, y: 30 }),
      ]))}</div>
      <div class="at" style="right:10px;top:70px">${phone(270, 560, homeScreen(app()))}</div>`,
  }),
  new Theme({
    id: 'texts',
    lines: ['Reply to your texts', 'from your computer.'],
    sub: 'Read and answer messages with a real keyboard, without picking up your phone.',
    scene: ({ pc }) => `
      <div class="at" style="left:30px;top:0">${pc.laptop(940, pc.desktop([
        pc.messages(PHONE, MUM, { x: 110, y: 60, width: 1060, height: 680 }),
      ]))}</div>
      <div class="at pop" style="left:460px;top:400px;transform:scale(1.15);transform-origin:left top">${pc.notification(`Messages · ${PHONE}`, 'Mum', 'On my way home', { x: 0, y: 0 })}</div>`,
  }),
  new Theme({
    id: 'calls',
    lines: ['Answer calls without', 'reaching for your phone.'],
    sub: "You'll see who's calling right on your Mac or PC, wherever your phone is.",
    scene: ({ app, pc, phone }) => `
      <div class="at" style="left:0;top:40px">${pc.laptop(780, pc.desktop([
        pc.messages(PHONE, { ...MUM, open: 2, bubbles: [['in', 'Call me when you are free'], ['out', 'Calling you in a minute']], draft: '' }),
      ]))}</div>
      <div class="at pop" style="left:110px;top:0;transform:scale(1.35);transform-origin:left top">${pc.call('Ada Okafor', PHONE, 290, { x: 0, y: 0 })}</div>
      <div class="at" style="right:10px;top:110px">${phone(250, 520, homeScreen(app({ dark: true })))}</div>`,
  }),
  new Theme({
    id: 'files',
    lines: ['Send files in seconds,', 'both ways.'],
    sub: 'Share photos, videos and documents of any size between your phone and computer.',
    scene: ({ app, pc, phone }) => {
      const a = app();
      const sheet = a.screen(a.home([a.hero('MacBook Pro', 'Syncing'), a.tiles()]), a.shareSheet({
        title: '12 photos',
        size: '48.6 MB',
        targets: [{ name: 'MacBook Pro', progress: 64, detail: '8 of 12 · 41 MB/s · about 1 second left' }, { name: 'Work PC' }],
      }));

      return `
        <div class="at" style="left:0;top:40px">${pc.laptop(780, pc.desktop([
          pc.folder('Downloads', 6, { x: 60, y: 60, width: 760, height: 520 }),
          pc.transfers([
            { icon: 'image', title: '12 photos from Ada\'s phone', detail: '8 of 12 · 41 MB/s', progress: 64 },
            { icon: 'file-text', title: 'Invoice-March.pdf', detail: 'Saved to Downloads' },
            { icon: 'film', title: 'Birthday.mp4', detail: 'Saved to Downloads' },
          ], { x: 640, y: 300, width: 580, height: 420 }),
        ]))}</div>
        <div class="at" style="right:10px;top:70px">${phone(270, 560, sheet)}</div>`;
    },
  }),
  new Theme({
    id: 'mirror',
    lines: ['Use your phone', 'from your computer.'],
    sub: "See your phone's screen in a window and use it with your mouse and keyboard.",
    scene: ({ app, pc }) => `
      <div class="at" style="left:30px;top:0">${pc.laptop(940, pc.desktop([
        pc.messages(PHONE, MUM, { x: 60, y: 90, width: 760, height: 560 }),
        pc.mirror(PHONE, homeScreen(app()), { x: 860, y: 30, width: 360, height: 740 }),
      ]))}</div>`,
  }),
  new Theme({
    id: 'tv',
    lines: ['Your phone is', 'the TV remote.'],
    sub: 'Control your smart TV from your phone, and show your photos and videos on the big screen.',
    scene: ({ app, pc, phone, tv }) => `
      <div class="at" style="left:0;top:20px">${pc.tv(800, tv.screen(tv.viewer('sunset', 'IMG_2207.jpg', `From ${PHONE}`)))}</div>
      <div class="at" style="right:20px;top:150px">${phone(260, 540, app().screen(app().remote('Living room TV', { title: 'Midnight City', artist: 'M83' })))}</div>`,
  }),
  new Theme({
    id: 'private',
    lines: ['Your devices talk', 'to each other directly.'],
    sub: 'Everything between them is encrypted, and nothing goes through our servers.',
    scene: ({ app, pc, phone }) => `
      <div class="at" style="left:0;top:120px">${pc.laptop(640, pc.desktop([pc.messages(PHONE, MUM)]))}</div>
      <div class="link-line" style="left:600px;top:360px;width:200px"></div>
      <div class="at lock" style="left:650px;top:310px">${pc.icon('lock')}</div>
      <div class="at" style="right:20px;top:90px">${phone(250, 520, homeScreen(app()))}</div>`,
  }),
  new Theme({
    id: 'get',
    lines: ['Free on your phone.', 'Try it free on your computer.'],
    sub: 'FlyyLink is free on your phone and TV, and you can try it free on your Mac or PC.',
    scene: ({ app, pc, phone, tv }) => `
      <div class="at" style="left:250px;top:0">${pc.tv(560, tv.screen(tv.home({ ...TV_HOME, name: 'Living room TV', focus: 'rec-0' })))}</div>
      <div class="at" style="left:0;top:250px">${pc.laptop(640, pc.desktop([pc.messages(PHONE, MUM), pc.call('Ada Okafor', PHONE, 290, { x: 640, y: 30 })]))}</div>
      <div class="at" style="right:30px;top:180px">${phone(240, 500, homeScreen(app()))}</div>`,
  }),
];

const PROMO = [
  ...THEMES.flatMap((theme) => [new PromoGraphic(theme, FORMATS.square), new PromoGraphic(theme, FORMATS.story)]),
  ...[FORMATS.portrait, FORMATS.link, FORMATS.x, FORMATS.linkedin].map((format) => new PromoGraphic(THEMES[0], format)),
];
