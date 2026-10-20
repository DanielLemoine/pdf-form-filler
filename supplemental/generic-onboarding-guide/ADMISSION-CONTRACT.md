# Fixed generic workflow adapter: live admission contract

SOURCE ONLY / NOT_RUN. Root reviews this source and supplies final build/source pins before any execution. Do not serialize this context and treat JSON booleans as authority. It is a live root-owned admission object in the same persistent Windows PowerShell session as the existing v5 module, loaded native oracle and existing task11 host. No source here imports modules, loads/compiles assemblies, creates a job object, resets state or starts a new shell/session.

`Invoke-GenericOnboardingWorkflow -ReadOnlyAdmission $context` is implemented. It performs six fixed, fresh-process calls through **the unchanged v5 supervisor** and returns a nonacceptance console terminal. `Get-GAState` keeps strong references to source modules, context/locks, clock, oracle, processes/handles, raw captures, original pin ledger and failure/settlement records. One module instance supports one attempt. No reset/retry API exists.

## Root context fields

- `state='approved-fixed-generic-onboarding-plan'`, `executionAuthorized=true` only under root's later exact grant. These are root-owned live decisions, not user-supplied flags or substitutes for the remaining checks.
- `admissionCompletedClock`: the already running `Stopwatch` started after existing read-only admission and before setup/fixture generation. All setup, copies, scans, reads, parsing, six calls, evidence and final settlement consume the same 600 seconds. The adapter never restarts it. Initial six-slot reservation is 402+10 seconds; consumed setup/fixture time must leave that reservation.
- `v5Module`: already loaded `PublicationValidation.psm1`, exact SHA256 `298603d591eeeddddd8763dc19ac47949ba390f570873fc3cfed6da8c6c3ee29`. Root must validate/pin/retain this module before calling the adapter, as in the existing session launcher.
- `workflowModule`, `assertionsModule`: already loaded exact new source modules, pinned by `adapterSha256` / `assertionsSha256`. `entryScriptSha256` pins the adjacent entry script. No automatic import or force reload.
- `domainOracle`: existing `PdfForm.FixedDriver.IFixedDomainOracleV1` implementation, already bound to this exact attempt/package inventory and the same clock. `oracleAssemblySha256` pins its already loaded assembly. It provides native handle identity and complete bounded domain observations; the adapter adds no recursive enumerator.
- `task11Host`: existing `IExistingBoundedHarnessHost` implementation, already loaded and pinned by `task11HostAssemblySha256`. Only its native identity observations are used here. `publicationAssembly` / `publicationAssemblySha256` pin the loaded actual publication assembly exposing public `CompletionProtocol.Reconcile`. There is no replacement completion implementation. Root must confirm assembly/PowerShell compatibility and actual loaded-byte provenance during admission.
- `publicationAdmission`: the existing v5 read-only admission object used by `New-StartInfo`; root retains its input locks. Its host/runtime/system paths and immutable packet must be the same admitted inputs used by this context. `hostPath`, `hostSha256`, `cliDll`, `cliSha256`, `runtimeVersion='10.0.13'`, `toolBuild` are exact final-candidate identities. Fixed argv pins framework selection and disables roll-forward; sample/fill must also report `.NET 10.0.13` from the child.
- `attemptRoot`, `packetDirectory=<attemptRoot>\packet`, `engineDirectory` under packet, `captureDirectory=<attemptRoot>\evidence`. Existing oracle roles remain `packet/outputs/state/evidence/fixtures`. Root creates/adopts them only through the existing admitted setup. Outputs must initially be empty. No existing attempt is reused/repaired.
- `maximumRunBytes` is independently admitted, positive and <=2 GiB; `reserveBytes` >=12 GiB. Existing oracle accounting enforces both apparent and allocated bytes, caller-available free reserve and its finite entry/depth/read/fingerprint bounds. Named streams, hardlinks, aliases and reparse inputs must be denied by existing admission/oracle rules.
- `memoryAdmission`: **root's existing live native inherited memory-envelope observation**, tied to this owner PID, with `ownerPid`, positive `inheritedPrivateBytesLimit<=536870912`, `nativeEnforcementObserved=true`. Root must actually establish and verify inheritance before admission. The adapter verifies the join and refuses absent/unknown admission; it does not implement that native limit or infer it from an arbitrary JSON statement. No new supervision framework is introduced.
- `originalPins`: finite independently approved file records `{Key,Path,Bytes,Sha256,NativeIdentity}`. Native identities use task11's 16-lowercase-hex volume/file-number representation `xxxxxxxxxxxxxxxx:xxxxxxxxxxxxxxxx`. Root must bind all source/assembly/runtime dependencies and consumed fixtures; original records are verified and privately copied into the protected ledger before use.

