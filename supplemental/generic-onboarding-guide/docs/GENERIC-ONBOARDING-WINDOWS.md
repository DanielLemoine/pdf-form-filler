# Add a PDF form on Windows

Preserved review successor 003, 20 October 2026. **Source only; no runtime, test, renderer or viewer was run.** Earlier source successors and uploaded ZIPs remain unchanged. This revision corrects the public compatibility schema, final command envelopes, diagnostic privacy checks and reviewed-draft provenance gate.

The workflow is **check -> configure -> review/edit mappings -> save-config -> sample-input -> fill**. `scripts/GenericOnboardingWorkflow.psm1` now implements the six fixed CLI calls through the existing v5 supervisor. The entry script dispatches to that already loaded module in the same persistent session after root supplies exact reviewed build/source pins and live admission. There is no ApiContractConfirmed bypass, arbitrary command API, new process supervisor, automatic import or assembly load.

## Freeze the engine before introducing a new form

Use task11's `EngineFreeze` / `FileImage` and first-fixture sequence contract. An independent owner must freeze the complete admitted production closure **before** the third-form PDF, fixtures, configuration or cached approval is generated/copied/introduced. A hash first computed after those files are present cannot establish this ordering.

The freeze records the launcher hash and every closure member's relative path, native identity, kind, length and SHA256. It includes the entry assembly, dependencies, runtimeconfig/deps, fonts/resources, plugins and every externally resolved runtime file actually used. Record the baseline original two-form configuration/fixtures separately. Keep the independently pinned freeze evidence outside the engine closure. Runtime closure inventory and launch identity must agree before and after each operation and at final settlement, including a fresh CLI process/reload.

The first third-form introduction sequence must be greater than the freeze sequence. Verify the source freeze evidence itself against the independently approved pin; a caller flag, runtime version string, driver-created run-only hash or unverified JSON claim cannot replace the evidence. Task11's independent fixture owner/host observes the ordering; this guide does not invent evidence retroactively.

Admit disjoint ordinary local roots for `bundle`, `fixtures`, `configuration` and `evidence`; admit tool/output roots separately as necessary. Reject equal, nested or native-identity-alias roots. Third-form fixture, reviewed draft, synthetic input and new catalog files must never be placed in the frozen bundle. The bounded host must verify native ancestry and identity, not only string prefixes. The output domain must satisfy the existing local fixed-NTFS/no-reparse/no-alias admission and space policy.

## Execute only through the existing bounded Windows host

The command table below is an **argv specification for the admitted host**, not shell commands to paste or launch directly. No raw `dotnet`, `pdfinfo`, `pdftoppm`, viewer, tree scan or render enumeration is enabled by this handoff.

Every CLI, inspect, renderer and viewer invocation must use the existing bounded persistent launcher and native domain oracle. The root-owned exact plan must pin this assertion module and entry script, launcher/driver/oracle assembly, full runtime closure, CLI, renderer/inspection/viewer executables and sidecars, fixture tool/font and all input/evidence bytes. Tool selection by PATH or a version string is insufficient.

The plan must bind finite execution/termination/capture deadlines, a whole-session deadline, process/private-memory or equivalent admitted memory limits, stdout/stderr byte caps, aggregate disk/apparent/allocated byte caps and free-space reserve. Bounded inventory/read limits must cover file count, directory count, depth, per-file and aggregate bytes, canonical fingerprint/log size, and raster page/count/dimension/byte limits. Unobserved termination, incomplete capture, unknown inventories, exceeded caps or retained live process custody stop continuation. No automatic retry or process-name/tree search is supplied.

`BOUNDED-PLAN-REQUIREMENTS.json` makes the required pins, lifecycle and finite cap values explicit. Source is authored, awaiting root review/final pins; it grants no execution authority. The adapter preserves v5's 60-second process deadline, 5-second termination grace, 2-second capture grace and 1 MiB/pipe limits. Before every launch it reserves **67 seconds for every remaining slot plus 10 seconds for final settlement**, within the same running 600-second clock. Every bounded read, JSON token validation and domain observation uses that clock. Root must independently establish a native inherited memory envelope before admission; the adapter neither creates a new job framework nor pretends v5 itself supplies that native limit.

Task10's located FixedSessionLauncher is read-only reference; its fixed original plan is unchanged. The new small adapter directly reuses its exact v5 `New-ProcessState`, `Invoke-OwnedProcess`, custody, start-info environment isolation and exceptional settlement rule. It does not replace v5, add kill-by-name/tree logic, alter task10/task11 files or dynamically extend the admitted plan. Task11 supplies pre-existing independently pinned freeze/introduction evidence and native identity observations. Final candidate pins, live admission and root review are still required before execution.

