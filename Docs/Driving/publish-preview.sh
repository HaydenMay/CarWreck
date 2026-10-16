#!/usr/bin/env bash
# Publishes Builds/WebGLDrive to the `gh-pages` branch (GitHub Pages: https://<owner>.github.io/CarWreck/).
# Usage (from the repo root, after building):  bash Docs/Driving/publish-preview.sh
# Build first:  Unity -batchmode -quit -projectPath . -buildTarget WebGL \
#               -executeMethod CarWreck.Driving.EditorTools.DrivingWebBuild.Build
#
# The gh-pages branch holds only built files (an orphan branch); development stays on the feature branch.
# Cache safety: GitHub Pages lets browsers cache files for ~10 minutes, so the Unity build files are renamed to include the
# build id (Build/WebGLDrive.<id>.*) and index.html is patched to match. The previous build's files are kept for one more
# publish so a browser holding a cached older index.html still finds its files. version.json carries the build id; the page
# compares it with its own and reloads once if it is stale.
set -euo pipefail

SRC="Builds/WebGLDrive"
[ -f "$SRC/index.html" ] || { echo "No build at $SRC - build the WebGL player first." >&2; exit 1; }

SOURCE_SHA="$(git rev-parse --short HEAD)"
SOURCE_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
AUTHOR_NAME="$(git log -1 --format=%an)"
AUTHOR_EMAIL="$(git log -1 --format=%ae)"
BUILD_ID="$(grep -o 'const BUILD_ID = "[^"]*"' "$SRC/index.html" | head -1 | sed 's/.*"\(.*\)"/\1/')"
[ -n "$BUILD_ID" ] || { echo "Could not read the build id from $SRC/index.html" >&2; exit 1; }
WORK="$(mktemp -d)"
trap 'git worktree remove --force "$WORK" >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT

git fetch origin gh-pages >/dev/null 2>&1 || true
if git show-ref --verify --quiet refs/remotes/origin/gh-pages; then
  git worktree add -B gh-pages "$WORK" origin/gh-pages >/dev/null
else
  git worktree add --detach "$WORK" >/dev/null
  (cd "$WORK" && git checkout --orphan gh-pages >/dev/null 2>&1 && git rm -rf . >/dev/null 2>&1 || true)
fi

# Previous build id (to keep its files one more publish).
PREV_ID=""
[ -f "$WORK/version.json" ] && PREV_ID="$(grep -o '"buildId": "[^"]*"' "$WORK/version.json" | sed 's/.*: "\(.*\)"/\1/' || true)"

# Remove everything except the previous build's files, then add the new build.
find "$WORK" -mindepth 1 -maxdepth 1 ! -name .git ! -name Build -exec rm -rf {} +
if [ -d "$WORK/Build" ]; then
  find "$WORK/Build" -type f ! -name "*.${PREV_ID:-__none__}.*" -delete 2>/dev/null || true
  # (files are named WebGLDrive.<id>.<kind>; the pattern above keeps only the previous id's files)
fi
mkdir -p "$WORK/Build"
for f in "$SRC"/Build/*; do
  base="$(basename "$f")"
  cp "$f" "$WORK/Build/${base/WebGLDrive./WebGLDrive.${BUILD_ID}.}"
done
sed "s#Build/WebGLDrive\.#Build/WebGLDrive.${BUILD_ID}.#g" "$SRC/index.html" > "$WORK/index.html"
[ -d "$SRC/StreamingAssets" ] && cp -r "$SRC/StreamingAssets" "$WORK/"
touch "$WORK/.nojekyll"
printf '{ "buildId": "%s", "sourceBranch": "%s", "sourceCommit": "%s", "published": "%s" }\n' \
  "$BUILD_ID" "$SOURCE_BRANCH" "$SOURCE_SHA" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$WORK/version.json"

cd "$WORK"
git add -A
# Same author as the source commit (the repo may rely on a system-derived identity that a temp worktree does not see).
export GIT_AUTHOR_NAME="${GIT_AUTHOR_NAME:-$AUTHOR_NAME}" GIT_AUTHOR_EMAIL="${GIT_AUTHOR_EMAIL:-$AUTHOR_EMAIL}"
export GIT_COMMITTER_NAME="${GIT_COMMITTER_NAME:-$AUTHOR_NAME}" GIT_COMMITTER_EMAIL="${GIT_COMMITTER_EMAIL:-$AUTHOR_EMAIL}"
git commit -m "Publish driving prototype preview ${BUILD_ID} (source ${SOURCE_BRANCH} @ ${SOURCE_SHA})" >/dev/null
git push origin gh-pages
echo "Pushed build ${BUILD_ID}. Live (after Pages rebuilds, ~1 min): https://haydenmay.github.io/CarWreck/"
