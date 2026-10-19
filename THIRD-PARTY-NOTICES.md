# Third-party notices and source references

Project-authored code uses AGPLv3. The following material retains its own license and copyright notices. The `notices/` directory contains the package-supplied notice/license/nuspec files available for the exact locked versions, plus a pinned upstream source-license reference for PlatformAbstractions. `notices/dependencies.json` records all14 resolved package identities and lock content hashes without machine-local paths.

## NuGet dependency families

- iText, itext.commons and itext.bouncy-castle-adapter9.8.0: upstream identifies AGPL/commercial licensing. This source project follows the AGPL route; no commercial iText grant is claimed. Preserve upstream notices. See the included iText LICENSE.md/NOTICE files and the AGPLv3 text in the root LICENSE. Upstream source: https://github.com/itext/itext-dotnet .
- BouncyCastle.Cryptography2.7.0: package metadata identifies MIT; its exact LICENSE.md is included. Upstream source: https://github.com/bcgit/bc-csharp .
- Microsoft.Extensions.DependencyInjection, DependencyInjection.Abstractions, Logging, Logging.Abstractions, Options and Primitives5.0.0; Microsoft.Extensions.DependencyModel10.0.2; System.Security.Cryptography.Pkcs6.0.5; System.Security.Cryptography.Xml6.0.2: preserve the included package licenses, notices and metadata. The inspected package metadata identifies MIT, subject to the components enumerated in their third-party notices.
- Microsoft.DotNet.PlatformAbstractions1.1.0: the package nuspec lacks an explicit license field. The included pinned dotnet/core-setup source project declares1.1.0/netstandard1.3 and its repository LICENSE is MIT, copyright2015.NET Foundation. See that package's SOURCE-LICENSE-CONTEXT.json for the exact commit and URLs. This source association does not prove byte-for-byte DLL build provenance. The applicability of every entry in the repository-wide third-party notice file to this managed DLL remains unresolved; no unrelated Microsoft.Extensions/Apache conclusion is used.

No dependency DLL, package cache or runtime installer is included in this source tree. Locked restore retrieves packages from the configured official public feed. The source license decision does not waive dependency obligations for later binary distribution or network deployment.

## Blank templates and optional tools

The two bundled blank PDFs originate from the IRS URLs and exact hashes in `fixtures/appearance/README.md`. They are not project-authored source and are not relicensed here. Preserve their existing content and notices. Exact-template matching means an updated download is not interchangeable.

Optional Python appearance scripts use pypdf and PyMuPDF only when separately installed. They are not included dependencies, and their unpinned environment is not a supported reproduction claim. Review their own upstream terms before using or distributing them. No new external font is included in this snapshot; the pending embedding correction requires its own source/license review.
