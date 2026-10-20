# PDF Form Filler

An offline .NET command-line tool in development for the exact reviewed Form 941 (March 2026) and Schedule B (March 2024) PDF templates. It validates supplied values and creates new editable or flattened PDFs. It does not calculate tax or file returns.

**Development checkpoint:** The earlier embedded-font two-form cut passed Linux regression and selected Poppler/MuPDF visual checks. This draft adds an external, hash-bound catalog/profile workflow for supported AcroForms. Its source review and combined synthetic Linux regression checkpoint have passed. A separate synthetic non-tax third form has now passed configuration-only onboarding, editable/flattened structural checks and selected Linux visual review with the engine unchanged. Windows, interactive-viewer and physical-print qualification remain open. See [development status](docs/DEVELOPMENT-STATUS.md).

## Adding a form by configuration

The draft now includes `check`, `configure`, operator review, `save-config`, `sample-input`, and catalog-based validation/filling. A saved package binds exact template and profile bytes to implemented font/layout/checkbox policies; it does not add a form identity to the compiled registry. The two bundled IRS profiles remain available through their existing `--profile` path.

See the [configuration workflow](docs/GENERIC-ONBOARDING.md). Configuration cannot implement unsupported PDF features or waive independent inspection, glyph, fit, appearance or publication checks. The accepted synthetic third-form workflow demonstrates this configuration-only path; it does not qualify arbitrary forms or unsupported features. For the next workstation checks, use the [work-PC acceptance handoff](docs/WORK-PC-ACCEPTANCE.md).

## Build from source

Install the .NET SDK version pinned in `global.json` (10.0.402). All projects target .NET 10. Package versions and all ten lockfiles are included. From the repository root, restore in locked mode from the public NuGet feed, then build:

```sh
dotnet restore PdfForm.slnx --locked-mode --configfile NuGet.Config --disable-parallel -m:1 -nr:false -p:BuildInParallel=false
dotnet build PdfForm.slnx --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:BuildInParallel=false -p:UseSharedCompilation=false
```

This repository contains source, not a runtime or binary distribution. Restore needs network access to the configured official feed; the application itself works offline. Do not silently regenerate lockfiles on a locked-restore error. A clean-room restore/build of this curated layout remains pending. The combined generic Linux regression checkpoint is described below. The retained two-form checkpoint applies to its earlier exact source cut.

On Bash, `tools/dotnet-env.sh` can isolate writable SDK/NuGet state under `.runtime`. Set `PDFFORM_DOTNET_ROOT` to an existing SDK directory, source the file from the repository root, and invoke `"$PDFFORM_DOTNET_ROOT/dotnet"` explicitly. Setting DOTNET_ROOT does not choose an executable from PATH. Also set `XDG_STATE_HOME="$PWD/.runtime/state"` and `NUGET_PLUGINS_CACHE_PATH="$PWD/.runtime/nuget-plugins"` if those variables are present in your environment. No SDK download is performed by the helper.

## CLI basics

After the Debug build, run the managed CLI through the installed .NET host:

```sh
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll --help
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll --version
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll validate --profile profiles/irs-941-2026-03.json --data samples/941-2026q3-monthly-final-refund.json --pdf fixtures/appearance/forms/f941.pdf
```

For development-only create-new filling, create a writable output parent first and choose a destination that does not already exist:

```sh
mkdir -p artifacts/demo
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll fill fixtures/appearance/forms/f941.pdf --profile profiles/irs-941-2026-03.json --data samples/941-2026q3-monthly-final-refund.json --out artifacts/demo/form-941-flat.pdf --flatten
```

Omit `--flatten` to request editable output. Relative paths resolve from the process working directory, not from the DLL directory. On Windows, inputs must be on local fixed drives; output parents must be on local fixed NTFS, with no symbolic-link/junction/reparse ancestors. Network and removable-drive inputs are unsupported by this bounded implementation. Linux is used for development tests. macOS publication is unsupported.

