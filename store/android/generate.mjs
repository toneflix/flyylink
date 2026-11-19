// Renders the Google Play feature graphic and phone, tablet and TV screenshots from page/.
// Run from this folder: npm install && npm run generate
import { mkdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { Assets } from './assets.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '../..');
const { chromium } = createRequire(import.meta.url)('playwright');

/** Screenshots each graphic in page/index.html at its canvas size, times its scale. */
class Renderer {
  constructor(assets, outputs) {
    this.assets = assets;
    this.outputs = outputs;
  }

  async run() {
    const browser = await chromium.launch();
    const index = pathToFileURL(join(HERE, 'page/index.html'));
    const pages = new Map();

    const pageAt = async (scale) => {
      if (!pages.has(scale)) {
        const page = await browser.newPage({ viewport: { width: 1080, height: 1920 }, deviceScaleFactor: scale });
        await page.addInitScript((assets) => { window.ASSETS = assets; }, this.assets);
        pages.set(scale, page);
      }

      return pages.get(scale);
    };

    const first = await pageAt(1);
    await first.goto(index.href);
    const graphics = await first.evaluate(() => [...GRAPHICS, ...TV_GRAPHICS].map(({ id, width, height, scale }) => ({ id, width, height, scale })));

    for (const { id, width = 1080, height = 1920, scale = 1 } of graphics) {
      const page = await pageAt(scale);

      await page.setViewportSize({ width, height });
      await page.goto(`${index}?graphic=${id}`);
      await page.evaluate(() => document.fonts.ready);

      const out = this.outputs(id);
      await page.locator('.canvas').screenshot({ path: out });
      console.log(`wrote ${out}`);
    }

    await browser.close();
  }
}

/** Where each graphic goes: the TV banner inside the app is the launcher's, the rest are for the listing. */
function outputFor(id) {
  if (id === 'feature-graphic') return join(HERE, 'feature-graphic.png');
  if (id === 'amazon-promo') return join(HERE, 'amazon/promo.png');
  if (id.startsWith('amazon-phone-')) return join(HERE, `amazon/screenshots/${id.slice('amazon-phone-'.length)}.png`);
  if (id === 'tv-banner-app') return join(ROOT, 'android/app/src/main/res/drawable-xhdpi/tv_banner.png');
  if (id === 'tv-banner') return join(HERE, 'tv/banner.png');
  if (id.startsWith('tv-')) return join(HERE, `tv/screenshots/${id.slice(3)}.png`);

  const tablet = id.match(/^(amazon-)?tablet-(\d+)-(.+)$/);
  if (tablet) return join(HERE, `${tablet[1] ? 'amazon/' : ''}tablet/${tablet[2]}-inch/${tablet[3]}.png`);

  return join(HERE, `screenshots/${id}.png`);
}

const assets = new Assets(ROOT).load();

for (const folder of ['screenshots', 'tablet/7-inch', 'tablet/10-inch', 'amazon/screenshots', 'amazon/tablet/7-inch', 'amazon/tablet/10-inch', 'tv/screenshots', '../../android/app/src/main/res/drawable-xhdpi']) {
  mkdirSync(join(HERE, folder), { recursive: true });
}

await new Renderer(assets, outputFor).run();