Required pin keys: `v5`, `assertions`, `adapter`, `oracle`, `task11-host`, `publication`, `dotnet`, `cli`, `entry-script`, `template`, `reviewed-source/catalog`, `reviewed-source/profile`, `freeze-evidence`, `introduction-evidence`. Runtime/assembly sidecars and independent admission evidence must also be pinned by root. A source/assembly path/hash mismatch stops before launch. All bounded immutable reads together are limited to 256 MiB per invariant pass; each file is capped at64 MiB, PDF4 MiB, profile/data1 MiB, parsed receipts256 KiB.

R3 review-mode refinement: reviewed-source pins are required when `reviewed-draft-provenance` is supplied. Without that approved provenance cut, reviewed files may be absent; the adapter still performs check/configure and then returns review-needed before any reviewed copy/save. This is a noncomplete `configuration_needed`/1 result, not a successful six-slot workflow. The task11 positive coordinator may continue to require its full reviewed preconditions; an initial authoring/review phase remains separately admitted work.

The independently approved `reviewed-draft-provenance` pin points to a bounded private JSON input with exact fields:

```
schema: pdfform.reviewed-draft-provenance/1
producerCliSha256: exact approved CLI digest
template: {path: template.pdf, bytes, sha256}
sourceDraft: {catalog: {path: catalog.json, bytes, sha256}, profile: {path: profile.json, bytes, sha256}}
reviewedCopy: {catalog: {path: catalog.json, bytes, sha256}, profile: {path: profile.json, bytes, sha256}}
reviewReference: root's opaque documented review reference
```

Root/task11's existing `RequireDocumentedReviewedDraftProvenanceAndPins()` must attest the actual generated source cut and documented operator edits under the same frozen candidate, and register this original pin. The record does not self-approve anything. Immediately before copying, current configure/catalog and configure/profile receipt pins must equal both sourceDraft descriptors; reviewed source pins must equal reviewedCopy descriptors. The two sets intentionally need not equal each other. A mismatch refuses before copying; no current hash is silently promoted to a new approved cut.

Optional `diagnosticCanaries` extends the fixed private canary set. Diagnostic retention is capped at64 reports/8MiB; JSON report bytes256KiB, raw text1MiB, decoded nodes16384/depth24/string characters1MiB, private tokens512/65536 characters/4096 each. Every scan consumes the same clock. Screened channels are stdout/stderr, committed configuration result/completion metadata and adapter process/terminal reports. Intentional data/profile/catalog exports and admission/provenance inputs are excluded. New generated sample string values of at least four characters, excluding booleans, trigger rescreening of all earlier reports. Unknown/incomplete privacy checks stop continuation with fixed value-free codes.

## Freeze and fixture boundary

Task11's owner freezes the full actual product/runtime closure **before** generating/copying any third-form template/configuration. Fixture generation is a separate explicitly admitted fixture-tool action outside the immutable packet and before this plan's check phase. The clock/reservation includes any setup/fixture time in this session. This adapter does not generate a fixture, change an engine, add a registry entry or stage a hidden configuration inside the bundle.

`freeze-evidence` is the independently pinned task11 `EngineFreeze` JSON. `introduction-evidence` is the independently pinned owner's actual report containing `freeze` and `firstFixtureSequence` from the task11 contract. Root independently verifies its producer/admission and that it reflects real observations, not reconstructed sequence claims. This adapter joins the complete frozen file identities/hash/lengths and launcher identity, requires the supplied `firstFixtureSequence` to be greater than freeze sequence, and compares the actual closure again before/after each step and final settlement. This is not a claim that the entire task11 regression suite already passed.

`freezePaths` maps every frozen `FileImage.RelativePath` to its exact absolute source: `engine/.` is engineDirectory; `engine/<relative>` is that exact descendant; `runtime/NNN` selects only the corresponding already admitted `externalRuntimeFiles[NNN]`. No arbitrary selector, wildcard or discovery path is accepted. New/missing package inputs are rejected by the existing complete oracle inventory; external runtime closure admission remains root-owned and independently frozen.