There is no overwrite switch: `--force` is unqualified and refused. `run` and diagnostic file-output options (`--result`, `--json`, `--md`) remain disabled. A successful validation does not verify rendered text fit. A successful fill is not filing or print approval.

Ordinary diagnostics are value-free JSON on stdout. A fill result's `document.pdf` path is a logical privacy-preserving label; the destination is exactly the caller's `--out` path. Independently compare the destination's length/SHA-256 with its descriptor. An error exit does not prove that no file was created: a postcommit diagnostic failure can leave the PDF intact and report `E_DIAGNOSTIC_WRITE artifactState=published`. Preserve the destination and inspect it after confirming process settlement; do not assume rollback or blindly rerun.

The examples are independent fictional form demonstrations, not a reconciled two-form return. Keep real data outside the repository. Dates are ISO JSON values such as `2026-09-25`; the Form 941 date cells display `09/25/2026`. Schedule B uses the bundled, embedded Liberation Serif font at fixed 10 pt for printable ASCII (U+0020–U+007E) plus É. Unsupported Unicode and text that does not fit are rejected; no automatic fallback or shrink-to-fit is used. Filling reports `W_FONT_EXPLICIT_REPLACEMENT`.

## Tests

From the repository root after building, this Bash example supplies an absolute, existing publication fixture parent and a fresh renderer output directory:

```sh
repo="$(pwd -P)"
mkdir -p "$repo/artifacts/publication-fixtures"
dotnet tests/PdfForm.Core.Tests/bin/Debug/net10.0/PdfForm.Core.Tests.dll "$repo"
dotnet tests/PdfForm.Pdf.Tests/bin/Debug/net10.0/PdfForm.Pdf.Tests.dll "$repo"
dotnet tests/PdfForm.Publication.Tests/bin/Debug/net10.0/PdfForm.Publication.Tests.dll "$repo/artifacts/publication-fixtures"
dotnet tests/PdfForm.VerifiedFill.IntegrationTests/bin/Debug/net10.0/PdfForm.VerifiedFill.IntegrationTests.dll "$repo" "$repo/artifacts/verified-fill-001"
dotnet tests/PdfForm.Cli.Tests/bin/Debug/net10.0/PdfForm.Cli.Tests.dll "$repo"
python3 tools/check_migration.py
```

Use a new final renderer directory for each attempt. The publication harness accepts exactly one existing fully qualified parent, creates its own unique attempt, and emits a terminal receipt. An exit code alone does not replace reviewing that receipt. Windows test paths should be fully qualified and meet the local fixed-NTFS/no-reparse contract. The old convenience shell wrapper is intentionally omitted because its root-admission behavior is stale.

The preserved embedded-font checkpoint passed Core 142, inspector 147, renderer 149, CLI 45 and nine migration comparisons. Its separate active publication result was 244 Linux cases. The corrected generic source has accepted synthetic Linux results of Core 202, inspector 551, renderer 149, CLI 91, publication 313 and nine migration comparisons. These results combine distinct accepted runs using identical product source, with a test-only nullable-memory fixture correction in the final publication run; they are not a single monolithic run. The 379-case Windows publication selection remains prospective. See [development status](docs/DEVELOPMENT-STATUS.md) for the current boundary.

The [historical golden publication overlay](supplemental/publication-golden/README.md) still targets its exact earlier commit and does not apply to the current generic harness. The [supplemental onboarding guide assertions](supplemental/generic-onboarding-guide/README.md) are separate test-side source with external Windows integration dependencies; they are not part of the solution's ordinary harness counts.

## License and third-party material

Project-authored source is licensed under GNU Affero General Public License version 3 (`AGPL-3.0-only`); see [LICENSE](LICENSE). Third-party code, package notices and the blank IRS templates retain their own applicable terms; this project license does not relicense them. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

This is a development source repository, not a release-qualified product. Read the license before distributing a modified/binary version or providing a network service. No warranty, filing advice or regulatory acceptance is supplied.
