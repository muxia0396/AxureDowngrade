# Contributing

Thank you for helping improve Axure Downgrade.

## Before opening an issue

- Search existing issues first
- Remove confidential client content from screenshots and logs
- Do not upload proprietary `.rp` files unless you own them and intend to make
  them public
- Include the Axure RP source version, Windows version and the displayed error
  code

## Development setup

Requirements:

- Node.js 20 or newer
- Rust stable with the MSVC toolchain
- Microsoft Edge WebView2
- Visual Studio Build Tools with the Desktop development with C++ workload
- A lawfully installed copy of Axure RP 9 for conversion integration testing

```powershell
cd desktop
npm ci
npm run tauri dev
```

Run checks before submitting changes:

```powershell
cargo test -p axure-core
cd desktop
npm run build
```

## License of contributions

By submitting a contribution, you agree that your contribution may be
distributed under the repository's PolyForm Noncommercial License 1.0.0 and
that you have the right to provide it under those terms.

Commercial use remains prohibited unless separately authorized in writing by
the licensor.
