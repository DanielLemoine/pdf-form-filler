# Development status

## Embedded-font checkpoint

The embedded-font development cut built with zero warnings/errors using .NET SDK 10.0.402. Linux observations were Core 142, inspector 147, renderer 149, CLI 45 and nine exact migration comparisons. All six build/test phases exited successfully and their owned processes settled. The active publication harness retains its separate 244-case Linux result because all seven runtime files matched its prior passing closure. The supplemental golden publication tests remain a separate 254-case result.

The CLI regressions use the unchanged stock Schedule B sample in editable and flattened modes, checking output descriptors, privacy and identical-argument no-overwrite preservation. These checks are separate from the selected visual specimens. This curated source layout still needs its own clean-room restore/build.

## Selected visual correction accepted

The prior native, unembedded Schedule B font produced visibly uneven spacing and overlapping letters in Poppler. Two apostrophe-window checks also failed. Those results remain historical failures; they are not reclassified as passes.

The correction embeds the exact bundled Liberation Serif Regular font and replaces the generated Schedule B text defaults and appearances consistently. Printable ASCII (U+0020–U+007E) plus É remains the supported repertoire at fixed 10 pt. Unsupported Unicode or text that does not fit is rejected. The diagnostic is `W_FONT_EXPLICIT_REPLACEMENT`; there is no silent workstation-font search or automatic fallback.

The corrected cut passed a bounded 288-dpi review of 12 PDFs and 28 rendered pages in Poppler 26.05.0 and MuPDF 1.26.11. All 2,388 recorded structural/pixel/preservation checks passed. Independent reviewers viewed all four contact sheets and accepted ordinary-name spacing, admitted glyphs, and the selected Form 941 overview/date/checkbox details. The unchanged apostrophe search windows now contain visible ink in both editable and flattened outputs.

This supports the selected synthetic specimens and fixed font in those two renderer versions. It does not establish Windows/Adobe behavior, arbitrary-viewer equivalence, viewer editing/reset, physical printing or filing readiness. Exact bitmap equality between renderers is not claimed.

## Generic catalog integration: pending checkpoint

This draft contains the reviewed Core catalog contracts, PDF discovery/binding/rendering policies, CLI authoring and catalog commands, and create-new configuration publication. The two built-in identities remain unchanged. A third form's identity is present only in test-side fixture source, never in the production registry.

Two narrow fixture corrections are included: explicit PDF `/Off` name construction for the installed library API, and post-close MemoryStream.ToArray readback for finalized serialization. The corrected focused build passed. The inspector then stopped at the first configured bundled-font positive assertion, at 1 pt; the returned diagnostic cause has not yet been observed. A bounded value-free diagnostic run is being prepared before any product correction. Core has passed 202 checks in this development line. The broader inspector/renderer/CLI/publication/migration checkpoint remains incomplete; earlier interrupted and failed attempts do not count as passes.

Prospective counts are Core 202, inspector 543, renderer 149, CLI 91, publication 313 Linux / 379 Windows and nine migration comparisons. The separate third-form generator mode does not change ordinary CLI counts and has not been invoked for end-to-end acceptance. Before that acceptance, freeze the complete built production CLI closure, then introduce a new synthetic blank form and reviewed configuration, and run check/configure/save/sample/editable/flattened flows without changing any frozen engine bytes. Human draft review is mandatory.

The external catalog path is implemented development source; configuration-only onboarding is not yet end-to-end qualified. Generic visual acceptance, Windows viewer editing/reset, physical printing and filing readiness remain open. The embedded-font two-form results above apply only to their exact earlier cut.

## Scope boundaries

The legacy profile path admits its exact reviewed template/profile pairs. The new catalog path requires a reviewed exact-byte package and independently observed supported PDF structure. Neither path permits hash mismatch overrides, unsupported-feature enrollment, force replacement or complete job/packet publication. Data-level packet consistency APIs do not imply the disabled `run` command is implemented. Filing readiness stays `not_assessed`.

Create-new checks preserve known existing destinations. Detected identity substitutions and unknown stage entries cause refusal at the corresponding checks. Stronger post-check hostile namespace races, interruption/recovery qualification and platform-specific durability remain outside the accepted boundary. A committed PDF can remain after diagnostic-output failure; preserve and inspect the requested destination rather than assume rollback.

Project-authored source uses AGPLv3. Dependencies, blank templates and the bundled font retain their own notices and terms. Source licensing, correctness, Windows qualification and readiness to distribute a supported binary product are separate decisions.
