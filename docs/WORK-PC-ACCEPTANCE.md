# Work-PC development acceptance handoff

The Linux same-engine synthetic third-form milestone is accepted against product-source commit `7f1b19fd1526019d4589b0ea605e7eac83f21ad9`. This handoff is a prospective workstation check, not a Windows pass or a supported binary release. Documentation-only successors do not change the tested product identity.

## Prerequisites and source

- Use a local fixed-drive checkout and local fixed-NTFS output parents, with no symbolic-link, junction or reparse ancestry. Do not use network or removable drives. Keep real personal/business data outside this test checkout.
- Obtain the source from the reviewed development branch and record the exact checked-out commit. Do not merge or install a release as part of these checks.
- Have the .NET SDK **10.0.402** available through the selected `dotnet` executable. `global.json` disables SDK roll-forward. Check `dotnet --version` from the repository root; record `dotnet --info` and the CLI version/runtime. The accepted Linux run used .NET runtime 10.0.13; the work-PC runtime still needs qualification.
- Locked restore needs access to the configured official NuGet feed. The repository includes all ten project lockfiles and an inventory of 14 resolved packages in [notices/dependencies.json](../notices/dependencies.json). Direct PDF dependencies are iText 9.8.0 and its Bouncy Castle adapter 9.8.0; transitive identities and content hashes must remain locked. Do not regenerate locks to get past an error.
- The bundled Liberation Serif font and full notice are already included; no workstation font installation is needed. Python 3 is needed only for the migration comparison. PDF inspection/render tools and an interactive PDF viewer are separate test tools, not CLI runtime dependencies.

From an approved development directory, obtain a new checkout:

```powershell
git clone --branch development/reviewed-source-20261019 --single-branch https://github.com/DanielLemoine/pdf-form-filler.git
Set-Location pdf-form-filler
git rev-parse HEAD
dotnet --version
```

Follow the locked restore/build and individual test commands in the [README](../README.md#build-from-source). Stop on failure and retain the exact command, exit status and diagnostic output. A clean-room build of this curated layout has not yet been accepted. The Linux counts are not Windows expectations: the current Windows publication selection is 379 cases and remains unqualified. Prior focused Windows corrections are evidence-specific, not a general SDK or packaging workaround.

## Reproduce a fictional form, then review it

After a successful local build, make a new test directory and use fully qualified paths for the separate fixture host:

```powershell
$repo = (Get-Location).Path
$parent = Join-Path $repo 'artifacts'
if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent }
$case = Join-Path $parent 'work-pc-third-001'
New-Item -ItemType Directory -Path $case
$font = Join-Path $repo 'src\PdfForm.Pdf\Fonts\LiberationSerif-Regular.ttf'
$blank = Join-Path $case 'third-form.pdf'
dotnet tests/PdfForm.Cli.Tests/bin/Debug/net10.0/PdfForm.Cli.Tests.dll --test-only-third-fixture $font Blank $blank
```

Choose a new final directory for another attempt. The fixture host requires an existing parent and creates a new PDF; it does not overwrite. Check its exit status and compare the created file's length and SHA-256 with the receipt. Freeze and record the complete built CLI/dependency/runtime identity before this generator call if claiming a new unchanged-engine acceptance run; hashing only pdfform.dll is insufficient.

Use the [new-form commands](GENERIC-ONBOARDING.md) with this blank and fresh absolute output paths. The verified order is:

1. `check` (a compatible blank without configuration returns `configuration_needed`, exit 1), then `configure`.
2. Preserve the emitted draft. Review separate catalog/profile copies against the actual blank; choose semantic names, labels, required status and fictional sample hints. Never clear unresolved issues to force acceptance.
3. `save-config --reviewed`, then `check --catalog` (supported, exit 0).
4. `sample-input`, `validate --catalog --data --pdf`, editable `fill`, then `fill --flatten`.

Require successful receipts and exact output length/hash joins; preserve uncertain published artifacts instead of rerunning over them. Do not edit the saved package or frozen engine during the run. Configuration cannot add unsupported PDF features.

## Workstation checks still to complete

- Inspect every label, text value, empty field, checkbox state and any date layout in both PDFs. Compare output hashes to receipts; check editable canonical values/defaults/appearances and absence of flattened widgets/fields independently.
- In the intended interactive viewer, edit a fictional value, turn the checkbox off, save to a separate new file, close and reopen. Check visible text, checkbox state and retained values. Test reset behavior separately against the original blank defaults. Record viewer/version and outcomes; the Linux reset-default check was structural, not an interactive reset observation.
- Render and physically print a synthetic sample with the intended workstation/viewer/printer settings, checking scale, clipping, glyphs and checkbox visibility. Record those observations separately; no physical print has yet been observed.
- Preserve original generated files and record native publication results. A visual pass does not resolve pending Windows-native publication/recovery checks or filing readiness.

## Before a supported binary handoff

Still required: an accepted clean-room build; a selected and tested Windows architecture/runtime packaging approach (framework-dependent versus self-contained); a complete dependency/runtime/output manifest; preserved project, dependency and font notices; and accepted Windows-native, viewer and print checks for the intended use. The source contains no dependency DLLs, package cache, runtime installer or ready-to-install application. Review [third-party notices](../THIRD-PARTY-NOTICES.md), including the documented PlatformAbstractions provenance limitation, before binary distribution. Release, deployment and filing approval remain separate decisions.