The fixed order is check/configure/save-config/sample-input/fill-editable/fill-flattened, with exits **1,0,0,0,0,0**. Configure output remains untouched. Before save, exactly two separately approved reviewed source files are copied create-new into a separate reviewed directory, retaining their original hash/length pins. Saved package, generated sample, every prior output, copied input and diagnostic file are protected thereafter. Each call uses a fresh v5-owned process; local exact assertions and full invariant checks gate serial continuation.

Before that copy, both current configure descriptors must match the independently approved **source-draft cut** recorded in a pinned reviewed-draft provenance file. The same record binds the separately operator-edited catalog/profile source pins to that cut. Edited bytes may differ from the unedited draft. No approved cut means an explicit `configuration_needed`/1 stop after configure, without copying or claiming a six-step pass; mismatched/unrelated reviewed provenance is refused. Task11's existing provenance bridge must supply this evidence and new source pins, not merely assert that some reviewed files exist.

This six-slot plan includes no inspect/renderer/viewer invocation. Those remain separate later bounded actions; manual/per-field/visual labels stay not_observed. No raw tool fallback or dynamic seventh command is available.

## Prepare and review the blank

Use only an independently approved blank third, non-tax PDF with exact SHA256 and length. Review its revision, all pages, visible labels, controls and reading order in a separately admitted viewer. Preserve that approval record. A computed hash identifies bytes; it does not approve them. Changed template bytes require another review and binding.

Keep generated drafts, operator-reviewed drafts, saved packages, generated samples and reviewed synthetic inputs separate. Use fresh create-new destinations, fully qualified local paths and argument arrays. An existing destination requires another name; no `--force`, overwrite, install, restore or rebuild step is part of onboarding.

| Step | Exact CLI argv after the pinned entry assembly |
|---|---|
| Check blank | `check <approved-template.pdf>` |
| Configure | `configure <approved-template.pdf> --out <new-draft-directory>` |
| Save reviewed draft | `save-config <approved-template.pdf> --draft <reviewed-draft-directory> --out <new-package-directory> --reviewed` |
| Generate sample | `sample-input --catalog <saved/catalog.json> --out <new-sample.json>` |
| Editable fill | `fill <approved-template.pdf> --catalog <saved/catalog.json> --data <synthetic.json> --out <new-editable.pdf>` |
| Flattened fill | Same fill argv plus `--flatten`, with a different new destination. |

There is no generic `--config` option. The six-slot adapter fills the product-generated sample in both modes. Task11's separate regression scope covers independently reviewed fictional and negative inputs; no dynamic extra command is added to this plan. A hand-written fixture cannot replace testing product sample generation.

## Check support and the observed template

Check must emit **`pdfform.compatibility/1`**, not the ordinary command result. Require the exact expected status/process exit and exact observed `templateSha256` / `templateBytes` against the approved blank pin. A correct status with the wrong template or schema fails.

The public Core document has `counts` and `configurationState`; it does not expose inspector-private top-level `pages`, `prefilled` or `fields`. Public counts are nonnegative integer pages/terminalFields/textFields/checkboxFields/otherFields/widgets/requiredFields/combFields; pre-observation errors may carry an empty map and omit observed identity. Configuration state is absent/draft/reviewed/invalid; this initial no-catalog step specifically requires absent. The workflow's known approved template still requires both exact observed identity fields. A public prefilled issue is a refusal; counts do not prove blankness.

| Check status | Exit | Next action |
|---|---:|---|
| `supported` | 0 | The exact configured template can enter the supported fill path; data and output verification remain. |
| `configuration_needed` | 1 | Review structural discovery and author supported mappings/policies. |
| `invalid` | 2 | Preserve diagnostics and correct the input or binding. |
| `unsupported` | 3 | Preserve the bounded feature report and stop filling. |
| `internal_error` | 4 | Preserve the complete attempt for investigation. |

The blank third-form check expects `configuration_needed`/1. If separately admitted, configured check expects `supported`/0. Neither may report prefilled input or missing engine features as accepted blank support. The six-slot plan uses the save receipt's exact passed binding stages and both fill paths; it does not silently add another check or validate invocation.

Unsupported reports guide future engine work. Keep the exact template/build identities, stable reason codes, feature counts and first failed stage. Renaming keys or adding configuration cannot implement unsupported active content, XFA, signatures, widgets, fonts or layout. Share only authorized blank/synthetic artifacts; no taxpayer or private business data is needed.

## Review mappings and text policies

Discovery observes canonical field/widget identity, types, flags, geometry, checkbox export states and source font facts. Draft `field_<SHA256>` keys hash exact canonical field identities. The operator decides semantic keys, target meanings, required/optional values, types, checkbox mapping and form-specific cross-field rules. Compare every mapping against the blank; check omissions, duplicates and intentional blanks. Do not infer business meaning from `Text1` or a guessed label.

