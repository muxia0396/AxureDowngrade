# Axure Downgrade

Axure Downgrade is an experimental desktop tool for converting Axure RP 11
documents into Axure RP 9 documents while preserving static page layout,
widget styling, text, and embedded resources. Interactions may be removed
during conversion.

> [!IMPORTANT]
> This project is **source-available for noncommercial use** under the
> [PolyForm Noncommercial License 1.0.0](LICENSE). It is not OSI-approved open
> source because commercial use is restricted. Commercial use requires
> separate written permission from the licensor.

## Download and quick start

The recommended distribution is the Windows x64 portable ZIP attached to the
latest GitHub Release.

1. Download `AxureDowngrade-<version>-windows-x64-portable.zip` and its
   `.sha256.txt` file
2. Verify the archive checksum
3. Extract the whole archive; do not move the executable away from `bin`
4. Run `AxureDowngrade.exe`
5. Select an Axure RP 11 `.rp` file
6. On first conversion, select the folder containing `AxureRP9.exe`

Runtime requirements:

- Windows 10 or Windows 11, x64
- Microsoft Edge WebView2 Runtime
- A lawfully installed copy of Axure RP 9

The source RP file is processed locally and is never overwritten by the app.

The project deliberately separates the conversion engine from the desktop UI:

- `crates/axure-core`: file probing, intermediate representation, downgrade
  rules, and eventually the RP 9 writer.
- `desktop`: Tauri 2 desktop application built with React and TypeScript.
- `fixtures`: paired minimal Axure 9 and Axure 11 research documents.

The Tauri-versus-Electron decision is recorded in
[`docs/FRAMEWORK_DECISION.md`](docs/FRAMEWORK_DECISION.md).
The complete Chinese technical design and downgrade pipeline are documented in
[`docs/AXURE_RP11_TO_RP9_DOWNGRADE_TECHNICAL_SPEC.md`](docs/AXURE_RP11_TO_RP9_DOWNGRADE_TECHNICAL_SPEC.md).

## Current status

The repository now emits editable RP 9 containers for the tested RP 11
documents. The writer rebuilds the outer LZ4 index, rewrites Page,
DesignDocument, and DocumentSettings packages through Axure RP 9's serializer,
removes interaction records and RP 11-only fields, and verifies every
rewritten package by loading it again with the RP 9 parser.

Current evidence includes:

- pixel-identical RP 9 rendering for the controlled rectangle fixture;
- four official Axure RP 11 training projects with 21 real pages and 34
  dynamic-panel state packages, converted and opened by the real Axure RP 9
  GUI;
- all five official Axure 11 widget libraries, adding 1,060 isolated widget
  pages and 84 panel-state packages;
- unchanged counts for every static record type across those projects; the
  only removed record types are interaction records;
- rendered RP 9 evidence for text, images, dynamic panels, form widgets,
  connectors, page hierarchy, shadows, and multi-column layouts.

This remains a research build: third-party/custom widget libraries, missing
fonts, and document-specific external resources may still need additional
fallback rules. The installed RP 11 build and all official Axure libraries
contain no Flex/Flexbox record or property; official RP 11 documentation
describes alignment inside pages and dynamic panels, not an automatic Flexbox
container.

## Development

Requirements:

- Node.js 20 or newer
- Rust stable with the MSVC toolchain
- Microsoft Edge WebView2
- Visual Studio Build Tools with the Desktop development with C++ workload

```powershell
cd desktop
npm ci
npm run tauri dev
```

Run the Rust core tests from the repository root:

```powershell
cargo test -p axure-core
```

Inspect one RP file as JSON:

```powershell
cargo run -p axure-lab -- inspect fixtures\axure9\00-empty.rp
```

Compare a controlled Axure 9/11 pair:

```powershell
cargo run -p axure-lab -- compare `
  fixtures\axure9\00-empty.rp `
  fixtures\axure11\00-empty.rp `
  --summary
```

The comparison report records hashes, embedded file signatures, printable
strings in ASCII and UTF-16LE, 4 KiB block entropy, common prefix/suffix
lengths, aligned similarity, and changed byte ranges. These are structural
clues, not proof of field meaning.

Inspect the independently compressed packages in an RP file:

```powershell
cargo run -p axure-lab -- inspect-packages fixtures\axure11\01-rectangle.rp
```

Current sample-backed findings are recorded in
[`docs/FORMAT_EVIDENCE.md`](docs/FORMAT_EVIDENCE.md).

Once a reader has produced the version-neutral document IR, staticize it with:

```powershell
cargo run -p axure-lab -- staticize document-ir.json > static-document.json
```

Staticization expands nested widgets to page-relative absolute coordinates,
flattens groups, components, repeaters, dynamic panels, and Flex containers,
removes non-visual hotspots, and reports every drop or substitution. It does
not silently accept non-finite or negative geometry.

## Build and run the RP9 bridge

The current Windows bridge uses the serialization and LZ4 assemblies from a
locally installed, licensed copy of Axure RP 9. Build it with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\build-bridge.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9
```

