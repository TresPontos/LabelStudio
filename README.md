# QL-800 Hardware Spike

This repository currently contains only Milestone 0A: an Apache-2.0-licensed,
Windows x64 console spike for evaluating Brother QL-800 discovery, status,
driver printing, and official raster commands.

The current implementation is deliberately dry. It can enumerate printers and
generate/validate raster payloads, but it does not submit a RAW print job.

## Requirements

- Windows 10 or 11 x64
- .NET 10 SDK
- Official Brother QL-800 Windows driver for hardware discovery

## Safe Commands

```powershell
dotnet run --project src/Ql800Spike.Cli -- list-media
dotnet run --project src/Ql800Spike.Cli -- generate --test geometry-60 --media brother.dk-22251
dotnet test Ql800Spike.slnx
```

Printer discovery is read-only but accesses the local Windows spooler:

```powershell
dotnet run --project src/Ql800Spike.Cli -- discover
```

No command currently sends printer data.
