# LabelStudio

A Windows desktop label editor for Brother QL-800 thermal printers. Design labels
with text, rectangles, lines, and images on a Skia-rendered canvas, preview the
thermal raster output, and print directly to the printer via the Windows spooler.

## Status

**Alpha** — this is early-stage software under active development. Core editing,
rendering, and printing pipelines work, but some interactions (resize handles,
rotation) are still being refined. Physical printing requires a connected
Brother QL-800 with the official Windows driver installed.

## Features

- **Canvas editor** with SkiaSharp rendering, zoom, pan, and 0/90/180/270° view rotation
- **Element types**: text (with inline editing), rectangles, lines, images
- **Multi-selection** with combined bounds, group/ungroup, Z-order, and layers panel
- **Transforms**: drag, directional resize, text rotation, snap-to-grid, and ruler guides
- **Undo/redo** with full command history
- **Clipboard**: copy, paste, and duplicate with cross-document asset transfer
- **Document persistence**: JSON serialization with schema migration (v1→v4)
- **Thermal preview**: monochrome + red plane raster preview before printing
- **Brother QL-800 printing**: RAW spooler transport with raster encoding and validation
- **Media support**: DK-22251 (62 mm continuous) and DK-11204 (die-cut)
- **Preflight checks**: validates document geometry before printing

## Requirements

- Windows 10 or 11 x64
- .NET 10 SDK
- Brother QL-800 Windows driver (for physical printing)

## Build

```powershell
dotnet build LabelStudio.slnx --configuration Release
dotnet test LabelStudio.slnx --configuration Release
```

## Run the latest validated desktop app

Always launch the app with `run-latest.cmd` from the repository root. It checks
whether the source changed since the latest successful build, runs restore and
tests when needed, and launches the self-contained publish at
`artifacts/latest/LabelStudio.Desktop.exe`. The status bar shows the app version
and Git commit; click it for full build details.

To explicitly test and publish changes before launching:

```cmd
build-latest.cmd
```

This replaces the canonical latest folder only after tests and Release publish
succeed. Do not launch files under `bin\`, `LabelStudioApp\`, or temporary
staging folders directly; see [docs/running-latest.md](docs/running-latest.md).

## QL-800 hardware spike

The repository also includes `Ql800Spike`, a console tool for evaluating Brother
QL-800 discovery, status, and raster command generation:

```powershell
dotnet run --project src/Ql800Spike.Cli -- list-media
dotnet run --project src/Ql800Spike.Cli -- generate --test geometry-60 --media brother.dk-22251
dotnet run --project src/Ql800Spike.Cli -- discover
```

## Architecture

| Project | Responsibility |
|---|---|
| `LabelStudio.Document` | Document model, elements, units, geometry |
| `LabelStudio.Layout` | Layout engine and prepared scene |
| `LabelStudio.Rendering` | Monochrome raster, thermal target renderer, text layout |
| `LabelStudio.Editor` | Canvas transform, selection, commands, hit testing, snapping |
| `LabelStudio.Printing` | Print abstractions, preflight, mock backend |
| `LabelStudio.Printing.BrotherQl` | QL raster encoder, validator, media mappings |
| `LabelStudio.Printing.Windows` | Win32 spooler RAW transport |
| `LabelStudio.Storage` | JSON serialization and schema migration |
| `LabelStudio.Desktop` | WPF desktop application (canvas, inspector, layers) |
| `Ql800Spike.Core` | Hardware spike: discovery, status, raster protocol |
| `Ql800Spike.Cli` | Command-line tool for printer evaluation |

## License

Apache License 2.0. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