Generic v3 data omits `context`; the v3 profile omits `filingYears`. Do not insert a tax-context stub. Ordinary form-specific dates/identifiers remain semantic keys. Legacy v2 IRS context rules stay separate.

Use actual emitted DTOs. Frozen `TextFieldPolicy` properties are Target/Page/Rectangle/Strategy/Layout/ResourceName/BaseFont/Subtype/Encoding/FontSize/MetricsSha256/Repertoire and optional ProgramSha256. Source identity facts must describe the source font; absent encoding is an observed value. Omit ProgramSha256 only when the source program is absent; JSON null is forbidden.

- `single-line-v1` supports ordinary formatted date strings; `eight-date-cells-v1` requires `MMddyyyy` and all eight reviewed rectangles.
- Fixed observed size is finite, 1–72 pt; built-in IRS forms remain 10 pt. No shrinking.
- `preserve-embedded-simple-v1` requires TrueType/WinAnsiEncoding/source program hash/`encoded-font-v1`.
- `bundled-liberation-serif-ascii-v1` permits source Type1/TrueType with WinAnsiEncoding or observed absence, using `ascii-plus-eacute-v1`; output Liberation is engine-pinned. No silent font substitution or broader Unicode promise.

## Require exact published configuration receipts

Only configure/save-config may use **`pdfform.configuration-result/1`**. Require exact expected `operation`, `status=ok`, process and receipt `exitCode=0`, `artifactState=published`, `filingReadiness=not_assessed`, no errors, full template/profile identities and exact payload descriptors. Staged, unknown, failure, unrelated-operation or malformed receipts stop the workflow even if a destination directory exists.

| Operation | Exact payload ID / logical path set | Exact directory members |
|---|---|---|
| configure | `catalog/catalog.json`, `profile/profile.json` | Both payloads plus `result.json` and `completion.json`. No copied template. |
| save-config | Above plus `template/template.pdf` | Three payloads plus `result.json` and `completion.json`. |

Each output descriptor has exactly `id`, `path`, positive integer `bytes`, lowercase SHA256. Reject unknown/extra/missing/duplicate IDs, wrong logical paths, mismatched length/hash or extra directory members. Reopen/hash actual payloads through the bounded host. Profile identity must agree with the actual reopened profile's id/revision/profileVersion and its descriptor. Saved template identity must agree with `template.pdf` and the original approved template hash/length. Configure's observed template identity must agree with the independently known input identity; its path is not a request to open a nonexistent output.

Configuration stages are exactly syntax/profile/binding/bindingStructure/data/packet/render. Syntax/profile must pass; binding/bindingStructure may be passed or not_run as admitted; data/packet/render are always not_run. The host must apply the existing strict completion validator to the committed result/completion metadata. Validate **both** terminal stdout and committed result independently. A committed ok receipt cannot excuse a later nonzero/published terminal failure. No flag may waive any validation.

## Require the separate sample result

CLI-owner source now emits **`pdfform.result/2`** for sample-input: operation `sample-input`, status `ok`, exit 0, artifactState `published`, no errors, and exactly one **`data` / `data.json`** descriptor matching the actual caller-selected sample file's length/hash. Syntax/profile/binding/bindingStructure/data pass; packet/render remain not_run. Verify the template identity too. Configuration-result receipts for this operation are rejected.

