# Releasing

Versioning uses SemVer git tags via MinVer.

- tag format: `vMAJOR.MINOR.PATCH`
- examples: `v0.1.0`, `v0.1.1`, `v1.0.0`
- the tag drives app version metadata during release builds

## Release flow

1. merge to `main`
2. create and push a tag like `v0.2.0`
3. create a GitHub Release for that tag and publish it
4. GitHub Actions builds and uploads:
   - `DropAndForget-<version>-win-x64.zip`
   - `DropAndForget-<version>-win-arm64.zip`
   - `DropAndForget-<version>-macos-x64.dmg`
   - `DropAndForget-<version>-macos-arm64.dmg`
   - `DropAndForget-<version>-linux-x64.tar.gz`
   - `DropAndForget-<version>-linux-arm64.tar.gz`
   - `DropAndForget-<version>-linux-x64.deb`
   - `DropAndForget-<version>-linux-arm64.deb`
   - `DropAndForget-<version>-linux-x64.rpm`
   - `DropAndForget-<version>-linux-arm64.rpm`
   - matching `.sha256` checksum files for every uploaded asset

## Workflows

- `.github/workflows/ci.yml`
  - runs tests on pushes to `main`
- `.github/workflows/release.yml`
  - runs on GitHub Release publish
  - tests first
  - builds `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, and `linux-arm64`
  - uploads release assets to the published release
  - rewrites the release body with a generated download table and checksum links

## Notes

- Windows release ships zipped published builds for `win-x64` and `win-arm64`.
- Every uploaded release asset also gets a `.sha256` sidecar file.
- Release notes get a generated downloads table with direct asset and checksum links.
- Windows publish enables single-file extraction for all bundled content to avoid missing sidecar issues at launch.
- macOS release currently ships `.dmg` assets for `osx-arm64` and `osx-x64`.
- macOS release does not currently upload a zipped `.app` bundle.
- Without notarization, macOS may still warn or require right-click Open or a manual allow step in System Settings.
- Linux release ships a tarball with app files plus desktop integration files under `usr/share`, using `Assets/logo.svg` as the launcher icon.
- Linux release ships tarballs, `.deb` packages, and `.rpm` packages for both `linux-x64` and `linux-arm64`.
- macOS app bundles are code signed during packaging. With no configured identity, packaging falls back to ad-hoc signing.
- notarization only runs when `MACOS_SIGN_IDENTITY`, `MACOS_NOTARY_APPLE_ID`, `MACOS_NOTARY_TEAM_ID`, and `MACOS_NOTARY_PASSWORD` are configured in GitHub Actions secrets.
