<p align="right">
  <a href="README.md">简体中文</a> · <strong>English</strong>
</p>

<div align="center">
  <img src="desktop/public/app-logo-rounded.png" width="96" height="96" alt="Axure Downgrade Logo">
  <h1>Axure Downgrade</h1>
  <p><strong>Convert Axure RP 11 files into projects that Axure RP 9 can open and edit</strong></p>
  <p>Preserve pages, text, images, basic styling, and layout · Local processing · Source files stay unchanged</p>
  <p>
    <a href="https://github.com/muxia0396/AxureDowngrade/releases/latest"><img src="https://img.shields.io/github/v/release/muxia0396/AxureDowngrade?display_name=tag&style=flat-square" alt="Latest Release"></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/license-PolyForm_Noncommercial_1.0.0-4f46e5?style=flat-square" alt="PolyForm Noncommercial License"></a>
    <img src="https://img.shields.io/badge/platform-Windows_x64-0078d4?style=flat-square&logo=windows11" alt="Windows x64">
    <img src="https://img.shields.io/badge/Tauri-2-24c8db?style=flat-square&logo=tauri&logoColor=white" alt="Tauri 2">
  </p>
</div>

Axure Downgrade is a Windows desktop tool for converting Axure RP 11 (`.rp`) files into projects that Axure RP 9 can open and continue editing. The conversion focuses on preserving the static design: page structure, text, images, basic styles, absolute positioning, page hierarchy, and common widgets. Interactions and RP 11-only capabilities may be removed and are recorded in the conversion report.

> [!IMPORTANT]
> This project is available under the [PolyForm Noncommercial License 1.0.0](LICENSE) for noncommercial use only. Because commercial use is restricted, this is a source-available project rather than OSI-approved open-source software. Commercial use requires separate written permission from the licensor.

## Download and quick start

The recommended distribution is the Windows x64 portable package attached to GitHub Releases:

