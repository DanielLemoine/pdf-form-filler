# Synthetic appearance fixtures

The two blank templates are exact reviewed inputs, not replaceable by a new download under the same URL. Their hashes are listed below and in the profiles/fixture manifest.

- Form941 (March2026): https://www.irs.gov/pub/irs-pdf/f941.pdf ; SHA256 `38a3d8cf7a455101d52543c8c48e66202bc25c189ead878627e35c797c88e2ad`.
- ScheduleB (March2024): https://www.irs.gov/pub/irs-pdf/f941sb.pdf ; SHA256 `95a62159c8367fc50e6b5445f7d80f9a00ac7cdf56d224240fc2776cb545ab63`.

The forms retain their original embedded resources and reviewed active-content structures. The implementation's exact-template normalizer removes only the admitted structures in generated output; do not rewrite the bundled blank PDFs. Provenance is not a claim that all template/font resources are relicensed under the project's AGPL.

`fixture-manifest.json` contains fictional engine experiments, including mutually exclusive all-on selections and unreconciled totals. These are not valid tax-return examples. The AppearanceSpike project remains in the solution to preserve the source build graph; it is an experiment, not the production CLI.

The optional Python scripts inspect/rasterize generated files using external pypdf/PyMuPDF tools. Their environment is not pinned for supported reproduction. Do not confuse single-glyph specimens with correct multi-character rendering: current ScheduleB public-name output has an open visual defect described in `docs/DEVELOPMENT-STATUS.md` at the repository root.