Both final catalog-command envelopes require the exact six scalar tool fields name/version/build/engine/runtime/**sourceReference**, profiles=[], stats={}, exact warning objects and no extra nested value fields or requestId. `sourceRevision` is not the Core field. Ordinary warnings use the CLI's fixed warning message, allowed stages and hashed optional references.

The generated data must be `pdfform.data/3` with no context. Product generation must return bounded deterministic fictional values satisfying supported type/required/length/glyph/fit/rule checks, or an actionable inability. Preserve output and stop on inability. Independently reviewed fixtures should cover nontrivial names, checkbox on/off, ordinary dates, supported accents and near-limit text. Negative missing/type/unknown/overflow/glyph cases remain explicit tests, not silently accepted samples.

## Verify fill receipts and preserve every consumed byte

Fill requires `pdfform.result/2`, operation fill, status ok, process/receipt exit 0, published state, no errors, and exactly one descriptor with **id `document` and logical path `document.pdf`**. Its length/hash must match the physical caller-selected `--out`. A different logical path fails even when its hash matches.

Successful fill levels are syntax/profile/data/binding/bindingStructure/**render passed**, packet not_run. A positive fill receipt claiming render not_run is invalid; sample retains its separate render-not-run contract.

Establish every expected output hash from its **original validated receipt**, never from a later observation. Maintain one protected ledger covering original pins, copied reviewed-draft files, copied synthetic input, saved package/metadata, generated sample and all prior published PDFs/receipt files. Copied inputs retain their original source pins; do not bless the copy by computing a replacement expected hash.

Before and after **each** CLI invocation, after each reviewed-file copy, on failure settlement and at final settlement, the adapter re-observes the complete protected set and frozen closure. Reads of protected sources are compared against their original pins; publication reads are bound to original validated descriptors before continuation. Require original path/native identity/length/hash and directory inventories. Do not check only the current case. Changed/missing/extra/aliased inputs or prior outputs fail. Any separately admitted later inspect/render/viewer action must use the same full ledger checks before and after its settlement.

Immediately after each inspect, renderer or viewer operation, compare the published PDF again to its original fill-receipt pin. Summary hashes must copy the validated original descriptor, not a fresh hash of potentially changed bytes. Preserve originals during viewer edit tests: save to a distinct new file and treat that edit/reopen derivative separately. Recheck the generated original PDF and every consumed input after viewer settlement too. If a process outcome or custody is uncertain, no success settlement is available.

The adapter screens bounded raw and recursively JSON-decoded **diagnostic/report channels** for synthetic canaries, absolute private paths and discriminating known sample strings. It retains prior reports and rescreens them when the generated sample reveals additional values, then at invariant/final checks. Boolean and shorter-than-four-character strings are not unique canaries, matching task11's rule; strict envelope/member checks still reject injected value fields. Intentional sample/profile/catalog exports and provenance inputs are not logs and are never submitted to that report screen. Values are read from the sample only to extend the private token set; they are never echoed by a failure message.

## Verify both output modes honestly

- **Editable:** independently verify expected field/widget identities, values and editability; inspect every page. Through the admitted viewer, edit fictional data, save a separate new derivative and reopen it. Confirm values, checkmarks, date cells, alignment and clipping. Preserve the generated original against its original receipt pin.
- **Flattened:** independently verify fields/widgets are absent and expected appearances remain. Render every page with pinned bounded tools and confirm the admitted viewer offers no editable controls.
- `pdfinfo Form:`, extracted text, PNG creation and successful fill are smoke evidence. They do not establish per-field correctness, visual fit, save/reopen or print/filing acceptance.

Keep labels `manual_review_required`, `perFieldWidgetVerification=not_observed`, `visualAcceptance=not_observed`, `editableSaveReopen=not_observed` until those actual independent observations exist. This handoff supplies none of them.

## Preserve legacy IRS flows and failures

For an accepted embedded-font candidate, use the supplied original profiles and stock synthetic samples, including **`samples/941sb-2026q3.json`** for Schedule B. The earlier custom sample-path assumption is removed. Legacy validate/fill with `--profile` remains separate; the existing task10 fixed plan already pins both original forms and the stock samples. Its exact accepted candidate hashes govern, not source001's historical pre-embedding profile assumptions.

Keep diagnostics, process evidence, original pins and every existing output on all failures. Nonzero exits, published reporting failures, staged/unknown outcomes and `E_DIAGNOSTIC_WRITE` do not imply rollback. Preserve and reconcile settled evidence; do not overwrite or blindly retry.

## Source handback and remaining integration

`GenericOnboardingAssertions.psm1` contains pure exact-schema/operation/state/descriptor/identity, protected-artifact, disjoint-root and task11 freeze comparisons. It performs no file reads, scans, native calls, imports or launches. Source-only regression cases are separately authored; they have not run.

The adapter source implements the six-step lifecycle, full remaining reservation checks, root-admission/pin joins, v5 process calls, exact receipts, actual completion reconciliation, original-pin ledgers, domain/closure rechecks, create-new reviewed copies, finite evidence and final observation. `ADMISSION-CONTRACT.md` specifies the live root context and fixed aliases. Source integration cases and a disposable synthetic child fixture are included. None is built, imported, registered or executed here. Root supplies the final build/source pins and existing host/native memory admission, reviews the exact source and then separately authorizes execution.

CLI schema correction is assigned to its owner; the adapter requires `pdfform.compatibility/1` and exact observed identity. Read-only inspection of the newer ConfigurationCommands/CliConfigurationReceipt declarations reconciled configure/save stdout, fixed template/profile identity paths, draft payloads, stage values and public completion reconciliation. Sample requires its separate command result. Final candidate pins must bind that corrected integrated build; older source observations are not evidence that a final binary implements it. The earlier failed task-message transport was not retried.

No product source, existing task checkout, original archive, prior handoff or frozen runtime was edited. No upload or execution is authorized by this source successor.
