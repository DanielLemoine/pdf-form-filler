# Verified fill boundary

The public preparation API binds immutable exact template/profile bytes, validates supplied data, assigns every target (including blanks and checkbox Off), generates complete normal appearances, closes and reopens the result, and verifies values, geometry, resources and generated paint. Flattening uses those verified normal appearances and reopens again to check that interactive fields/widgets are absent and expected paint is retained. Create-new publication verifies closed artifact bytes before commit. These checks are deliberately narrower than visual equivalence.

Form 941 uses its existing embedded font and the reviewed eight `MMddyyyy` date cells. The current Schedule B path retains its unembedded native `TimesLTStd-Roman` resource at fixed10pt, admits printable ASCII plus É subject to glyph/fit checks, and always warns that portability is unqualified. **Its ordinary multi-character Poppler rendering failed independent visual acceptance.** The embedding correction is pending; see [development status](DEVELOPMENT-STATUS.md). No claim of usable general text rendering follows from decoded appearance text or width checks.

Checkboxes use reviewed resource-free vector marks rather than a viewer-substituted mark font. Unsupported clipping, opacity, transformations, unknown paint operators and unexpected active content fail the bounded verifier. There is no automatic font fallback or shrink-to-fit.

Publication status and filing status are separate. A create-new artifact may be committed before diagnostic output fails. Preserve and inspect the caller-requested destination on an uncertain result. Force replacement and whole-job output remain disabled.
