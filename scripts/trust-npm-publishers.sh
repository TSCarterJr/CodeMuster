#!/bin/sh
# Tell npm that release.yml in this repository is allowed to publish each package,
# so the release workflow never needs a token or a one-time password again.
# Run it from a real terminal: npm opens a browser for each confirmation.
set -e

NPM="npx -y npm@11.19.0"
REPO=TSCarterJr/CodeMuster

for pkg in codemuster \
  @codemuster/darwin-arm64 @codemuster/darwin-x64 \
  @codemuster/linux-arm64 @codemuster/linux-x64 \
  @codemuster/win32-arm64 @codemuster/win32-x64; do
  echo "== $pkg"
  $NPM trust github "$pkg" --file release.yml --repo "$REPO" --allow-publish --yes
done

echo
echo "Configured. Check any of them with: $NPM trust list codemuster"
