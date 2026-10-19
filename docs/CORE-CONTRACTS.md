# Pure validation contracts and handoff

This module implements PDF-independent Rev.2 parsing, profile invariants, typed normalization, data rules, initial 941 context policy, Schedule B calendar checks and packet consistency. It does not open PDFs or claim binding, appearance or filing acceptance.

## Entry points

- `ProfileParser.ParseProfile(ReadOnlySpan<byte>)` returns a `ParseResult<ProfileDefinition>`. Invalid syntax or invariants produce safe findings and no profile.
- `DataValidator.ParseData(ReadOnlySpan<byte>)` enforces the `/2` envelope and type grammar; `DataValidator.Validate(profile, data)` returns typed values, safe findings and explicit rule states.
- `PacketValidator.ParseJob(ReadOnlySpan<byte>)` parses the bounded two-form job and page/output policy. `PacketValidator.Validate(job, reports)` checks document context, identity, totals and calendar. Member data validation results must also succeed; packet results are additional, not replacements.
- `AssignmentPlanner.Build(validReport)` creates an in-memory payload manifest for every mapped field, including blanks and checkbox Off states. The renderer must additionally bind PDF identity/type/geometry/fonts and resolve omitted checkbox on-state under the single-on-state contract.
- `ResultDocument` describes value-free operation output. `tool.build` is the opaque build identity. Artifacts have `id`, `path`, `bytes`, `sha256`. Null optional finding members are omitted on serialization.

Use `StrictJson.Options` for camel-case case-sensitive serialization. `RuleCondition.EqualsValue` has wire name `equals` to avoid conflicting with record-generated equality methods.

## Authority and migration

Both reviewed profiles preserve their original ordered mappings: 68 keys/116 targets and 101 keys/212 targets. Migration changes only the accepted schema/typing/normalization/rule policies. Caller-neutral synthetic legacy fixtures are under `fixtures/contracts/legacy`; audit-only descriptive comments/status were removed without changing values or ordered mappings. Expected-map fixtures were captured independently from original target definitions, not reconstructed from the migrated definitions.

No initial date-cell rectangle is fabricated. Optional accepted layouts can be added only after the native spike and review. Exact source PDF hashes and original structural fingerprints are required. Profile content digests are recorded in the local migration manifest; this local artifact is not a signed release authority.

## Resource limits

JSON: 10 MiB, depth 32, 10,000 object members/keys/targets, 1,000 rules. Ordinary strings: 256 Unicode scalar values before and after declared normalization; profile regex source additionally capped at 1,024 characters with 100 ms execution timeout and full-string anchoring. Templates: 50 MiB/100 pages at the PDF boundary. Output/cache capacity is controlled separately.

Typed money/date/checkbox keys cannot use text-strip/case transformations to evade their strict grammar. Patterns preserve ASCII identifier semantics; no rounding or trimming occurs. Diagnostic errors use static text and approved key/rule names, never unknown user-supplied keys or values.

## Current test status

See [development status](DEVELOPMENT-STATUS.md) for the current observed code checkpoint and separate visual limitations. Core tests are data-only and cannot establish appearance correctness.

## Production profile authority

`ProductionProfileRegistry` contains the exact two migrated profile/template pairs independently accepted for mapping and contract fidelity, admitted only for bounded private renderer development. The explicit adoption scope is `bounded_private_development` and entry status is `accepted_private_development`. This admission does not establish release, supported-host, viewer, print or filing readiness. Any changed profile bytes, including a date-cell layout extension, need a separately reviewed registry update. Runtime trust enrollment and mismatch overrides are absent. `ProductionProfileSnapshot` owns immutable admitted bytes; opening/parse operations never reopen a caller path.

The admission/layout/wiring changes are a new source cut after the historical 137/30 checkpoint. Core activation/layout tests have now passed in the 142-case harness; no earlier diagnostic-only CLI count is reused as evidence for the new fill adapter.

Generic authoring `validate`/`check-profile` APIs remain available independently. Their success does not grant production profile authority. Packet policy identifier is caller-neutral `irs-941-packet/1`; it contains only supplied-value form consistency checks. Other packet-policy identifiers are not accepted by this implementation.
