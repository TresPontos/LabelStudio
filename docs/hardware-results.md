# Milestone 0A Hardware Results

## Environment

- Printer: Brother QL-800
- Connection: USB001
- PnP instance: `USBPRINT\BROTHERQL-800\6&330D29E5&0&USB001`
- Driver: Brother QL-800, file version `3.0 built by: WinDDK`
- Queue processor/datatype: `winprint` / `RAW`
- Driver resolutions: 300 x 300 and 300 x 600 dpi

## Discovery With DK-22251

- PnP correlation was high and unambiguous with one QL-800 attached.
- `DC_MEDIAREADY` returned `1.1" x 3.5"` while DK-22251 was installed.
- Therefore `DC_MEDIAREADY` reflects driver/default state and is not accepted as
  reliable automatic roll detection.
- The Brother driver notification UI independently identified `62mm Continuous
  Length Tape` after a failed print.

## RAW Status Request

- Request bytes: `1B 69 53` (`ESC i S`).
- Spooler accepted all three bytes as job 3.
- `ReadPrinter` failed with Win32 error 6 (`ERROR_INVALID_HANDLE`).
- The job drained and left the queue empty.
- Conclusion: this Brother USB port-monitor path accepts RAW output but does not
  expose Brother protocol responses through `ReadPrinter` on the queue handle.

## RAW Canary Attempts

### Jobs 4 and 5: Incorrect Monochrome Framing

- Payload: 30,110 bytes, monochrome `67 00 5A` rows, expanded mode `0x08`.
- Job 4 was submitted while the printer was off and is not evidence about the
  payload.
- Job 5 reached the powered printer and produced `Error of unknown cause`.
- No automatic retry occurred.
- Finding: DK-22251 requires two-colour raster framing even for black-only art.

### Job 6: Two-Colour Framing, Black Art Only

- Payload: 59,777 bytes.
- Expanded mode: `0x09` (two-colour plus cut-at-end).
- Each row contained a populated `77 01 5A` black plane and blank
  `77 02 5A` red plane.
- Spooler accepted the payload in one write; the job disappeared without an
  error state; queue returned to empty and printer status returned to normal.
- Physical result: label printed and said `CANARY-30`; appearance was described
  as burgundy.
- Physical result: artwork was mirrored on the horizontal axis.
- Physical result: total cut length was approximately 40 mm, not the requested
  30 mm.
- A reference line was approximately 35 mm from one edge and 5 mm from the
  other.

## Current Conclusions

- QL raster plus Windows RAW spooler is viable for output.
- DK-22251 must use two-plane framing even when only black artwork is requested.
- Raster rows require a horizontal transformation before submission.
- Continuous finished-length planning must include a 35-dot margin on both the
  leading and trailing edge.
- Spooler job disappearance is useful transport evidence but is not proof of
  physical completion.

### Job 7: Horizontal Transform

- Added a horizontal transformation within the media's printable head region.
- The RAW job completed and disappeared without an error state.
- Physical result: `CANARY-30` was readable in the correct orientation.
- Physical result: artwork was still described as dark burgundy rather than
  unambiguously black.
- Conclusion: horizontal protocol orientation is resolved. Output-plane colour
  requires a dedicated black/red separation test.

### Job 8: Output-Plane Separation

- Printed distinct artwork through the high-energy and low-energy planes.
- Physical result: the high-energy `BLACK` region appeared dark burgundy.
- Physical result: the low-energy `RED` region appeared red.
- Physical result: both regions had correct horizontal orientation.
- Conclusion: plane order is confirmed as black/high-energy first and
  red/low-energy second. The dark-burgundy appearance is the observed black
  output on this DK-22251 roll and is not caused by swapped channels.

### Jobs 9 and 10: Continuous-Length Calibration

- Job 9 requested 60 mm using 674 raster rows minus one assumed margin.
- Measured edge-to-edge length: approximately 63 mm.
- Measured distance between the two printed reference lines: 55 mm.
- Job 10 requested 100 mm using 1,146 raster rows minus one assumed margin.
- Measured edge-to-edge length: approximately 104 mm.
- Measured distance between the two printed reference lines: 95 mm.
- The line-to-line measurements match the raster geometry to normal ruler
  precision.
- Conclusion: the 35-dot feed margin contributes at both the leading and
  trailing edges. Finished continuous length must be planned as
  `rasterRows + 2 * feedMarginDots`.

### Job 11: Corrected 60 mm Length

- Planner used 639 raster rows plus two 35-dot feed margins.
- RAW payload contained 119,297 bytes and passed dry validation.
- The job completed without a spooler error and left the queue empty.
- Measured edge-to-edge cut length: approximately 60 mm.
- Conclusion: the two-margin continuous-length formula is validated at
  300 x 300 dpi on DK-22251.

## Media Switch To DK-11204

- Replaced DK-22251 with DK-11204 while keeping the same powered USB printer
  and Windows queue.
- Queue and PnP identity remained stable on `USB001`.
- `DC_MEDIAREADY` continued to report `1.1" x 3.5"` rather than the installed
  17 x 54 mm roll.
- Conclusion: standard Windows capability queries neither detect this roll nor
  expose a usable media-change notification. Manual selection plus protocol
  validation is required unless another Brother status channel is found.

### Job 12: First DK-11204 Attempt (Failed)

- Sent 566 rows, 53,081 bytes with the within-printable-area horizontal mirror.
- Spooler accepted all bytes and the job left the queue, but nothing physical
  printed.
- Root cause: `TransformPhysicalRowToProtocol` mirrored only within the
  printable area (dots 555..719 to 555..719), leaving data at the wrong head
  position for die-cut media. The Brother protocol requires a full 720-dot row
  mirror: physical dot x maps to protocol dot 719 - x.

### Job 13: Corrected DK-11204 Placement (Success)

- Fixed `TransformPhysicalRowToProtocol` to mirror the entire 720-dot row.
- Updated `QlRasterJobValidator.ValidatePrintableHeadArea` to check protocol
  coordinates (mirrored printable range).
- Sent the same 566 rows, 53,081 bytes with the corrected transform.
- The label printed successfully with artwork fully within the 17 x 54 mm
  die-cut area and correct orientation.
- Conclusion: the full-row horizontal mirror is the correct protocol transform
  for all media types. DK-11204 die-cut placement is validated.
