// Renders the social media graphics in images/ from page/.
// Run from store/android after npm install there: npm run promo
import { mkdirSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { Assets } from '../android/assets.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '../..');
const OUT = join(HERE, 'images');
const { chromium } = createRequire(join(HERE, '../android/package.json'))('playwright');

/** The store assets plus the Windows app's Lucide icons, which cover the computer screens. */
class PromoAssets extends Assets {
  load() {
    const assets = super.load();

    return { ...assets, icons: { ...this.windowsIcons(), ...assets.icons } };
  }

  windowsIcons() {
    const source = readFileSync(join(this.root, 'windows/src/FlyyLink/Icons/Lucide.cs'), 'utf8');
    const icons = {};

    for (const [, name, d] of source.matchAll(/Lucide\.(\w+) => "([^"]+)"/g)) {
      const kebab = name.replace(/([a-z0-9])([A-Z0-9])/g, '$1-$2').toLowerCase();
      icons[kebab] = `<svg viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="${d}" fill="none" stroke="currentColor"/></svg>`;
    }

    return icons;
  }
}

const assets = new PromoAssets(ROOT).load();
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1080, height: 1080 } });
const index = pathToFileURL(join(HERE, 'page/index.html'));

await page.addInitScript((value) => { window.ASSETS = value; }, assets);
await page.goto(index.href);

const graphics = await page.evaluate(() => PROMO.map(({ id, width, height }) => ({ id, width, height })));
mkdirSync(OUT, { recursive: true });

for (const { id, width, height } of graphics) {
  await page.setViewportSize({ width, height });
  await page.goto(`${index}?graphic=${id}`);
  await page.evaluate(async () => { await document.fonts.ready; fit(); });

  await page.locator('.canvas').screenshot({ path: join(OUT, `${id}.png`) });
  console.log(`wrote images/${id}.png`);
}

await browser.close();
