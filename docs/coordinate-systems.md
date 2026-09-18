# Coordinate Systems

LabelStudio uses four distinct coordinate systems. They must never be
conflated.

## 1. Document Coordinates

The coordinate space of the document model. Units are integer
micrometres. Origin is top-left, X increases rightward, Y increases
downward.

```text
(0, 0) = top-left corner of the label page
```

Document coordinates are printer-independent. They do not know about
dots, heads, or protocol positions.

## 2. Physical Label Coordinates

Same axis convention as document coordinates (top-left origin, right/down
positive) but expressed in device dots at a specific DPI. These are
produced by the target renderer when converting micrometres to dots.

```text
physicalX = MicrometresToDots(documentX, dpi)
physicalY = MicrometresToDots(documentY, dpi)
```

For the QL-800 at 300 DPI: 1 mm = 1000 um = ~11.81 dots.

## 3. Physical Printer-Head Coordinates

The physical print head has a fixed number of elements. For the QL-800
the head is 720 dots wide. Physical head coordinates are 0..719, left to
right, matching the actual thermal element positions.

The label may not fill the entire head. For DK-22251 (62 mm), the
printable area starts at dot 12 and spans 696 dots. For DK-11204
(17 mm), the printable area starts at dot 555 and spans 165 dots.

```text
headX = physicalLabelX + headLeftBlankDots
```

## 4. Protocol Raster Coordinates

The Brother QL raster protocol transmits rows right-to-left relative to
the physical head. Protocol dot 0 corresponds to head dot 719, and
protocol dot 719 corresponds to head dot 0.

```text
protocolX = 719 - headX
protocolX = 719 - physicalLabelX - headLeftBlankDots
```

This full-row mirror is applied by the QL raster encoder after
rendering. It is NOT applied by the generic renderer.

## Regression Warning

The die-cut placement bug in Milestone 0A Job 12 was caused by
mirroring only within the printable area instead of the full 720-dot
row. The within-area mirror is identity for centered media but places
data at the wrong head position for offset media like DK-11204.

The fix: always mirror the entire physical head width.
```text
protocolX = HeadWidthDots - 1 - physicalX
```
Never mirror within only the printable sub-range.