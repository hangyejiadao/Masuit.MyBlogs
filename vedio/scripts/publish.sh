#!/usr/bin/env bash
set -euo pipefail
ARCHIVE=${1:?Usage: publish-vedio.sh ARCHIVE [DEPLOY_PATH]}
DEPLOY_PATH=${2:-/home/work/myblogs}
case "$DEPLOY_PATH" in /*) ;; *) echo 'DEPLOY_PATH must be absolute' >&2; exit 1 ;; esac
test -s "$ARCHIVE"
test -d "$DEPLOY_PATH/appwwwroot"
STAGING=$(mktemp -d /tmp/myblogs-vedio.XXXXXX)
trap 'rm -rf "$STAGING"' EXIT
# Reject absolute paths and traversal before extracting a release.
if tar -tzf "$ARCHIVE" | grep -Eq '(^/|(^|/)\.\.(/|$))'; then
  echo 'Unsafe archive paths' >&2; exit 1
fi
tar -xzf "$ARCHIVE" --no-same-owner -C "$STAGING"
python3 - "$STAGING" <<'PY'
import hashlib, json, pathlib, sys
root = pathlib.Path(sys.argv[1]).resolve()
manifest = json.loads((root / 'version.json').read_text())
assert manifest['name'] == 'Streambert Web'
for filename, digest in manifest['assets'].items():
    path = (root / filename).resolve()
    assert path.is_relative_to(root), filename
    assert hashlib.sha256(path.read_bytes()).hexdigest() == digest, filename
for filename in ('index.html', 'logo.svg', 'LICENSE.txt', 'streambert-source.tar.gz'):
    assert filename in manifest['assets'] and (root / filename).stat().st_size > 0, filename
assert '/vedio/assets/' in (root / 'index.html').read_text()
PY
DESTINATION="$DEPLOY_PATH/appwwwroot/vedio"
if sudo test -d "$DESTINATION"; then
  sudo mkdir -p "$DEPLOY_PATH/backups/vedio"
  sudo tar -czf "$DEPLOY_PATH/backups/vedio/$(date -u +%Y%m%dT%H%M%SZ).tar.gz" -C "$DESTINATION" .
fi
sudo mkdir -p "$DESTINATION"
sudo rsync -rlt --exclude=index.html --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r "$STAGING/" "$DESTINATION/"
sudo install -m 644 "$STAGING/index.html" "$DESTINATION/index.html.next"
sudo mv "$DESTINATION/index.html.next" "$DESTINATION/index.html"
echo "Streambert Web published to $DESTINATION"
