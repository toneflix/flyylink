/** The computer side of the promo graphics: a laptop and a TV holding FlyyLink windows, drawn so they read as any brand's. */
class Computer {
  /** The desktop is laid out at 1280x800 and scaled into the laptop's screen. */
  static DESKTOP = { width: 1280, height: 800 };

  constructor(assets) {
    this.assets = assets;
  }

  icon(name) {
    return `<span class="ic">${this.assets.icons[name]}</span>`;
  }

  laptop(width, desktop) {
    const screen = width * 0.94;
    const scale = screen / Computer.DESKTOP.width;

    return `<div class="pc-laptop" style="width:${width}px">
      <div class="lid" style="padding:${width * 0.03}px;border-radius:${width * 0.035}px">
        <div class="glass" style="width:${screen}px;height:${screen * 0.625}px;border-radius:${width * 0.012}px">
          <div class="desktop" style="transform:scale(${scale})">${desktop}</div>
        </div>
      </div>
      <div class="base" style="height:${width * 0.035}px"><i style="width:${width * 0.16}px"></i></div>
    </div>`;
  }

  /** A TV on a stand, showing a 960x540 dp screen from the TV app. */
  tv(width, screen) {
    const inner = width * 0.97;

    return `<div class="pc-tv" style="width:${width}px">
      <div class="panel" style="padding:${width * 0.015}px;border-radius:${width * 0.012}px">
        <div class="glass" style="width:${inner}px;height:${inner * 0.5625}px"><div style="transform:scale(${inner / 960});transform-origin:top left">${screen}</div></div>
      </div>
      <div class="stand"><i style="width:${width * 0.04}px;height:${width * 0.04}px"></i><b style="width:${width * 0.3}px;height:${width * 0.014}px"></b></div>
    </div>`;
  }

  desktop(windows) {
    return `<div class="wallpaper"></div>${windows.join('')}`;
  }

  window(title, body, { x, y, width, height, icon = true }) {
    const badge = icon ? `<img src="${this.assets.appIcon}">` : '';

    return `<div class="win" style="left:${x}px;top:${y}px;width:${width}px;height:${height}px">
      <div class="bar">${badge}<span>${title}</span><i></i><i></i><i></i></div>
      <div class="content">${body}</div>
    </div>`;
  }

  avatar(initials, hue) {
    return `<span class="avatar" style="background:hsl(${hue} 70% 62%)">${initials}</span>`;
  }

  /** The Messages window: conversations on the left and the open one on the right. */
  messages(phone, { threads, open, bubbles, draft = '' }, frame = { x: 70, y: 70, width: 900, height: 600 }) {
    const list = threads.map(([name, text, hue], i) => `<div class="thread ${i === open ? 'on' : ''}">
      ${this.avatar(name.split(' ').map((w) => w[0]).join('').slice(0, 2), hue)}
      <div><div class="name">${name}</div><div class="text">${text}</div></div>
    </div>`).join('');
    const chat = bubbles.map(([side, text]) => `<div class="bubble ${side}">${text}</div>`).join('');
    const [name, , hue] = threads[open];
    const body = `<div class="messages">
      <div class="threads"><div class="search">${this.icon('search')}Search</div>${list}</div>
      <div class="chat">
        <div class="head">${this.avatar(name[0], hue)}<b>${name}</b>${this.icon('phone')}</div>
        <div class="bubbles">${chat}</div>
        <div class="compose"><span>${draft || 'Write a message'}</span><span class="send">${this.icon('send')}</span></div>
      </div>
    </div>`;

    return this.window(`Messages · ${phone}`, body, frame);
  }

  /** The incoming call window with Decline and Answer. */
  call(name, phone, hue, { x, y }) {
    return `<div class="call" style="left:${x}px;top:${y}px">
      ${this.avatar(name.split(' ').map((w) => w[0]).join(''), hue)}
      <div class="who"><div class="name">${name}</div><div class="text">Calling on ${phone}</div></div>
      <span class="round decline">${this.icon('phone-off')}</span>
      <span class="round answer">${this.icon('phone')}</span>
    </div>`;
  }

  /** A notification from the phone, as it pops up on the computer. */
  notification(app, title, text, { x, y }) {
    return `<div class="note" style="left:${x}px;top:${y}px">
      <img src="${this.assets.appIcon}">
      <div class="body"><div class="top"><span>${app}</span><span>now</span></div><div class="title">${title}</div><div class="text">${text}</div></div>
      <span class="reply">Reply</span>
    </div>`;
  }

  /** The transfers window: what's arriving and what already landed. */
  transfers(items, frame) {
    const rows = items.map(({ icon, title, detail, progress }) => {
      const bar = progress != null ? `<div class="progress"><i style="width:${progress}%"></i></div>` : '';
      const end = progress != null ? `<span class="percent">${progress}%</span>` : `<span class="done">${this.icon('circle-check')}</span>`;

      return `<div class="row"><span class="chip">${this.icon(icon)}</span><div class="body"><div class="title">${title}</div>${bar}<div class="detail">${detail}</div></div>${end}</div>`;
    }).join('');

    return this.window('Transfers', `<div class="transfers">${rows}</div>`, frame);
  }

  /** The phone's screen in a window on the computer, with a cursor over it. */
  mirror(phone, appHtml, frame) {
    const width = frame.width - 40;
    const scale = width / 412;
    const app = appHtml.replace('class="app', `style="width:412px;height:${(frame.height - 80) / scale}px;transform:scale(${scale})" class="app`);

    return this.window(phone, `<div class="mirror"><div class="screen" style="width:${width}px;height:${frame.height - 80}px">${app}</div><span class="cursor">${this.icon('mouse-pointer-2')}</span></div>`, frame);
  }

  /** A row of photos in a folder, to stand for files that arrived. */
  folder(name, count, frame) {
    const photos = ['sunset', 'sea', 'city', 'forest', 'dusk', 'sand'].slice(0, count)
      .map((p) => `<div class="photo ${p}"></div>`).join('');

    return this.window(name, `<div class="folder">${photos}</div>`, { ...frame, icon: false });
  }
}
