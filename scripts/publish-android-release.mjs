// Publishes an Android APK to this repository's GitHub Releases and writes dist/android-release.json,
// the tracked manifest the website's download card reads. Because the APK lives on GitHub and the
// manifest is committed, Git-triggered Vercel deploys keep the download.
//
//   node scripts/publish-android-release.mjs <version> <path-to-apk> [--verify-only]
//
// Requires the GitHub CLI logged in to the repository owner (`gh auth login`). --verify-only skips the
// upload (for an APK attached by hand on github.com) and only checks the published file.
import { createReadStream } from 'node:fs';
import { copyFile, mkdtemp, open, rm, stat, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
const verifyOnly = args.includes('--verify-only');
const [version, sourceArg] = args.filter((arg) => !arg.startsWith('--'));
if (!/^\d+\.\d+\.\d+$/.test(version ?? '') || !sourceArg) {
  console.error('Usage: node scripts/publish-android-release.mjs <version> <path-to-apk> [--verify-only]');
  process.exit(1);
}

const assetName = 'grid-run-android.apk';
const tag = `android-v${version}`;
const remote = execFileSync('git', ['remote', 'get-url', 'origin'], { cwd: root, encoding: 'utf8' }).trim();
const repo = remote.match(/github\.com[:/]([\w.-]+\/[\w.-]+?)(?:\.git)?$/)?.[1];
if (!repo) throw new Error('The origin remote is not a GitHub repository.');
const url = `https://github.com/${repo}/releases/download/${tag}/${assetName}`;

const source = resolve(sourceArg);
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
const sha256 = hash.digest('hex');

function findGh() {
  const candidates = [process.env.GH_PATH, 'gh', join(process.env.ProgramFiles ?? 'C:/Program Files', 'GitHub CLI', 'gh.exe')];
  for (const candidate of candidates.filter(Boolean)) {
    try {
      execFileSync(candidate, ['--version'], { stdio: 'ignore' });
      return candidate;
    } catch {}
  }
  throw new Error('GitHub CLI not found. Install it (winget install GitHub.cli), run `gh auth login`, or set GH_PATH.');
}

if (!verifyOnly) {
  const gh = findGh();
  const folder = await mkdtemp(join(tmpdir(), 'grid-run-release-'));
  try {
    const asset = join(folder, assetName);
    await copyFile(source, asset);
    let exists = true;
    try {
      execFileSync(gh, ['release', 'view', tag, '--repo', repo], { stdio: 'ignore' });
    } catch {
      exists = false;
    }
    const run = (ghArgs) => execFileSync(gh, ghArgs, { stdio: 'inherit' });
    if (exists) run(['release', 'upload', tag, asset, '--clobber', '--repo', repo]);
    else run(['release', 'create', tag, asset, '--repo', repo, '--title', `Grid Run for Android ${version}`,
      '--notes', `Grid Run ${version} for Android 10 or later (ARM64). SHA-256: ${sha256}`]);
  } finally {
    await rm(folder, { recursive: true, force: true });
  }
}

// Download the published file and confirm it is byte-identical before the website points at it.
let published = null;
for (let attempt = 1; attempt <= 5 && !published; attempt++) {
  const response = await fetch(url, { redirect: 'follow' });
  if (response.ok) {
    const check = createHash('sha256');
    let size = 0;
    for await (const chunk of response.body) {
      check.update(chunk);
      size += chunk.length;
    }
    published = { size, sha256: check.digest('hex') };
  } else if (attempt < 5) {
    await new Promise((done) => setTimeout(done, 3000));
  } else {
    throw new Error(`${url} returned HTTP ${response.status}; nothing was written.`);
  }
}
if (published.size !== info.size || published.sha256 !== sha256) {
  throw new Error(`The published APK differs from ${source} (size ${published.size}, SHA-256 ${published.sha256}); nothing was written.`);
}

const manifest = { version, sizeBytes: info.size, sha256, url, publishedAt: new Date().toISOString() };
const manifestPath = resolve(root, 'dist/android-release.json');
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
console.log(`Published ${tag}: ${(info.size / 1048576).toFixed(1)} MiB, SHA-256 ${sha256}`);
console.log(`Verified ${url}`);
console.log('Wrote dist/android-release.json; commit it to update the website.');
