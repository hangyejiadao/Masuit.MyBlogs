import { readFile, writeFile, copyFile, readdir } from "node:fs/promises";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = fileURLToPath(new URL("../", import.meta.url));
const dist = path.join(root, "dist");
const upstream = JSON.parse(await readFile(path.join(root, "UPSTREAM.json"), "utf8"));
await copyFile(path.join(root, "LICENSE"), path.join(dist, "LICENSE.txt"));
execFileSync("tar", ["-czf", path.join(dist, "streambert-source.tar.gz"), "-C", root,
  "src", "public", "scripts", "tests", "package.json", "package-lock.json", "vite.config.js",
  "playwright.config.js", "index.html", "README.md", "UPSTREAM.json", "UPSTREAM-README.md", "LICENSE", "tmdb-tutorial.md"], { stdio: "inherit" });
const sourceHash = createHash("sha256").update(await readFile(path.join(dist, "streambert-source.tar.gz"))).digest("hex");
const assets = {};
async function record(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const filename = path.join(directory, entry.name);
    if (entry.isDirectory()) await record(filename);
    else assets[path.relative(dist, filename).replaceAll("\\", "/")] = createHash("sha256").update(await readFile(filename)).digest("hex");
  }
}
await record(dist);
await writeFile(path.join(dist, "version.json"), JSON.stringify({
  name: "Streambert Web", version: upstream.version, upstreamCommit: upstream.commit,
  builtAt: new Date().toISOString(), sourceHash, assets,
}, null, 2) + "\n");
console.log(`Packaged Streambert Web: ${Object.keys(assets).length} files, source ${sourceHash.slice(0, 12)}`);