Fixed external fixture aliases, introduced only after freeze:

- `<attemptRoot>\fixtures\third-form.pdf`: exact approved blank pin.
- `<attemptRoot>\fixtures\reviewed-draft\catalog.json` and `profile.json`: independently operator-reviewed source pins. They are copied create-new to the separate output reviewed directory immediately before save, retaining original hashes and lengths. Configure's produced draft stays unchanged.

The bundle, fixtures, configuration and evidence roots must be disjoint and native-identity distinct. The adapter creates only `outputs/configuration` and its reviewed copy directory; CLI owns creation of new draft/saved packages and output files. Every created configuration directory is native-identity pinned for subsequent settlement checks.

## Finite plan and exact outcomes

Root passes `plan=New-GenericOnboardingFixedPlan <the fixed paths> '10.0.13'` for independent review. The adapter reconstructs and validates it before any setup write; it never dispatches arbitrary supplied commands. IDs/order/argv/exit/schema/status/state must exactly match:

| ID | Command | Exit | Required successful receipt |
|---|---|---:|---|
| go-check | check original blank | 1 | compatibility/1, configuration_needed, exact template hash/bytes |
| go-configure | configure original blank -> new draft directory | 0 | configuration-result/1, configure/ok/published, two exact payloads, binding not_run |
| go-save-config | save original blank + reviewed copy -> new saved directory, --reviewed | 0 | configuration-result/1, save-config/ok/published, three exact payloads, binding passed |
| go-sample-input | sample-input from saved catalog -> new sample file | 0 | result/2, sample-input/ok/published, data/data.json, data passed; packet/render not_run |
| go-fill-editable | fill original blank + catalog + generated sample -> editable.pdf | 0 | result/2, fill/ok/published, document/document.pdf, exact physical byte/hash match; render passed |
| go-fill-flattened | same inputs -> flattened.pdf plus --flatten | 0 | same strict fill receipt |

Actual aliases are returned by `New-GenericOnboardingFixedPlan`; no caller-chosen seventh command, --force, shell string, renderer or viewer is supported. The existing v5 `New-StartInfo` supplies the sanitized environment, separate state directories and fixed host. The adapter changes only its ArgumentList to the reconstructed fixed array. Before every launch it requires all remaining `N*67000+10000` milliseconds; failure stops before `Start`.

The exact v5 owned state is registered in v5 custody before `Invoke-OwnedProcess`. No independent wait loop/termination implementation exists. An escaped supervisor call may use only the existing one-time exceptional settlement rule; after v5 returns or attempts a kill there is no second kill. Unknown/live custody stops and remains strongly retained. A subsequent slot requires exact process, receipt, completion, original-pin, frozen-closure and native domain oracles to pass.

Configured packages are checked against actual bounded reopened payloads and the **existing public completion reconciler**, including real exact directory membership. Both stdout and committed result must pass. Metadata and payloads become immutable ledger entries. Copied reviewed input, sample, every prior PDF and all written raw/process evidence are checked before/after each later invocation and at final settlement. Expected PDF hashes always come from their original validated fill receipts. The adapter never computes a replacement expected PDF hash after inspection/rendering.

Evidence writes are create-new and bounded. The on-disk terminal is a candidate requiring the matching console final domain observation. No filesystem write follows that final observation. Failure retains source modules, clocks, roots, raw memory streams and exact process/custody references; there is no cleanup/retry/reset API.

## Tests and limitations

Load/register source tests only under a later root grant. Assertion tests remain synthetic and NOT_RUN. Workflow tests include pure finite-plan/reservation/process-state/JSON controls and eight integration scenarios using the real v5 supervisor/oracle/completion API plus the separate `tests/fixture/GenericAdapterFixture.cs` source. Each integration scenario requires a new admitted module/context/attempt in its own persistent test session; no force reload/reset with custody is permitted. Root's test factory provides final fixture build pins, independent pre-introduction freeze/evidence, actual native host/oracle and live resource admission. No fake process/domain oracle may be substituted and called integration evidence.

The fixture deliberately emits nonrendered fictional plumbing bytes and cannot prove PDF validity, rendering, real compatibility or product behavior. Test receipts must retain `acceptance=false`; viewer, per-field/widget, editable save/reopen and visual acceptance remain not_observed. The six CLI plan does not run inspect/render/viewer tools. Those require a separate later pinned bounded plan and must recheck originals against the retained original receipt pins after settlement.
