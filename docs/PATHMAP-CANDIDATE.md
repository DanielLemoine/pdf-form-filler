# Path-normalized Linux development candidate

This records the completed `pathmap-candidate001` development handoff. It is not a release or a Windows-qualified application. The binary archive is not stored in this repository; ordinary source review and testing can use the exact commit below without a transfer ZIP.

## Exact inputs and archive

- Product source: [`7f1b19fd1526019d4589b0ea605e7eac83f21ad9`](https://github.com/DanielLemoine/pdf-form-filler/tree/7f1b19fd1526019d4589b0ea605e7eac83f21ad9).
- Source checkout used for this build: [`dfc3c1f03eff0a7896e72ce70563846fe0690847`](https://github.com/DanielLemoine/pdf-form-filler/tree/dfc3c1f03eff0a7896e72ce70563846fe0690847), the documentation-only successor; 214 source files, with no product/project/lockfile changes.
- SDK: 10.0.402; reference and qualification runtime: .NET 10.0.13.
- Archive: `pdf-form-cli-7f1b19f-pathmap-candidate001.zip`; 6,667,809 bytes; 93 regular files; SHA-256 `f307d03ddde666fa5842c6096cacb33fa6ffedb159b6539a42d6b0888d2c6222`.
- Payload: 32 runtime files, including 29 DLLs; 25 third-party DLLs are unchanged. The archive also includes exact template/profile/sample inputs, notices, source and package inventories, and checksums. It omits the SDK, .NET runtime, apphost, PDBs and fixture test runners.

The four new project DLL identities are:

| Assembly | Bytes | SHA-256 |
| --- | ---: | --- |
| PdfForm.Core.dll | 180224 | `28fa1fe91c887d1bde31575a15b637d49d295951ab8154c93607ec6884ace309` |
| PdfForm.Pdf.dll | 664064 | `394140879fee81c87a5b09006b9fb474f6ac5f2c9cae531e587c6b0663475f33` |
| PdfForm.Publication.dll | 89088 | `b6cfca739120f37fc902b9661aa23e9c983d073d8c182326c78f50246efcc204` |
| pdfform.dll | 141312 | `e3bc09a776cf5f7f2110d59343d03c8d6ed77174828921947afe6ba7b1f508ac` |

Their CodeView paths use `/_/pdf-form-filler/src/<project>/obj/Debug/net10.0/<assembly>.pdb`. The original binaries remain a separate historical identity.

## Rebuild recipe

Use the exact source checkout above in a fresh directory without generated bin/obj outputs, the pinned SDK and unchanged lockfiles. The qualification used an existing verified local NuGet feed/cache containing the exact 14 locked packages; package identities and lock content hashes are recorded in [dependencies.json](../notices/dependencies.json). This was not a fresh-online or clean-machine restore.

The only new compiler property is `PathMap`. It replaces the local source root, including generated obj files and the CodeView PDB path, with `/_/pdf-form-filler`. Keep Debug configuration, portable PDB generation and deterministic compilation, with optimization disabled. Do not add trimming, AOT, ReadyToRun, a runtime identifier, signing, Release settings or `ContinuousIntegrationBuild`.

From that source directory, set `src` to its absolute path, `dotnet` to the pinned SDK executable, and `feed`/`packages` to the verified local package locations:

```sh
"$dotnet" restore src/PdfForm.Cli/PdfForm.Cli.csproj --locked-mode --source "$feed" --packages "$packages" --disable-build-servers -m:1 -nodeReuse:false -p:BuildInParallel=false -p:UseSharedCompilation=false -p:NuGetAudit=false
"$dotnet" build src/PdfForm.Cli/PdfForm.Cli.csproj --no-restore --disable-build-servers -c Debug -m:1 -nodeReuse:false -p:BuildInParallel=false -p:UseSharedCompilation=false -v:normal "-p:PathMap=$src=/_/pdf-form-filler"
```

Do not weaken NuGet signature verification or change lockfiles. This four-project CLI graph excludes the test runners and appearance spike. The ordinary build creates apphost/PDB files, but those are excluded from this framework-dependent handoff. Retain the full font notice and third-party notices. The archive's `PACKAGE-INVENTORY.json` identifies its exact runtime payload; the recipe alone does not promise identical ZIP bytes. See [Microsoft's PathMap documentation](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/advanced#pathmap).

Actual compiler inputs, options and output identities were checked. No independent second same-input rebuild was performed, so byte-for-byte reproducibility is not claimed. The first wrapper stopped on metadata assumptions after both SDK commands succeeded; the reviewed no-rebuild continuation reused those captured outputs and passed its targeted checks. The failed wrapper remains historical failure evidence.

## Targeted checks and limits

Eight staged CLI calls and six native raster calls passed with Microsoft.NETCore.App 10.0.13. Third-form editable output retained four canonical fields/widgets, four normal appearances and blank reset defaults; flattened output had zero fields/widgets and the three expected text values. Selected third-form rasters matched the previously accepted raster, and stock Form 941/Schedule B outputs preserved baseline page/text/raster parity.

Schedule B's candidate check used the public `samples/941sb-2026q3.json` with only `values.name` changed to the fictional value `Fictional O'Connor-Smith & Co. É`; derived input SHA-256 `ec575027256aed83fe4c54026534d2cade885af9b64eeeea1c1fbd29755996dc`. The bundled sample is unchanged, and its exact original bytes were not separately executed in this candidate. Historical full-regression counts remain specific to the original binaries; they are not relabeled as a full candidate rerun.

Independent archive inspection reconciled exact members, lengths, hashes and extracted bytes, with no private preparation paths in the package. The exact two added upstream notices and the affected dependency DLLs' pinned-package identities were also reconciled. The new source notice files are recorded in [the upstream additions manifest](../notices/UPSTREAM-NOTICE-ADDITIONS.json); the archive retains those same bytes at the manifest's `notices/` paths. The PlatformAbstractions matching-version source association does not prove source-to-binary reproducibility. This record is not a legal-clearance opinion.

## Running an existing handoff

From its extracted root, use `dotnet bin/pdfform.dll`; a compatible .NET 10 runtime matching the host architecture is required. No machine-font installation is needed. Follow the [configuration workflow](GENERIC-ONBOARDING.md), replacing only the source-build CLI prefix with `dotnet bin/pdfform.dll`. Keep output destinations new, review the complete mapping and unresolved issues before `--reviewed`, and use fictional data for development checks. Preserve and inspect any output left after a diagnostic failure rather than blindly rerunning.

A compatible runtime does not establish Windows, clean-machine, interactive editing/save/reopen/reset, printing, production or filing qualification. Windows inputs require local fixed drives and output parents require local fixed NTFS without reparse ancestry. macOS publication is unsupported. The [work-PC handoff](WORK-PC-ACCEPTANCE.md) remains the separate acceptance checklist; its SDK/test-fixture instructions apply to a source checkout, not the binary package. This docs/notice successor changes no product source, project, dependency, runtime or release state.
