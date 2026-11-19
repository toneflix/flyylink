/** A phone frame holding one app screen, scaled from dp to canvas pixels. */
class Phone {
  /** A plain frame has no camera, so it doesn't look like a particular brand's phone. */
  constructor(screenWidth, screenHeight, dpWidth = 412, plain = false) {
    this.screenWidth = screenWidth;
    this.screenHeight = screenHeight;
    this.dpWidth = dpWidth;
    this.plain = plain;
  }

  get scale() {
    return this.screenWidth / this.dpWidth;
  }

  get frameClass() {
    return 'phone';
  }

  render(appHtml) {
    const height = this.screenHeight / this.scale;
    const app = appHtml.replace('class="app', `style="width:${this.dpWidth}px;height:${height}px;transform:scale(${this.scale})" class="app`);

    const frame = this.screenWidth / 800;

    return `<div class="${this.frameClass}" style="--k:${frame}">${this.bezel()}<div class="screen" style="width:${this.screenWidth}px;height:${this.screenHeight}px">${this.punchHole()}${app}</div></div>`;
  }

  bezel() {
    return '';
  }

  punchHole() {
    return this.plain ? '' : '<div class="camera"></div>';
  }
}

/** A tablet frame: wider bezels with the camera in the top one. */
class Tablet extends Phone {
  get frameClass() {
    return 'phone tablet';
  }

  bezel() {
    return this.plain ? '' : '<div class="camera"></div>';
  }

  punchHole() {
    return '';
  }
}

/**
 * One 1080x1920 store screenshot: logo, two-line headline, and the app in a phone.
 * The Amazon Appstore copy uses `amazonLines` where given, since Amazon rejects Google marks such as "Android".
 */
class StoreShot {
  constructor({ id, lines, amazonLines = lines, dark = false, screen, extra = () => '' }, store = 'play') {
    this.plain = store === 'amazon';
    this.id = this.plain ? `amazon-phone-${id}` : id;
    this.lines = store === 'amazon' ? amazonLines : lines;
    this.dark = dark;
    this.screen = screen;
    this.extra = extra;
    this.width = 1080;
    this.height = 1920;
    this.scale = 1;
  }

  app(assets) {
    return new AndroidApp(assets, { dark: this.dark, plain: this.plain });
  }

  device() {
    return new Phone(800, 1456, 412, this.plain);
  }

  render(assets) {
    const app = this.app(assets);

    return `<div class="canvas shot" style="width:${this.width}px;height:${this.height}px">
      <img class="mark" src="${assets.mark}">
      <div class="headline">${this.lines[0]}<br><span class="accent">${this.lines[1]}</span></div>
      <div class="device">${this.device().render(this.screen(app))}</div>
      ${this.extra(assets)}
    </div>`;
  }
}

/**
 * The same store screenshot on a 7-inch (600 dp) or 10-inch (800 dp) tablet.
 * Play gets 1440x2560 and 2160x3840; the Amazon Appstore gets 1200x1920 and 1600x2560.
 */
class TabletShot extends StoreShot {
  static FORMATS = {
    'tablet-7': { dpWidth: 600, width: 1080, scale: 4 / 3, store: 'play' },
    'tablet-10': { dpWidth: 800, width: 1080, scale: 2, store: 'play' },
    'amazon-tablet-7': { dpWidth: 600, width: 1200, scale: 1, store: 'amazon' },
    'amazon-tablet-10': { dpWidth: 800, width: 1200, scale: 4 / 3, store: 'amazon' },
  };

  constructor(spec, format) {
    super(spec, TabletShot.FORMATS[format].store);
    this.id = `${format}-${spec.id}`;
    this.dpWidth = TabletShot.FORMATS[format].dpWidth;
    this.width = TabletShot.FORMATS[format].width;
    this.scale = TabletShot.FORMATS[format].scale;
  }

  app(assets) {
    return new AndroidApp(assets, { dark: this.dark, roomy: true, plain: this.plain });
  }

  device() {
    return new Tablet(900, 1440, this.dpWidth, this.plain);
  }
}

/** The 1024x500 feature graphic: Play's feature graphic, or the Amazon Appstore promo image without Google marks. */
class FeatureGraphic {
  constructor(id = 'feature-graphic', title = 'Your Android phone,', plain = false) {
    this.id = id;
    this.title = title;
    this.plain = plain;
  }

  render(assets) {
    const app = new AndroidApp(assets, { plain: this.plain });
    const phone = new Phone(232, 520, 412, this.plain);
    const screen = app.screen(app.home([app.hero('MacBook Pro', 'Syncing'), app.tiles(), app.flyyDropCard()]));

    return `<div class="canvas feature">
      <div class="glow"></div>
      <div class="laptop-wrap app" style="width:auto;height:auto;background:none">${app.laptop(270, true)}</div>
      <div class="device">${phone.render(screen)}</div>
      <div class="copy">
        <div class="brand"><img src="${assets.mark}"><span>FlyyLink</span></div>
        <div class="headline">${this.title}<br><span class="accent">on your Mac and PC</span></div>
        <div class="sub">Texts, calls, notifications and files.</div>
      </div>
    </div>`;
  }
}

