# Milestone 0A Test Procedure

This document records the approved order for the QL-800 hardware spike. The
current build implements only environment capture, read-only discovery, media
profiles, deterministic test rasters, dry raster encoding, and status-packet
decoding. It does not submit print data.

## Safety Rules

- Use one QL-800 queue and stop other applications that may print.
- Disable automatic retries.
- Confirm the Windows queue is empty before every physical test.
- Use a unique test/run identifier for every submitted job.
- Never retry a job whose physical outcome is uncertain.
- Keep generated payloads and logs with the corresponding physical labels.

## Stage 1: Read-Only Discovery

Install DK-22251, close the printer cover, turn the QL-800 on, connect it by
USB, and make sure Editor Lite mode is off.

```powershell
dotnet run --project src/Ql800Spike.Cli -- discover --output artifacts/discovery-dk22251
```

Expected behavior:

- No label feeds, prints, or cuts.
- The QL-800 queue appears with its Brother driver and USB port.
- PnP information identifies QL-800 or Brother VID/PID evidence.
- `capabilities.json` records driver-advertised resolution and media data.

Return the console output plus these files:

```text
artifacts/discovery-dk22251/environment.json
artifacts/discovery-dk22251/discovery.json
artifacts/discovery-dk22251/capabilities.json
```

## Stage 2: Media Switch Discovery

This stage will be finalized after Stage 1 establishes which read-side status
sources are usable. The intended sequence is DK-22251, DK-11204, then DK-22251,
without restarting the spike unless required by an observed stale handle.

## Stage 3: Driver Canary

Not implemented yet. Driver submission will only be added after discovery
results are reviewed.

## Stage 4: RAW Canary

The first RAW submission is restricted to the `canary-30` pattern and requires
an explicit physical-print confirmation flag. With DK-22251 installed, it is
expected to print one monochrome label approximately 30 mm long and cut once.

```powershell
dotnet run --project src/Ql800Spike.Cli --configuration Release --no-build -- print-raw-canary --printer "Brother QL-800" --confirm-physical-print --output artifacts/raw-canary-dk22251
```

The command validates the payload again immediately before submission, refuses
to run when the queue is non-empty, executes in a bounded child process, and
never retries automatically.
