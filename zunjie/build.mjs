import { cp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = new URL('./', import.meta.url);
const output = new URL('./_site/', root);
const pkg = JSON.parse(await readFile(new URL('package.json', root), 'utf8'));
const commit = process.env.GITHUB_SHA || execFileSync('git', ['rev-parse', 'HEAD'], {
  cwd: fileURLToPath(root), encoding: 'utf8',
}).trim();
const version = `${pkg.version}-${commit.slice(0, 8)}`;
const cacheKey = encodeURIComponent(version);

await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });
for (const file of ['index.html', 'style.css', 'app.js', 'engine.js', 'scene.js', 'assets']) {
  await cp(new URL(file, root), new URL(file, output), { recursive: true });
}

// Version every browser reference so CDN/browser caches fetch the new release.
for (const file of ['index.html', 'app.js']) {
  const target = new URL(file, output);
  const content = (await readFile(target, 'utf8')).replace(
    /\.\/(app\.js|engine\.js|scene\.js|style\.css|assets\/favicon\.svg)/g,
    (reference) => `${reference}?v=${cacheKey}`,
  );
  await writeFile(target, content);
}

await writeFile(new URL('version.json', output), JSON.stringify({
  version,
  commit,
  builtAt: new Date().toISOString(),
  sourceCommit: '796ef1598571b4b1ebcbc447fd0146e19b7810de',
}, null, 2) + '\n');
console.log(`Built ${version} in ${fileURLToPath(output)}`);
