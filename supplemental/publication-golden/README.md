# Supplemental publication golden-test source

This directory supplies corresponding source for the separately built golden publication test harness. It contains the two reviewed test leaves and four frozen, independently authored synthetic fixture documents. The artifact payloads are 32-byte synthetic sequences, not real PDFs or taxpayer data. The helper embeds exact fixture bytes and independent expected hashes; the four raw files preserve the external oracle for inspection.

**This is not merged into the main solution source.** The main publication harness retains 244 observed Linux cases and 310 prospective Windows cases. Applying this overlay adds 10 common tests: external-package recovery plus under/exact/over actual-total, result-cap and completion-cap controls. The separate overlay run observed 254 passing Linux cases. Its 320-case Windows selection was prospective at this source handoff; no Windows pass or new whole-CLI build is claimed here.

## Exact rebuild in a separate checkout

Use a separate clean checkout of the same source commit, not the main checkout whose results you intend to preserve. From its repository root, compare the 19 preimage inputs in `BASE-SHA256SUMS` first:

```sh
sha256sum -c supplemental/publication-golden/BASE-SHA256SUMS
cp supplemental/publication-golden/source/tests/PdfForm.Publication.Tests/Program.cs tests/PdfForm.Publication.Tests/Program.cs
cp supplemental/publication-golden/source/tests/PdfForm.Publication.Tests/PublicationGoldenRecoveryTests.cs tests/PdfForm.Publication.Tests/PublicationGoldenRecoveryTests.cs
dotnet restore tests/PdfForm.Publication.Tests/PdfForm.Publication.Tests.csproj --locked-mode --configfile NuGet.Core.Config --disable-parallel -m:1 -nr:false -p:BuildInParallel=false
dotnet build tests/PdfForm.Publication.Tests/PdfForm.Publication.Tests.csproj --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:BuildInParallel=false -p:UseSharedCompilation=false
repo="$(pwd -P)"
mkdir -p "$repo/artifacts/publication-golden-fixtures"
dotnet tests/PdfForm.Publication.Tests/bin/Debug/net10.0/PdfForm.Publication.Tests.dll "$repo/artifacts/publication-golden-fixtures"
```

On Windows, verify the same SHA-256 pins with `Get-FileHash`, use `Copy-Item` for the two copies, and pass an existing absolute local fixed-NTFS fixture parent with no reparse ancestry. The project reference points to the unchanged `src/PdfForm.Publication` project; no other product source is replaced. `NuGet.Core.Config` has no package feed and this two-project closure contains no package-type dependencies. The pinned SDK must already provide its .NET reference packs.

`SOURCE-PINS.json` records all 20 source/config/lock inputs after applying the overlay, plus exact fixture hashes and ordered-selection digests. The raw fixture documents are not required at runtime because the helper embeds their byte-identical data. A new build naturally generates its own restore metadata; none from the original build is distributed here. Byte-identical binaries are not promised across build paths. Keep this test runtime separate from the CLI runtime; never replace the CLI's Publication.dll with the relocated test build's copy.

Read the harness's bootstrap and terminal receipt, reconcile exact selected/result identities and counts, and retain failure output. Exit 0 alone is not sufficient evidence. These tests exercise bounded structural recovery and read limits, not persistent authenticity, rendering, hostile filesystem-race qualification or filing readiness.
