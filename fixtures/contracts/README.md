# Synthetic contract fixtures

The legacy profiles and four data examples are synthetic migration/negative-test inputs. They preserve original logical keys, target ordering, values and older wire shapes. Audit-only descriptive status/comments were removed so these public fixtures do not depend on private reports or a particular calling application.

The current runtime rejects `/1` schemas. `tools/migrate_reviewed_inputs.py` performs the documented offline migration to `/2`. Expected target maps in `tests/PdfForm.Core.Tests` are independently captured from legacy target definitions; tests compare every ordered target and separately exercise every assignment/copy/choice.

`date-layout-941.json` is the independently reviewed eight-cell geometry for the exact Form 941 template. The deterministic migration applies it only to Form 941 profile version 2. It does not infer geometry from field widths. `profile-migration-manifest.json` records reproduction facts; runtime authority comes only from the source-owned exact-byte registry, not this manifest or the migration script.

No employer application source, live taxpayer records, credentials or private audit findings belong in this directory.
