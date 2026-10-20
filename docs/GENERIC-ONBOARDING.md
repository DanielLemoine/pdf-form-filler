# Add a supported AcroForm through configuration

This is the current development command flow. The external catalog implementation is source-reviewed; its complete unchanged-engine third-form acceptance is still pending. Use blank synthetic forms until the relevant environment and outputs have been checked. Configuration changes cannot add an unsupported PDF feature.

## Inspect and draft

After building, use the same managed CLI throughout. Existing output parents must be writable; every output file or package directory below must be new. Relative paths resolve from your working directory. Windows requires local fixed drives and fixed-NTFS output parents without reparse ancestry.

```sh
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll check blank-form.pdf
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll configure blank-form.pdf --out form-draft
```

`check` emits `pdfform.compatibility/1`: supported=0, configuration_needed=1, invalid=2, unsupported=3, internal_error=4. A supported blank form without a catalog normally needs configuration. Read the complete status and issues; nonzero does not always mean an internal failure. `configure` creates catalog/profile drafts and configuration result/completion metadata. It discovers fields and rendering facts, not their business meaning.

## Review the actual draft

Preserve the original generated draft. Work on a separate reviewed copy of its catalog.json and profile.json. Verify every canonical target, value kind, required/optional status, validation rule, fixed layout/font policy and checkbox export state against the blank form. The default field identifiers are hashes of canonical names; labels and business semantics are not inferred. A context-free profile/data pair uses version 3 schemas and has no tax context.

Resolve every configuration issue using supported policies and actual template facts. Do not clear an unresolved list merely to force acceptance. Unsupported active content, XFA, signatures, field types, fonts or geometry need engine work or a suitable form, not a renamed key. Keep the exact original template identity. Review any sampleHints as explicitly fictional examples, never real personal data.

When the reviewed copy is ready, explicitly approve it with the existing flag:

```sh
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll save-config blank-form.pdf --draft form-reviewed --out form-package --reviewed
```

`save-config` checks the edited profile, refreshes its exact catalog identity, requires no unresolved issues, performs independent configured binding and publishes a new package with catalog.json, profile.json, template.pdf, result.json and completion.json. The flag records your review decision; it does not waive unsupported features or structural errors. Treat the saved package as immutable. To revise it, review a new copy and save to a new destination.

## Generate fictional input and fill

```sh
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll sample-input --catalog form-package/catalog.json --out sample.json
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll validate --catalog form-package/catalog.json --data sample.json
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll fill form-package/template.pdf --catalog form-package/catalog.json --data sample.json --out filled-editable.pdf
dotnet src/PdfForm.Cli/bin/Debug/net10.0/pdfform.dll fill form-package/template.pdf --catalog form-package/catalog.json --data sample.json --out filled-flat.pdf --flatten
```

The sample is a bounded synthetic data/3 document with schema/profile/values. Its profile identity must match the saved profile. Generated values can be insufficient for a particular rule set; a failure requires explicit caller input or a reviewed correction, not ignored validation. Keep real inputs outside the repository.

Inspect every populated and cleared field in editable and flattened PDFs. Check text fit, checkbox states, defaults and any dates; editing/save/reopen, printing and filing are separate checks. JSON success is not visual or filing approval. Source-binding font warnings describe the original template; an explicit replacement warning describes the chosen generated output font. Do not blanket-suppress warnings.

There is no overwrite mode. A committed artifact may remain after diagnostic output fails; preserve and inspect the requested destination after process settlement. `document.pdf` in a result is a logical label, not a different destination. Compare actual output length/hash against the result before consuming it.

## Unchanged-engine acceptance boundary

For development acceptance, build and hash the complete production CLI closure before generating or introducing the new form/configuration. The separate test project has an explicit third-fixture mode; it never registers that form in production. Preserve the frozen engine bytes through draft generation, documented operator review, save, sample and both fill modes. Record actual process results and recheck the original closure afterward. The command examples above are usage instructions, not a report that this acceptance has passed.
