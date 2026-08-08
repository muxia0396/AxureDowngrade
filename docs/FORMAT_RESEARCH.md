# Axure RP format research protocol

The downgrade writer must be derived from controlled evidence. Descriptions of
the `.rp` format found online are hypotheses until confirmed by byte-level
comparison and by Axure RP 9 successfully opening generated output.

## Minimal paired fixtures

Create every fixture in Axure RP 9 first. Open a copy in Axure RP 11 and save it
without unrelated edits. Keep one changed property per fixture.

| Fixture | Axure 9 document | Axure 11 document | Property isolated |
| --- | --- | --- | --- |
| `00-empty` | one blank page | open and save | container and version metadata |
| `01-rectangle` | one rectangle | open and save | base widget structure |
| `02-position` | rectangle at x=13, y=17 | open and save | coordinates |
| `03-size` | 101 × 37 rectangle | open and save | dimensions |
| `04-text` | text with ASCII and CJK | open and save | text encoding |
| `05-style` | fill, border, radius | open and save | style records |
| `06-image-png` | one known PNG | open and save | resource table |
| `07-group` | two grouped widgets | open and save | hierarchy |
| `08-component` | one component instance | open and save | component references |
| `09-flex` | not available | create in 11 | layout baking input |

Do not include interactions in the first fixture set.

## Evidence captured per fixture

1. File length and first 256 bytes.
2. Container signature and printable-byte ratio.
3. Strings mentioning Axure or a version.
4. Hashes and decompression candidates for embedded sections.
5. Structural diff against the immediately preceding fixture.
6. Screenshot of the page in the authoring version.
7. Result of opening any generated output in Axure RP 9.

Generate the machine-readable report with:

```powershell
cargo run -p axure-lab -- compare `
  fixtures\axure9\00-empty.rp `
  fixtures\axure11\00-empty.rp
```

For a compact terminal report, append `--summary`. The full report is JSON and
can be retained beside screenshots and validation notes. Fixture `.rp` files
are ignored by Git by default to reduce the chance of committing customer data.

## Acceptance gate for the first writer

The writer is not considered implemented until Axure RP 9 opens its output and
the following values match the fixture: page count, widget count, z-order,
absolute bounds, text, fill, border, and image pixels.

## Staticization invariant

The Axure 11 reader must emit nested bounds relative to each immediate parent.
Before RP 9 serialization, run `staticize_document` or the CLI equivalent:

```powershell
cargo run -p axure-lab -- staticize document-ir.json
```

The resulting page widget lists are flat and page-relative. Their vector order
is the intended back-to-front z-order. Any `RotatedContainerApproximation`
issue blocks a pixel-fidelity claim until the source transform origin has been
identified from fixtures.