- [Download the latest release](https://github.com/muxia0396/AxureDowngrade/releases/latest)
- [Browse all releases](https://github.com/muxia0396/AxureDowngrade/releases)

To use the application:

1. Download `AxureDowngrade-<version>-windows-x64-portable.zip`
2. Optionally verify it with the matching `.sha256.txt` file
3. Extract the complete ZIP into a dedicated directory
4. Keep the executable and the `bin` directory in their original layout
5. Run `AxureDowngrade.exe`
6. Select an Axure RP 11 `.rp` file
7. On the first conversion, select the directory containing `AxureRP9.exe`
8. Start the conversion and review the generated report

### Runtime requirements

- Windows 10 or Windows 11, 64-bit
- Microsoft Edge WebView2 Runtime
- A lawfully installed copy of Axure RP 9

The application reads and processes project files locally. It does not upload project content or overwrite the source RP file. The converted RP 9 project is written to a new file.

## Features

- Probe and analyze Axure RP 11 project containers
- Rewrite pages, design documents, and document settings into RP 9-readable data
- Preserve text, images, basic styling, coordinates, and dimensions
- Preserve page hierarchy, common form widgets, connectors, and image resources
- Convert the static structure of dynamic panels and their state packages
- Remove interaction records and RP 11-only fields that cannot be converted safely
- Rebuild the outer LZ4 index and object packages
- Reload rewritten packages with the real Axure RP 9 parser for verification
- Produce conversion status, error codes, and machine-readable verification data
- Detect output files currently held open by Axure and avoid write conflicts

## Current status

The current version is **v0.1.7**. It produces editable RP 9 projects for the tested Axure RP 11 documents.

The current verification set covers:

- Pixel-identical RP 9 rendering for a controlled rectangle fixture
- 4 official Axure RP 11 training projects
- 21 real pages
- 34 dynamic-panel state packages
- All 5 official Axure 11 widget libraries
- 1,060 isolated widget pages
- 84 widget-library panel-state packages
- Text, images, vector shapes, dynamic panels, form widgets, connectors, page hierarchy, shadows, and multi-column layouts

Across these samples, counts remain unchanged for every static record type; the removed records belong only to interaction types. Each rewritten object package is immediately loaded again by the Axure RP 9 parser. Non-interaction records, scalar properties, and embedded byte-array hashes must match their pre-write snapshots.

This is still a research-oriented compatibility tool. Third-party widget libraries, custom widgets, missing fonts, external resources, and document-specific structures may require additional downgrade rules.

## Conversion principles

1. Treat the source file as read-only and never overwrite it
2. Prioritize the visible static design result
3. Do not guess when an interaction cannot be converted reliably
4. Record every removal, substitution, and downgrade in the report
5. Reload rewritten core objects through the RP 9 parser
6. Verify the rebuilt container index, package counts, and output consistency

Simplified pipeline:

```text
RP 11 file
    ↓
Container probing and package parsing
    ↓
Version-neutral intermediate representation
    ↓
Staticization and compatibility rules
    ↓
Object rewriting through the RP 9 serializer
    ↓
LZ4 container and index rebuilding
    ↓
RP 9 parser verification
    ↓
RP 9 project and conversion report
```

## Repository layout

```text
AxureDowngrade/
├─ crates/
│  ├─ axure-core/          Probing, parsing, intermediate representation, and downgrade rules
│  └─ axure-lab/           Research, comparison, and command-line tools
├─ desktop/                Tauri 2 + React + TypeScript desktop application
│  └─ src-tauri/
│     └─ bin/              RP 9 bridge and required LZ4 components
├─ fixtures/               Minimal research fixtures and IR samples
├─ tools/                  Bridge build, packaging, and verification scripts
├─ docs/                   Technical research, format evidence, and error documentation
└─ .github/                Issue templates and release workflow
```

See [docs/FRAMEWORK_DECISION.md](docs/FRAMEWORK_DECISION.md) for the framework decision, [the complete Chinese technical specification](docs/Axure11-9降级标准化技术文档.md) for the downgrade design, and [docs/FORMAT_EVIDENCE.md](docs/FORMAT_EVIDENCE.md) for sample-backed format findings.

## Development

Requirements:

- Node.js 20 or newer
- Rust stable with the MSVC toolchain
- Visual Studio Build Tools
- The Desktop development with C++ workload
- Microsoft Edge WebView2 Runtime
- A lawful Axure RP 9 installation for bridge builds and full verification

Start the desktop development environment:

```powershell
cd desktop
npm ci
npm run tauri dev
```

Build the frontend:

```powershell
cd desktop
npm run build
```

Run the core tests from the repository root:

```powershell
cargo test -p axure-core
```

## Research commands

Inspect an RP file as JSON:

```powershell
cargo run -p axure-lab -- inspect fixtures\axure9\00-empty.rp
```

Compare a controlled RP 9 / RP 11 pair:

```powershell
cargo run -p axure-lab -- compare `
  fixtures\axure9\00-empty.rp `
  fixtures\axure11\00-empty.rp `
  --summary
```

Inspect independently compressed packages:

```powershell
cargo run -p axure-lab -- inspect-packages fixtures\axure11\01-rectangle.rp
```

Staticize a version-neutral document IR:

```powershell
cargo run -p axure-lab -- staticize document-ir.json > static-document.json
```

Staticization converts nested widgets to page-relative absolute coordinates, expands groups, components, repeaters, and dynamic panels, and removes non-visual hotspots. Every drop or substitution is reported.

## Build the RP 9 bridge

The Windows bridge uses serialization and LZ4 components from a lawfully installed copy of Axure RP 9:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\build-bridge.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9
```

The desktop package includes the resulting bridge and its LZ4 dependencies. At conversion time, users still select the Axure RP 9 installation directory so the bridge can load `AxureRP9.exe` and the required model assemblies.

Direct bridge invocation:

```powershell
desktop\src-tauri\bin\AxureDowngradeBridge.exe `
  D:\ToolsWork\Axure9 `
  input-rp11.rp `
  output-rp9.rp
```

## Verification scripts

Verify repository fixtures:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\verify-fixtures.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9
```

Verify official training projects:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\verify-complex-samples.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9 `
  -Axure11Directory D:\ToolsWork\Axure11
```

Verify official Axure 11 widget libraries:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\verify-library-samples.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9 `
  -Axure11Directory D:\ToolsWork\Axure11
```

Open and verify generated files in the real Axure RP 9 GUI:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\verify-axure9-gui.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9
```

## Build a portable release

Build the optimized Windows executable:

```powershell
cd desktop
npm run tauri -- build --no-bundle
```

Package the executable from the repository root:

```powershell
tools\package-release.ps1 -Version 0.1.7 -Force
```

The release contains:

```text
AxureDowngrade.exe
bin\AxureDowngradeBridge.exe
bin\K4os.Compression.LZ4.dll
bin\K4os.Compression.LZ4.Legacy.dll
README.txt
ERROR_CODES.md
LICENSE
NOTICE
THIRD_PARTY_NOTICES.md
```

The executable must remain next to the `bin` directory in this layout.

## Error handling

When conversion fails, the UI displays an error code, cause, and detailed message. See [docs/ERROR_CODES.md](docs/ERROR_CODES.md) for the complete reference.

The bridge returns a JSON verification report with rewritten page, design-document, settings-package, and removed-interaction counts. The desktop application checks those values against the rebuilt RP container before reporting success.

## Release process

Before publishing, keep the version in these files aligned:

- `Cargo.toml`
- `desktop/package.json`
- `desktop/src-tauri/tauri.conf.json`

Create and push a version tag:

```powershell
git tag v0.1.7
git push origin v0.1.7
```

GitHub Actions tests the core, builds the Windows executable, creates the portable archive and SHA-256 file, and creates or updates the matching GitHub Release.

## Privacy and security

- RP files are processed only on the user's computer
- The application has no file-upload feature
- Source RP files are never overwritten
- The project does not distribute Axure RP itself or its proprietary assemblies
- Users must supply their own lawfully licensed Axure RP 9 installation

Read [SECURITY.md](SECURITY.md) before reporting a vulnerability. Do not attach RP files containing confidential business information or personal data to public issues.

## Contributing

Bug reports, compatibility evidence, minimal test fixtures, and code improvements are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a contribution.

RP files may contain unreleased product designs, customer information, and personal data. Only submit fixtures you created yourself or thoroughly sanitized minimal reproductions.

## License and notices

Original Axure Downgrade code is available under the [PolyForm Noncommercial License 1.0.0](LICENSE):

- Personal, educational, charitable, public research, and other noncommercial use is allowed
- Modification and redistribution are allowed only for noncommercial purposes
- Redistributions must preserve the license and required notice
- Commercial use requires separate written permission from the licensor

See [NOTICE](NOTICE) for the required copyright notice and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependency and trademark notices.

Axure is a trademark of its respective owner. This is an independent research and compatibility project and is not affiliated with, authorized by, or endorsed by Axure.
