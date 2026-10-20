# Third-party notices and source references

Project-authored code uses AGPLv3. The following material retains its own license and copyright notices. The `notices/` directory contains the package-supplied notice/license/nuspec files available for the exact locked versions, plus pinned upstream additions for DependencyModel and PlatformAbstractions. [The upstream additions manifest](notices/UPSTREAM-NOTICE-ADDITIONS.json) records their exact source URLs and file hashes. `notices/dependencies.json` records all 14 resolved package identities and lock content hashes without machine-local paths.

## NuGet dependency families

- iText, itext.commons and itext.bouncy-castle-adapter 9.8.0: upstream identifies AGPL/commercial licensing. This source project follows the AGPL route; no commercial iText grant is claimed. Preserve upstream notices. See the included iText LICENSE.md/NOTICE files and the AGPLv3 text in the root LICENSE. Upstream source: https://github.com/itext/itext-dotnet .
- BouncyCastle.Cryptography 2.7.0: package metadata identifies MIT; its exact LICENSE.md is included. Upstream source: https://github.com/bcgit/bc-csharp .
- Microsoft.Extensions.DependencyInjection, DependencyInjection.Abstractions, Logging, Logging.Abstractions, Options and Primitives 5.0.0; Microsoft.Extensions.DependencyModel 10.0.2; System.Security.Cryptography.Pkcs 6.0.5; System.Security.Cryptography.Xml 6.0.2: preserve the included package licenses, notices and metadata. The inspected package metadata identifies MIT, subject to the components enumerated in their third-party notices. DependencyModel 10.0.2 additionally includes the complete MIT license/copyright text from dotnet/dotnet commit `44525024595742ebe09023abe709df51de65009b`, the exact source commit declared by its package metadata.
- Microsoft.DotNet.PlatformAbstractions 1.1.0: the package nuspec lacks an explicit license field. The included pinned dotnet/core-setup source project declares 1.1.0/netstandard1.3 and its repository LICENSE is MIT, copyright 2015 .NET Foundation. See that package's SOURCE-LICENSE-CONTEXT.json for the exact commit and URLs. This source association does not prove byte-for-byte DLL build provenance. The complete 1,930-byte repository-wide third-party notice from that matching-version source commit is now retained conservatively; applicability of every entry to this managed DLL is not asserted. The package metadata and DLL do not identify an exact source commit, so a matching-version source association is established without claiming a byte-for-byte source-to-DLL rebuild. No unrelated Microsoft.Extensions/Apache conclusion is used.

No dependency DLL, package cache or runtime installer is included in this source tree. Locked restore retrieves packages from the configured official public feed. The source license decision does not waive dependency obligations for later binary distribution or network deployment.

## Blank templates and optional tools

The two bundled blank PDFs originate from the IRS URLs and exact hashes in `fixtures/appearance/README.md`. They are not project-authored source and are not relicensed here. Preserve their existing content and notices. Exact-template matching means an updated download is not interchangeable.

Optional Python appearance scripts use pypdf and PyMuPDF only when separately installed. They are not included dependencies, and their unpinned environment is not a supported reproduction claim. Review their own upstream terms before using or distributing them. The separately included font and its notice are described below.

## Bundled Schedule B font

`src/PdfForm.Pdf/Fonts/LiberationSerif-Regular.ttf` is Liberation Serif Regular 2.1.5, embedded in generated Schedule B outputs under the explicit replacement policy. The font is licensed under the SIL Open Font License 1.1, with its original copyright and reserved-font-name notices retained in `src/PdfForm.Pdf/Fonts/LiberationSerif-Regular.LICENSE.txt`. That full source notice also includes a separately labeled Debian packaging license stanza; it does not change the font's OFL terms. Preserve the complete notice. The project AGPL license does not relicense the font.

The accepted font file is 393,692 bytes, SHA-256 `9caef765d2e891c10dd73658894f01e660fe0c1c83e0bda7d6edf561d8f623d4`. The complete notice is 5,756 bytes, SHA-256 `f32df582a2aab128c07b2f7e36e7b928dc5f6bd82657b905c04f91459f9e5cf9`. Build outputs copy the full notice alongside the managed components. No machine font installation is required.
