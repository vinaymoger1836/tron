import { createReadStream } from 'node:fs';
import { copyFile, mkdir, open, stat, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const [phase, version, sourceArg] = process.argv.slice(2);
if (!/^\d+$/.test(phase ?? '') || !/^\d+\.\d+\.\d+$/.test(version ?? '') || !sourceArg) {
  console.error('Usage: node scripts/stage-android-apk.mjs <phase> <version> <path-to-apk>');
  process.exit(1);
}

const source = resolve(sourceArg);
const destination = resolve(root, 'dist/downloads/grid-run-android.apk');
if (source === destination) throw new Error('The source APK must be outside the download directory.');
const info = await stat(source);
if (!info.isFile() || info.size === 0) throw new Error('The APK must be a nonempty file.');
const handle = await open(source, 'r');
try {
  const magic = Buffer.alloc(4);
  await handle.read(magic, 0, 4, 0);
  if (!magic.equals(Buffer.from([0x50, 0x4b, 0x03, 0x04]))) throw new Error('The file is not an APK/ZIP archive.');
} finally {
  await handle.close();
}
const hash = createHash('sha256');
for await (const chunk of createReadStream(source)) hash.update(chunk);
await mkdir(dirname(destination), { recursive: true });
await copyFile(source, destination);
const staged = await stat(destination);
if (staged.size !== info.size) throw new Error('Staged APK size differs from the source.');
const release = {
  phase: Number(phase),
  version,
  sizeBytes: info.size,
  sha256: hash.digest('hex'),
  stagedAt: new Date().toISOString(),
};
await writeFile(resolve(root, 'dist/downloads/release.json'), `${JSON.stringify(release, null, 2)}\n`, 'utf8');
console.log(`Staged Phase ${phase} v${version}: ${(info.size / 1048576).toFixed(1)} MiB`);
console.log('Deploy from the repository root after checking the Vercel dry-run manifest.');
