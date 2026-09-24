# Running the latest build

To run LabelStudio, always launch:

    run-latest.cmd        (repo root)

That is the single entry point. It compares the current source fingerprint to
the last successful build. When anything in the project has changed, it
automatically runs restore, tests, and Release publish first, then opens
`artifacts\latest\LabelStudio.Desktop.exe`. When source is unchanged, it opens
the existing build immediately. It prints the version and build time it opens.
This also catches XAML and toolbar layout changes before launch.

## Building a new latest

To explicitly run the build pipeline without launching the app, run:

    build-latest.cmd      (repo root)

This runs the full pipeline: restore, all tests, Release publish, and only then
replaces `artifacts\latest`. `run-latest.cmd` invokes the same pipeline
automatically whenever it detects changed source. If any step fails, the
previous known-good build in `artifacts\latest` is left untouched and the app
is not launched. Close the latest app before launching after source changes so
Windows can replace its running executable.

The app shows version, commit, dirty state, and build time in its title and
status bar. Click the status-bar version for full build information. A
`build-info.json` manifest beside the executable includes the source fingerprint
and test count.

## Do not launch these directly

- `src\LabelStudio.Desktop\bin\Debug\...` and `bin\Release\...` are intermediate
  build outputs and may be stale or partial.
- `artifacts\latest-staging` and `artifacts\latest-previous` are transient
  folders used during a build; they may be incomplete.
- `LabelStudioApp\` is a legacy copy from an old manual publish; it is never
  updated by `build-latest.cmd`. Do not use it.
