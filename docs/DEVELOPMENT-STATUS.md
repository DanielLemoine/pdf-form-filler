# Development status

## Observed code checkpoint

The 2026-10-19 combined code snapshot built with zero warnings/errors using SDK 10.0.402. Linux observations were Core 142, inspector 147, renderer 136, CLI 45 and nine exact migration comparisons. The unchanged active publication harness has 244 passing Linux cases; its seven rebuilt runtime files matched its prior passing closure exactly. The two added CLI regressions use the unchanged stock Schedule B sample in editable and flattened modes and independently check output descriptors, privacy and identical-argument no-overwrite preservation.

These are structural, semantic and serialization checks. They do not establish correct PDF appearance in every viewer. This curated source layout has not had a separate clean-room restore/build. Earlier failures remain failures and are not included as passing evidence.

## Current blocking visual defect

An independent 288-dpi Poppler/MuPDF check of ordinary multi-character Schedule B name fields found visible uneven spacing and overlapping letters in Poppler; MuPDF displayed those lines cleanly. The public repertoire fixture also failed two Poppler character-window checks for an ASCII apostrophe, one editable and one flattened. A zero-window result alone does not prove a missing glyph, but the independent visual review did not accept the multi-character layout. Readable one-glyph-per-field specimens were insufficient evidence for real names.

The current source uses an unembedded native Schedule B font and emits `W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED`. An embedding correction is being developed separately. It is not in this snapshot and requires fresh source review, runtime regression and independent visual acceptance before a readiness claim. No installed workstation font is silently searched, copied or exported.

Form 941 has a separate bounded visual checkpoint, but that is not universal viewer, editing/reset, physical-print or filing acceptance. Editable and flattened output may differ at antialiased edges; exact bitmap equality is not claimed.

## Configuration-only form onboarding is pending

The mapping architecture is profile-driven, but the current production profile registry and approved template admission contain compiled two-form entries. A new form cannot yet be added only by configuration. An external exact-hash catalog design is being reviewed so supported form packages can be added without rebuilding the engine while preserving admission checks and resource limits. That correction is not present in this snapshot.

## Scope boundaries

Only the exact reviewed template/profile byte pairs are admitted for filling. Runtime trust enrollment, mismatch bypass, arbitrary templates, force replacement and complete job/packet publication are unavailable. Data-level packet consistency APIs do not imply the disabled `run` command is implemented. Filing readiness stays `not_assessed`.

Create-new checks preserve known existing destinations. Detected identity substitutions and unknown stage entries cause refusal at the corresponding checks. Stronger post-check hostile namespace races, interruption/recovery qualification and platform-specific durability remain outside the accepted boundary.

The source license has been selected as AGPLv3. Dependency notices and template/source provenance are included, but that decision is separate from code correctness, Windows execution, supported-host qualification and readiness to distribute a supported binary product.