const FLOWING = 'Syncing';

/** The listing screenshots, in order. Each is drawn on a phone and on both tablet sizes. */
const SCREENS = [
  {
    id: '01-home',
    lines: ['Android meets', 'your Mac and PC'],
    amazonLines: ['Your phone meets', 'your Mac and PC'],
    screen: (app) => app.screen(app.home([
      app.hero('MacBook Pro', FLOWING),
      app.tiles(),
      app.recent([
        { icon: 'file-text', title: 'Invoice-March.pdf', progress: 72, detail: '3.1 MB of 4.3 MB · 38 MB/s · about 1 second left' },
        { icon: 'image', title: 'IMG_2041.jpg', detail: 'From MacBook Pro · Saved to Downloads', when: '2 min. ago' },
        { icon: 'link', title: 'Link from MacBook Pro', detail: 'From MacBook Pro · Tap to open', when: '5 min. ago' },
      ]),
    ])),
  },
  {
    id: '02-flyydrop',
    lines: ['FlyyDrop to', 'anyone nearby'],
    screen: (app) => app.screen(
      app.home([app.hero('MacBook Pro', FLOWING), app.tiles(), app.flyyDropCard()]),
      app.flyyDropSheet("Ada's phone", { title: '3 photos · 12.4 MB', detail: 'IMG_2207.jpg, IMG_2208.jpg, IMG_2210.jpg' }),
    ),
  },
  {
    id: '03-send-files',
    lines: ['Send files', 'in seconds'],
    screen: (app) => app.screen(
      app.home([app.hero('MacBook Pro', FLOWING), app.tiles()]),
      app.shareSheet({
        title: '12 photos',
        size: '48.6 MB',
        targets: [
          { name: 'MacBook Pro', progress: 64, detail: '8 of 12 · 41 MB/s · about 1 second left' },
          { name: 'Work PC' },
        ],
      }),
    ),
  },
  {
    id: '04-notifications',
    lines: ['Never miss a text', 'while you work'],
    dark: true,
    screen: (app) => app.screen(app.home([
      app.hero('MacBook Pro', FLOWING),
      app.tiles(),
      app.recent([
        { icon: 'image', title: 'Boarding-pass.png', detail: 'From MacBook Pro · Saved to Downloads', when: '12 min. ago' },
        { icon: 'clipboard', title: 'Copied from MacBook Pro', detail: 'Meeting moved to 3pm, same room', when: '20 min. ago' },
      ]),
    ])),
    extra: (assets) => `<div class="banner" style="top:600px;left:390px">
      <span class="icon"><img src="${assets.appIcon}"></span>
      <div class="body"><div class="top"><span>MESSAGES</span><span>now</span></div><div class="title">Mum</div><div class="text">On my way home</div></div>
      <span class="reply">Reply</span>
    </div>`,
  },
  {
    id: '05-clipboard',
    lines: ['Copy here,', 'paste there'],
    screen: (app) => app.screen(
      app.home([
        app.hero('Work PC', FLOWING),
        app.tiles(),
        app.recent([
          { icon: 'clipboard', title: 'Copied from Work PC', detail: 'Wi-Fi password: blue-lagoon-42', when: '1 min. ago' },
        ]),
      ]),
      app.toast('Clipboard sent'),
    ),
  },
  {
    id: '06-many-computers',
    lines: ['One phone,', 'all your computers'],
    dark: true,
    screen: (app) => app.screen(app.home([
      app.carousel(['MacBook Pro', 'Work PC']),
      app.tiles(),
      app.recent([
        { icon: 'file-text', title: 'Q3-report.xlsx', detail: 'From Work PC · Saved to Downloads', when: '3 min. ago' },
        { icon: 'image', title: 'Screenshot 2026-11-10.png', detail: 'From MacBook Pro · Saved to Downloads', when: '8 min. ago' },
      ]),
    ])),
  },
  {
    id: '07-pair',
    lines: ['Pair once,', 'stay connected'],
    screen: (app) => app.screen(
      app.home([app.hero('Work PC', FLOWING), app.tiles()]),
      app.pairSheet('Work PC', '4F7A2C91'),
    ),
  },
  {
    id: '08-tv-remote',
    lines: ['Your phone is', 'the TV remote'],
    screen: (app) => app.screen(app.remote('Living room TV', { title: 'Midnight City', artist: 'M83' })),
  },
];

const SHOTS = SCREENS.map((spec) => new StoreShot(spec));
const AMAZON_SHOTS = SCREENS.map((spec) => new StoreShot(spec, 'amazon'));
const TABLET_SHOTS = Object.keys(TabletShot.FORMATS).flatMap((format) => SCREENS.map((spec) => new TabletShot(spec, format)));

const GRAPHICS = [
  new FeatureGraphic(),
  new FeatureGraphic('amazon-promo', 'Your phone,', true),
  ...SHOTS,
  ...AMAZON_SHOTS,
  ...TABLET_SHOTS,
];