The desktop build bundles the resulting bridge and LZ4 dependencies. At
conversion time, choose the Axure RP 9 installation directory so the bridge
can load `AxureRP9.exe` and its model assemblies.

The bridge can also be tested directly:

```powershell
desktop\src-tauri\bin\AxureDowngradeBridge.exe `
  D:\ToolsWork\Axure9 `
  input-rp11.rp `
  output-rp9.rp
```

Run every RP11 fixture through the bridge and write a machine-readable report:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\verify-fixtures.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9
```

Outputs and `verification-report.json` are written under
`target\fixture-verification`.

Run the broader verification suite against the official Axure 11 training
projects:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\verify-complex-samples.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9 `
  -Axure11Directory D:\ToolsWork\Axure11
```

This suite currently covers 21 real pages and 34 attached panel-state packages.
It asserts that every record-count difference between the RP 11 source and
RP 9 output belongs to an interaction type. Its JSON report is written to
`target\complex-verification\complex-verification-report.json`.

Run the official widget-library suite:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\verify-library-samples.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9 `
  -Axure11Directory D:\ToolsWork\Axure11
```

The library suite adds 1,060 controlled widget pages covering repeaters,
tables and cells, menus, trees, list/combo boxes, text areas, inline frames,
screenshots, dynamic panels, grouped layers, flow widgets, and the 920-page
icon library.

Verify those outputs through the real Axure RP 9 GUI:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\verify-axure9-gui.ps1 `
  -Axure9Directory D:\ToolsWork\Axure9
```

The rectangle fixture proves container rebuilding, RP9 object-stream
rewriting, interaction-tree removal, preservation of `X`, `Y`, `Width`, and
`Height`, and successful loading in the Axure RP 9 GUI. The native RP9 and
downgraded screenshots are pixel-identical across the 721,188-pixel canvas
comparison region. The official complex samples additionally cover vector
shapes, image boxes, dynamic panels, text boxes, checkboxes, radio buttons,
connectors, layers, and image-map regions. Page counts are derived from exact
`Axure:Page` records; packages containing only `Axure:PageStyle` are reported
separately as panel-state/attached object packages.

## Portable Windows build

Build an optimized executable without an installer:

```powershell
cd desktop
npm run tauri -- build --no-bundle
```

Package the executable, bridge, notices and checksum in the same structure as
the GitHub Release:

```powershell
cd ..
tools\package-release.ps1 -Version 0.1.7 -Force
```

The runnable directory consists of:

```text
target\release\axure-downgrade-desktop.exe
target\release\bin\AxureDowngradeBridge.exe
target\release\bin\K4os.Compression.LZ4.dll
target\release\bin\K4os.Compression.LZ4.Legacy.dll
```

All four files must stay together with the `bin` directory in that layout.
The verified portable archive is written to
`artifacts\AxureDowngrade-0.1.7-windows-x64-portable.zip`.

转换失败时，界面会显示错误码、原因和完整详情。错误码说明见
[`docs/ERROR_CODES.md`](docs/ERROR_CODES.md)。

The bridge returns a JSON verification report containing rewritten page,
design-document and settings counts plus the number of removed interaction
records. The desktop checks those counts against the rebuilt RP container
before reporting success. Every rewritten object package is immediately loaded
again by Axure RP 9's parser; non-interaction object records, scalar properties,
and embedded byte-array hashes must match the pre-write snapshot. The app also
refuses to overwrite an output file that is currently held open by Axure.

## Release process

The repository includes a tag-driven GitHub Actions workflow. Before tagging,
make sure the version in `Cargo.toml`, `desktop/package.json` and
`desktop/src-tauri/tauri.conf.json` matches the tag.

```powershell
git tag v0.1.7
git push origin v0.1.7
```

The workflow tests the core, builds the Windows executable, packages the
portable archive, writes SHA-256 verification data and creates the GitHub
Release.

## License

Original Axure Downgrade code is licensed under the
[PolyForm Noncommercial License 1.0.0](LICENSE).

- personal, educational, charitable, public research and other noncommercial
  use is permitted under the license
- modification and redistribution are permitted only for noncommercial
  purposes and must preserve the license and required notice
- commercial use requires separate written permission from the licensor

See [NOTICE](NOTICE) for the required copyright notice and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for dependencies and
trademark notices. See [CONTRIBUTING.md](CONTRIBUTING.md) and
[SECURITY.md](SECURITY.md) before contributing or reporting a vulnerability.
