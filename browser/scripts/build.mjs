// Builds the add-on for each browser into dist/: `node scripts/build.mjs`, or `node scripts/build.mjs --store` for the
// zips the stores take. Chrome and Edge share one build; Firefox gets its own background and add-on id.
import { cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const source = join(root, 'src');
const dist = join(root, 'dist');
const store = process.argv.includes('--store');
const manifest = JSON.parse(readFileSync(join(source, 'manifest.json'), 'utf8'));

const targets = {
  chrome(base) {
    // The key gives the unpacked add-on the id FlyyLink trusts. The stores give their own, so it stays out of those.
    const { key, ...rest } = base;
    return store ? rest : base;
  },

  firefox(base) {
    const { key, background, ...rest } = base;
    return {
      ...rest,
      background: { scripts: [background.service_worker], type: 'module' },
      browser_specific_settings: {
        gecko: {
          id: 'browser@flyylink.toneflix.net',
          strict_min_version: '128.0',
          data_collection_permissions: { required: ['none'] },
        },
      },
    };
  },
};

rmSync(dist, { recursive: true, force: true });

for (const [name, adapt] of Object.entries(targets)) {
  const folder = join(dist, name);
  mkdirSync(folder, { recursive: true });
  cpSync(source, folder, { recursive: true });
  writeFileSync(join(folder, 'manifest.json'), JSON.stringify(adapt(manifest), null, 2) + '\n');

  if (store) {
    const zip = join(dist, `flyylink-${name}-${manifest.version}.zip`);
    execFileSync('zip', ['-qr', zip, '.'], { cwd: folder });
    console.log(`Built ${zip}`);
  } else {
    console.log(`Built ${folder}`);
  }
}
