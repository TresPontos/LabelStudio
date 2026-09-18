# ADR-0001: QL-800 Printing Backend

- Status: Accepted
- Decision date: 2026-09-18

## Context

Milestone 0A compared driver-rendered GDI submission with an
application-owned QL raster encoder submitted as a RAW Windows spooler
job. Both paths were evaluated against physical hardware evidence on a
Brother QL-800 using DK-22251 (62 mm continuous black/red) and DK-11204
(17 x 54 mm die-cut black) media across 13 controlled print jobs.

## Decision

The Brother QL raster protocol is the primary QL-800 backend.

The Windows printer queue/spooler remains the transport.

The production pipeline is:

```text
Label / PrintIntent
        ->
Device-neutral rendering
        ->
QL target rendering
        ->
Brother QL Raster Encoder
        ->
Windows RAW Spooler Transport
        ->
Brother Windows printer queue / USB stack
        ->
QL-800
```

We are NOT switching to direct USB.

We are NOT using b-PAC as the core architecture.

The driver-rendered path may remain useful as a fallback, diagnostic
comparison, or compatibility path, but it does not dictate the document
model or primary rendering architecture.

## Hard Gates (All Passed)

- Physical dimensions remain within approved engineering tolerances.
- Continuous (DK-22251) and die-cut (DK-11204) media print correctly.
- Required cutter behavior is deterministic.
- Normal operation produces no unexplained missing or duplicate labels.
- Ordinary media and connection faults have bounded recovery procedures.
- The route does not depend on undocumented private data.
- Dependencies remain compatible with Apache-2.0 distribution.

## Evidence

### Jobs 3-4: Read-Only Discovery

- PnP: `USBPRINT\BROTHERQL-800\6&330D29E5&0&USB001`
- Driver: `Brother QL-800`, `3.0 built by: WinDDK`
- Queue processor/datatype: `winprint` / `RAW`
- Resolutions: 300x300, 300x600

### Jobs 5-8: DK-22251 Protocol Validation

- Monochrome framing (`67 00 5A`) causes "Error of unknown cause" on
  DK-22251. Two-colour framing (`77 01 5A` black + `77 02 5A` red) is
  required even for black-only artwork.
- Raster rows require horizontal mirroring for correct orientation.
- Plane order confirmed: black/high-energy first, red/low-energy second.
- Black output on DK-22251 appears dark burgundy; this is the media
  characteristic, not a channel swap.

### Jobs 9-11: Continuous Length Validation

- Continuous finished length = `rasterRows + 2 * feedMarginDots`
  (35-dot margin at both leading and trailing edges).
- Validated at 60 mm (639 rows) and 100 mm (1111 rows).

### Jobs 12-13: DK-11204 Die-Cut Validation

- Job 12 failed: mirroring only within the printable area placed data at
  the wrong head position. Nothing printed.
- Job 13 succeeded after fixing the transform to mirror the entire
  720-dot row: `physical x -> protocol 719 - x`.
- DK-11204 die-cut placement validated with correct orientation and
  artwork fully within the 17 x 54 mm area.

### Known Limitations

- `DC_MEDIAREADY` is unreliable; it reports `1.1" x 3.5"` regardless of
  installed roll. Manual media selection is required.
- Brother protocol status via `ReadPrinter` fails with
  `ERROR_INVALID_HANDLE (6)`. The Brother USB port monitor does not
  expose protocol responses on the spooler handle.
- No automatic retry is permitted after ambiguous print outcomes.

## Weighted Evaluation

| Category | Weight | Driver | RAW | Evidence |
|---|---:|---:|---:|---|
| Physical accuracy and repeatability | 25 | Medium | High | Jobs 9-13: deterministic length, placement, orientation |
| Reliability and job boundaries | 20 | Medium | High | All 13 jobs had clear spooler lifecycle; queue empty after each |
| Fault recovery and duplicate containment | 15 | Low | High | No retries; bounded monitoring; explicit confirmation required |
| Cutter and media control | 10 | Low | High | Cut-at-end flag, feed margin, auto-cut all deterministic |
| Media detection and status quality | 10 | Low | Low | DC_MEDIAREADY unreliable; ReadPrinter fails for both paths |
| Diagnostic transparency | 5 | Low | High | Full payload validation, artifact preservation, dry-run checks |
| Deployment requirements | 5 | Low | Medium | No extra drivers; uses existing Windows queue and RAW datatype |
| Implementation and maintenance complexity | 5 | Low | Medium | Protocol encoding is self-contained; no b-PAC dependency |
| Future black/red suitability | 3 | Low | High | Two-colour planes validated on hardware |
| Future background-process suitability | 2 | Low | Medium | No UI dependency in protocol layer |

## Consequences

- The document model is printer-independent; only the target renderer
  and backend know about Brother protocol details.
- The full 720-dot horizontal mirror is a protocol-level invariant, not
  a rendering-level one.
- Media selection is manual; the system cannot auto-detect installed
  rolls via Windows capabilities.
- Status readback requires a different channel than the Windows spooler
  ReadPrinter path; this is deferred to future investigation.
- The spike project is preserved as a hardware reference and regression
  utility; production code is developed separately.