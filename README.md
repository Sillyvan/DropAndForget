# DropAndForget

DropAndForget is a simple Dropbox alternative built on top of Cloudflare R2.

Use your own bucket, keep control of your storage, and pay for what you actually use. Cloudflare R2 pricing: https://developers.cloudflare.com/r2/pricing/

DropAndForget is a desktop app and works cross-platform through Avalonia builds.

## Why use it

- Your files live in your own Cloudflare R2 bucket
- No bundled storage subscription from us
- Simple drag and drop uploads
- Works on multiple platforms
- Optional encrypted mode for private buckets

## Features

- Connect directly to a Cloudflare R2 bucket
- Drag and drop files into the app
- Browse folders and files in the bucket
- Create folders, rename items, move items, and delete items
- Download files or export folders/selections as zip
- Search across the bucket
- File previews for common formats like text, images, and PDFs
- Optional sync mode with a local folder
- Optional encrypted mode with passphrase unlock

## Downloads

Grab the latest builds from the [Releases tab](https://github.com/Sillyvan/DropAndForget/releases).

- Each release asset also ships with a matching `.sha256` file for checksum verification.

## Install Notes

- macOS builds are not notarized. macOS may block first launch until you manually allow the app in Finder or System Settings.
- Windows builds are not code signed. SmartScreen may warn before launch.
- Linux packages are unsigned direct-download artifacts.

## Local Credentials

- Bucket credentials are saved locally in the app config so you do not need to re-enter them every launch.
- The app stores those saved credentials encrypted at rest with a machine-local key.
- Encryption-mode passphrases are not saved locally.
- Debug file logging is off by default in normal app use.

## Encryption Mode

Encryption mode is for people who want Cloudflare R2 to store encrypted blobs instead of readable files.

When enabled:

- File contents are encrypted before upload
- Filenames and folder paths are hidden from R2 too
- The app asks for your passphrase every launch
- The passphrase is not saved locally
- Multiple devices can open the same encrypted bucket if they know the same passphrase

### What it uses

- `Argon2id` for passphrase-based key derivation
- `XChaCha20-Poly1305` for authenticated encryption
- A bucket master key plus per-file keys, instead of one forever-key for everything

### Why that is safe

- `Argon2id` is designed to slow down brute-force attacks on passphrases
- `XChaCha20-Poly1305` provides modern authenticated encryption, so data is both encrypted and tamper-checked
- R2 never sees plaintext file contents, filenames, or folder structure in encrypted mode
- Your passphrase stays in memory for the session only

Important: if you lose the encryption passphrase, your encrypted files are not recoverable.

## Privacy And Support

- DropAndForget connects directly to your Cloudflare R2 bucket. The app is not a hosted storage service.
- The project does not provide account recovery for lost encryption passphrases.
- For bugs, release issues, or support, use [GitHub Issues](https://github.com/Sillyvan/DropAndForget/issues).

## Tech Stack

- `.NET 10`
- `Avalonia` for the desktop UI
- `Cloudflare R2` via its S3-compatible API
- `NSec.Cryptography` for encryption

That is the whole idea: a small desktop app for owning your own cloud storage without paying for a full Dropbox-style service.
