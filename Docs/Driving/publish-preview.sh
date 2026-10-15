#!/usr/bin/env bash
# Publishes Builds/WebGLDrive to the `gh-pages` branch (GitHub Pages: https://<owner>.github.io/CarWreck/).
# Usage (from the repo root, after building):  bash Docs/Driving/publish-preview.sh
# Build first:  Unity -batchmode -quit -projectPath . -buildTarget WebGL \
#               -executeMethod CarWreck.Driving.EditorTools.DrivingWebBuild.Build
# The gh-pages branch holds only built files (an orphan branch); development stays on the feature branch.
set -euo pipefail

SRC="Builds/WebGLDrive"
[ -f "$SRC/index.html" ] || { echo "No build at $SRC - build the WebGL player first." >&2; exit 1; }

SOURCE_SHA="$(git rev-parse --short HEAD)"
SOURCE_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
BUILD_ID="$(grep -o 'productVersion[^,]*' "$SRC/index.html" | head -1 || true)"
WORK="$(mktemp -d)"
trap 'git worktree remove --force "$WORK" >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT

git fetch origin gh-pages >/dev/null 2>&1 || true
if git show-ref --verify --quiet refs/remotes/origin/gh-pages; then
  git worktree add -B gh-pages "$WORK" origin/gh-pages >/dev/null
else
  git worktree add --detach "$WORK" >/dev/null
  (cd "$WORK" && git checkout --orphan gh-pages >/dev/null 2>&1 && git rm -rf . >/dev/null 2>&1 || true)
fi

# Replace the published build wholesale (stale Build/* files must not linger).
find "$WORK" -mindepth 1 -maxdepth 1 ! -name .git -exec rm -rf {} +
cp -r "$SRC"/. "$WORK"/
touch "$WORK/.nojekyll"
printf '{ "sourceBranch": "%s", "sourceCommit": "%s", "published": "%s" }\n' \
  "$SOURCE_BRANCH" "$SOURCE_SHA" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$WORK/version.json"

cd "$WORK"
git add -A
git -c user.name="$(git -C "$OLDPWD" config user.name)" -c user.email="$(git -C "$OLDPWD" config user.email)" \
  commit -m "Publish driving prototype preview (source $SOURCE_BRANCH @ $SOURCE_SHA)" >/dev/null
git push origin gh-pages
echo "Pushed. Live (after Pages rebuilds, ~1 min): https://haydenmay.github.io/CarWreck/"
