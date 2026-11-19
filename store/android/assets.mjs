import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

/** The logo, app icon and Lucide icons the app uses, read from the repo so they stay in sync. */
export class Assets {
  constructor(root) {
    this.root = root;
  }

  load() {
    return {
      mark: this.dataUrl(join(this.root, 'branding/flyylink-mark.svg'), 'image/svg+xml'),
      appIcon: this.dataUrl(join(this.root, 'branding/play-store-icon.png'), 'image/png'),
      icons: this.lucideIcons(join(this.root, 'android/app/src/main/res/drawable')),
    };
  }

  dataUrl(path, type) {
    return `data:${type};base64,${readFileSync(path).toString('base64')}`;
  }

  lucideIcons(folder) {
    const icons = {};

    for (const file of readdirSync(folder).filter((name) => name.startsWith('lucide_'))) {
      const name = file.slice('lucide_'.length, -'.xml'.length).replaceAll('_', '-');
      icons[name] = this.vectorToSvg(readFileSync(join(folder, file), 'utf8'));
    }

    return icons;
  }

  /** Turns an Android vector drawable of stroked or filled paths into inline SVG. */
  vectorToSvg(xml) {
    const paths = [...xml.matchAll(/<path([\s\S]*?)\/>/g)].map(([, attrs]) => {
      const d = attrs.match(/android:pathData="([^"]+)"/)[1];
      const stroked = attrs.includes('android:strokeColor');
      const filled = attrs.includes('android:fillColor');

      return `<path d="${d}" fill="${filled ? 'currentColor' : 'none'}" stroke="${stroked ? 'currentColor' : 'none'}"/>`;
    });

    return `<svg viewBox="0 0 24 24" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${paths.join('')}</svg>`;
  }
}
