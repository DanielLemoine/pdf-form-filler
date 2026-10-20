using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PdfForm.Publication;

// Deterministic in-process harness faults. These do not qualify wrapper processes or native races.
internal static class PublicationHarnessTests
{
    private const string PrivateSentinel = "PRIVATE-EXCEPTION-AND-PATH-DO-NOT-LOG";
    private static HarnessCase[] Cases() => [new("inner-first", _ => { }), new("inner-second", _ => { })];
    private static void Check(bool value, string checkpoint)
    {
        if (!value) throw new PublicationAssertionException(checkpoint);
    }
    private static (HarnessOutcome Outcome, string Out, string Error) Run(string[] args, HarnessCase[]? cases = null,
        Action<HarnessPoint, string?>? fault = null, TextWriter? output = null)
    {
        var stdout = output ?? new StringWriter(); var stderr = new StringWriter();
        var outcome = PublicationHarness.Run(args, cases ?? Cases(), stdout, stderr, fault);
        return (outcome, stdout.ToString() ?? "", stderr.ToString());
    }
    private static string Attempt(string parent, HarnessOutcome outcome) => Path.Combine(parent, "attempt-" + outcome.AttemptId);
    private static string[] Entries(string directory) => Directory.GetFileSystemEntries(directory).Order(StringComparer.Ordinal).ToArray();
    private static void PrivateFree(string output, string checkpoint) => Check(!output.Contains(PrivateSentinel, StringComparison.Ordinal), checkpoint);
    private static HarnessTerminal Terminal((HarnessOutcome Outcome, string Out, string Error) run)
    {
        Check(run.Outcome.TerminalWritten && run.Outcome.Terminal is not null, "harness-terminal-present");
        return run.Outcome.Terminal!;
    }

    private static bool Accepted(HarnessTerminal? terminal, int observedExitCode, string expectedAttemptId) =>
        PublicationHarness.IsPassingReceipt(terminal, observedExitCode, ["inner-first", "inner-second"], expectedAttemptId,
            OperatingSystem.IsWindows() ? "windows" : "linux");

    internal static void Register(Action<string, Action<string>> test)
    {
        var invalidArguments = new (string Name, Func<string, string[]> Arguments)[]
        {
            ("missing", _ => []), ("multiple", dir => [dir, dir]), ("blank", _ => [" "]),
            ("relative", _ => ["relative-fixtures"]), ("volume-root", dir => [Path.GetPathRoot(dir)!]),
            ("invalid-segment", dir => [Path.Combine(dir, "bad?segment")]),
            ("device-segment", dir => [Path.Combine(dir, "CON.txt")]),
            ("trailing-dot", dir => [Path.Combine(dir, "child.")]),
            ("double-leading-separator", _ => ["//not-an-approved-host/share/fixtures"])
        };
        foreach (var fixture in invalidArguments)
            test("harness-root-" + fixture.Name + "-zero-writes", dir =>
            {
                var sentinel = Path.Combine(dir, "foreign.txt"); File.WriteAllText(sentinel, PrivateSentinel);
                var before = Entries(dir); int metadata = 0, creates = 0;
                var result = Run(fixture.Arguments(dir), fault: (point, _) =>
                {
                    if (point == HarnessPoint.BeforeRootMetadata) metadata++;
                    if (point == HarnessPoint.BeforeAttemptCreate) creates++;
                });
                Check(result.Outcome.ExitCode == 2 && !result.Outcome.TerminalWritten && result.Outcome.AttemptId is null,
                    "root-refused-before-attempt");
                Check(metadata == 0 && creates == 0 && before.SequenceEqual(Entries(dir)) && File.ReadAllText(sentinel) == PrivateSentinel,
                    "root-refusal-no-metadata-or-writes");
                Check(result.Out.Length == 0 && result.Error.Contains("creation=not_attempted identity=not_attempted terminal=unavailable", StringComparison.Ordinal),
                    "root-refusal-static-status");
                PrivateFree(result.Error, "root-refusal-private-free");
            });

        foreach (var kind in new[] { "missing", "file" })
            test("harness-existing-parent-" + kind + "-refused", dir =>
            {
                string parent = Path.Combine(dir, "candidate");
                if (kind == "file") File.WriteAllText(parent, PrivateSentinel);
                var before = Entries(dir); int creates = 0;
                var result = Run([parent], fault: (point, _) => { if (point == HarnessPoint.BeforeAttemptCreate) creates++; });
                Check(result.Outcome.ExitCode == 2 && result.Error.StartsWith("E_HARNESS_ROOT ", StringComparison.Ordinal), "existing-parent-required");
                Check(creates == 0 && before.SequenceEqual(Entries(dir)), "existing-parent-refusal-no-writes");
                if (kind == "file") Check(File.ReadAllText(parent) == PrivateSentinel, "foreign-root-file-preserved");
            });

        if (OperatingSystem.IsWindows())
        {
            var roots = new (string Name, string Path)[]
            {
                ("unc", @"\\unapproved-host\share\fixtures"), ("extended", @"\\?\C:\fixtures"),
                ("device", @"\\.\C:\fixtures"), ("drive-relative", @"C:fixtures"),
                ("root-without-drive", @"\fixtures"), ("alternate-device", "//?/C:/fixtures")
            };
            foreach (var root in roots)
                test("harness-windows-root-" + root.Name + "-zero-io", dir =>
                {
                    int metadata = 0;
                    var result = Run([root.Path], fault: (point, _) => { if (point == HarnessPoint.BeforeRootMetadata) metadata++; });
                    Check(result.Outcome.ExitCode == 2 && metadata == 0 && Entries(dir).Length == 0 && result.Out.Length == 0,
                        "windows-root-lexical-refusal");
                });
        }
        if (OperatingSystem.IsLinux())
        {
            foreach (var position in new[] { "root", "ancestor" })
                test("harness-link-" + position + "-refused", dir =>
                {
                    var target = Path.Combine(dir, "target"); Directory.CreateDirectory(target);
                    var child = Path.Combine(target, "child"); Directory.CreateDirectory(child);
                    File.WriteAllText(Path.Combine(target, "foreign.txt"), PrivateSentinel);
                    var link = Path.Combine(dir, "alias"); Directory.CreateSymbolicLink(link, target);
                    var before = Entries(target); int creates = 0;
                    var result = Run([position == "root" ? link : Path.Combine(link, "child")], fault: (point, _) =>
                    { if (point == HarnessPoint.BeforeAttemptCreate) creates++; });
                    Check(result.Outcome.ExitCode == 2 && creates == 0 && before.SequenceEqual(Entries(target)) && Entries(child).Length == 0,
                        "link-parent-no-write");
                    Check(File.ReadAllText(Path.Combine(target, "foreign.txt")) == PrivateSentinel, "link-foreign-preserved");
                });
        }

        test("harness-success-exact-terminal-runtime-oracle", dir =>
        {
            // Spaces and Unicode remain valid in an ordinary local parent.
            var parent = Path.Combine(dir, "local fixtures ü"); Directory.CreateDirectory(parent);
            var sentinel = Path.Combine(parent, "foreign.txt"); File.WriteAllText(sentinel, PrivateSentinel);
            var result = Run([parent]); var terminal = Terminal(result);
            Check(result.Outcome.ExitCode == 0 && result.Outcome.CreationState == "created" && result.Outcome.IdentityState == "verified",
                "success-created-verified");
            var lines = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
            Check(lines.Length == 4 && lines[0] == "PDFFORM_TEST_ATTEMPT " + result.Outcome.AttemptId &&
                lines.Count(line => line.StartsWith("PDFFORM_TEST_ATTEMPT ", StringComparison.Ordinal)) == 1, "bootstrap-exact-once-first");
            string token = lines[0]["PDFFORM_TEST_ATTEMPT ".Length..];
            Check(token.Length == 32 && token.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9'), "bootstrap-token-grammar");
            var attempt = Path.Combine(parent, "attempt-" + token);
            Check(Entries(parent).Length == 2 && File.ReadAllText(sentinel) == PrivateSentinel && result.Error.Length == 0, "parent-sentinel-preserved");
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(attempt, "terminal.json")));
            var document = json.RootElement;
            string[] ids = ["inner-first", "inner-second"];
            Check(document.GetProperty("schema").GetString() == "pdfform.publication-tests/1" && document.GetProperty("attemptId").GetString() == token,
                "terminal-schema-bootstrap-binding");
            string[] ReadIds(string name) => document.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();
            Check(ReadIds("selectedCaseIds").SequenceEqual(ids) && ReadIds("executedCaseIds").SequenceEqual(ids) &&
                ReadIds("failedCaseIds").Length == 0 && ReadIds("skippedCaseIds").Length == 0, "terminal-independent-case-sets");
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("inner-first\ninner-second\n"))).ToLowerInvariant();
            Check(document.GetProperty("selectionSha256").GetString() == digest, "terminal-independent-selection-digest");
            var count = document.GetProperty("counts");
            Check(count.GetProperty("selected").GetInt32() == 2 && count.GetProperty("executed").GetInt32() == 2 &&
                count.GetProperty("passed").GetInt32() == 2 && count.GetProperty("failed").GetInt32() == 0 && count.GetProperty("skipped").GetInt32() == 0,
                "terminal-independent-counts");
            Check(document.GetProperty("caseResults").EnumerateArray().Select(item => item.GetProperty("id").GetString()).SequenceEqual(ids) &&
                document.GetProperty("caseResults").EnumerateArray().All(item => item.GetProperty("status").GetString() == "passed" &&
                item.GetProperty("phase").GetString() == "test" && item.GetProperty("bodyStarted").GetBoolean()), "terminal-independent-case-status");
            var runtime = document.GetProperty("runtime");
            Check(runtime.GetProperty("version").GetString() == Environment.Version.ToString() &&
                runtime.GetProperty("frameworkDescription").GetString() == RuntimeInformation.FrameworkDescription &&
                runtime.GetProperty("processArchitecture").GetString() == RuntimeInformation.ProcessArchitecture.ToString() &&
                runtime.GetProperty("osArchitecture").GetString() == RuntimeInformation.OSArchitecture.ToString(), "terminal-actual-runtime");
            Check(Accepted(terminal, 0, result.Outcome.AttemptId!), "terminal-positive-receipt");
        });

        foreach (var kind in new[] { "duplicate", "case-collision", "unsafe-id", "empty" })
            test("harness-selection-" + kind + "-zero-writes", dir =>
            {
                HarnessCase[] cases = kind switch
                {
                    "duplicate" => new HarnessCase[] { new("same", _ => { }), new("same", _ => { }) },
                    "case-collision" => [new("Same", _ => { }), new("same", _ => { })],
                    "unsafe-id" => [new("../escape", _ => { })], _ => []
                };
                int metadata = 0;
                var result = Run([dir], cases, (point, _) => { if (point == HarnessPoint.BeforeRootMetadata) metadata++; });
                Check(result.Outcome.ExitCode == 2 && result.Error.StartsWith("E_HARNESS_SELECTION ", StringComparison.Ordinal) &&
                    metadata == 0 && Entries(dir).Length == 0 && result.Out.Length == 0, "selection-refusal-no-write");
            });

        var setupFaults = new (string Name, HarnessPoint Point, string Creation, string Identity)[]
        {
            ("before-create", HarnessPoint.BeforeCaseCreate, "not_attempted", "not_attempted"),
            ("after-create", HarnessPoint.AfterCaseCreated, "created", "not_attempted"),
            ("before-identity", HarnessPoint.BeforeCaseIdentity, "created", "unavailable"),
            ("before-body", HarnessPoint.BeforeCaseBody, "created", "verified")
        };
        foreach (var fixture in setupFaults)
            test("harness-setup-failure-" + fixture.Name + "-attributed", dir =>
            {
                int first = 0, second = 0;
                var result = Run([dir], [new("inner-first", _ => first++), new("inner-second", _ => second++)], (point, id) =>
                { if (point == fixture.Point && id == "inner-first") throw new IOException(PrivateSentinel); });
                var terminal = Terminal(result); var item = terminal.CaseResults[0];
                Check(result.Outcome.ExitCode == 1 && first == 0 && second == 1, "setup-failure-continues-later-case");
                Check(item.Status == "failed" && item.Phase == "setup" && !item.BodyStarted && item.FailureCode == "E_HARNESS_CASE_SETUP" &&
                    item.DirectoryCreationState == fixture.Creation && item.IdentityState == fixture.Identity, "setup-partial-state-exact");
                Check(terminal.SelectedCaseIds.SequenceEqual(new[] { "inner-first", "inner-second" }) && terminal.ExecutedCaseIds.SequenceEqual(new[] { "inner-second" }) &&
                    terminal.FailedCaseIds.SequenceEqual(new[] { "inner-first" }) && terminal.SkippedCaseIds.Length == 0 &&
                    terminal.Counts == new HarnessCounts(2, 1, 1, 1, 0), "setup-failed-not-executed");
                Check(Directory.Exists(Path.Combine(Attempt(dir, result.Outcome), "inner-first")) == (fixture.Creation == "created"), "setup-residue-preserved");
                PrivateFree(result.Out + result.Error + File.ReadAllText(Path.Combine(Attempt(dir, result.Outcome), "terminal.json")), "setup-diagnostics-private-free");
            });

        test("harness-case-directory-collision-preserved", dir =>
        {
            string? token = null;
            var result = Run([dir], fault: (point, id) =>
            {
                if (point == HarnessPoint.AfterAttemptCreated) token = id;
                if (point == HarnessPoint.BeforeCaseCreate && id == "inner-first")
                {
                    var path = Path.Combine(dir, "attempt-" + token, id); Directory.CreateDirectory(path);
                    File.WriteAllText(Path.Combine(path, "foreign.txt"), PrivateSentinel);
                }
            });
            var terminal = Terminal(result);
            Check(result.Outcome.ExitCode == 1 && terminal.CaseResults[0].DirectoryCreationState == "attempted_unconfirmed" &&
                !terminal.CaseResults[0].BodyStarted && terminal.CaseResults[1].Status == "passed", "case-collision-accounted");
            Check(File.ReadAllText(Path.Combine(Attempt(dir, result.Outcome), "inner-first", "foreign.txt")) == PrivateSentinel,
                "case-collision-foreign-preserved");
        });

        foreach (var kind in new[] { "exception", "assertion" })
            test("harness-body-" + kind + "-static-diagnostic", dir =>
            {
                var result = Run([dir], [new("inner-first", _ =>
                {
                    if (kind == "assertion") throw new PublicationAssertionException("harness-known-checkpoint");
                    throw new IOException(PrivateSentinel);
                })]);
                var terminal = Terminal(result); var item = terminal.CaseResults[0];
                Check(result.Outcome.ExitCode == 1 && item.BodyStarted && item.Phase == "test" && terminal.ExecutedCaseIds.SequenceEqual(new[] { "inner-first" }),
                    "body-failure-counted-executed");
                Check(item.FailureCode == (kind == "assertion" ? "E_HARNESS_ASSERTION" : "E_HARNESS_CASE_EXECUTION") &&
                    item.Checkpoint == (kind == "assertion" ? "harness-known-checkpoint" : null), "body-static-code-only");
                PrivateFree(result.Out + result.Error + File.ReadAllText(Path.Combine(Attempt(dir, result.Outcome), "terminal.json")), "body-diagnostics-private-free");
            });

        test("harness-infrastructure-abort-selected-skipped", dir =>
        {
            int second = 0;
            var result = Run([dir], [new("inner-first", _ => { }), new("inner-second", _ => second++)], (point, id) =>
            { if (point == HarnessPoint.AfterCaseRecorded && id == "inner-first") throw new IOException(PrivateSentinel); });
            var terminal = Terminal(result);
            Check(result.Outcome.ExitCode == 4 && second == 0 && terminal.RunFailureCode == "E_HARNESS_INTERRUPTED" &&
                terminal.Counts == new HarnessCounts(2, 1, 1, 0, 1), "abort-counts-exact");
            Check(terminal.CaseResults[1] == new HarnessCaseResult("inner-second", "skipped", "not_started", "not_attempted", "not_attempted", false, "E_HARNESS_ABORTED") &&
                !Directory.Exists(Path.Combine(Attempt(dir, result.Outcome), "inner-second")), "abort-selected-not-started");
            PrivateFree(result.Out + result.Error, "abort-private-free");
        });

        test("harness-attempt-create-failure-no-marker", dir =>
        {
            var result = Run([dir], fault: (point, _) => { if (point == HarnessPoint.BeforeAttemptCreate) throw new IOException(PrivateSentinel); });
            Check(result.Outcome.ExitCode == 4 && result.Outcome.CreationState == "not_attempted" && !result.Outcome.TerminalWritten &&
                Entries(dir).Length == 0 && result.Out.Length == 0 && result.Error.StartsWith("E_HARNESS_ATTEMPT_CREATE ", StringComparison.Ordinal), "attempt-create-not-started");
            PrivateFree(result.Error, "attempt-create-private-free");
        });
        test("harness-attempt-collision-no-overwrite", dir =>
        {
            var result = Run([dir], fault: (point, id) =>
            {
                if (point != HarnessPoint.BeforeAttemptCreate) return;
                var child = Path.Combine(dir, "attempt-" + id); Directory.CreateDirectory(child);
                File.WriteAllText(Path.Combine(child, "foreign.txt"), PrivateSentinel);
            });
            Check(result.Outcome.ExitCode == 4 && result.Outcome.CreationState == "attempted_unconfirmed" && result.Out.Length == 0 &&
                !result.Outcome.TerminalWritten && Entries(dir).Length == 1, "attempt-collision-no-success-marker");
            var child = Attempt(dir, result.Outcome);
            Check(Entries(child).Length == 1 && File.ReadAllText(Path.Combine(child, "foreign.txt")) == PrivateSentinel, "attempt-collision-preserved");
        });
        foreach (var kind in new[] { "after-create", "before-identity", "renamed" })
            test("harness-attempt-" + kind + "-unverified-preserved", dir =>
            {
                var pointToFail = kind == "before-identity" ? HarnessPoint.BeforeAttemptIdentity : HarnessPoint.AfterAttemptCreated;
                var result = Run([dir], fault: (point, id) =>
                {
                    if (point != pointToFail) return;
                    if (kind == "renamed") Directory.Move(Path.Combine(dir, "attempt-" + id), Path.Combine(dir, "renamed-created-child"));
                    throw new IOException(PrivateSentinel);
                });
                Check(result.Outcome.ExitCode == 4 && result.Outcome.CreationState == "created" && !result.Outcome.TerminalWritten && result.Outcome.Terminal is null,
                    "unverified-attempt-failure-honest");
                Check(result.Outcome.IdentityState == (kind == "before-identity" ? "unavailable" : "not_attempted") &&
                    result.Out.TrimEnd() == "PDFFORM_TEST_ATTEMPT " + result.Outcome.AttemptId, "unverified-created-marker-retained");
                var preserved = kind == "renamed" ? Path.Combine(dir, "renamed-created-child") : Attempt(dir, result.Outcome);
                Check(Entries(dir).Length == 1 && Directory.Exists(preserved) && Entries(preserved).Length == 0, "unverified-residue-no-terminal-fallback");
                PrivateFree(result.Error, "unverified-diagnostics-private-free");
            });
        test("harness-bootstrap-write-failure-preserves-created-child", dir =>
        {
            var writer = new FailingWriter(1);
            var result = Run([dir], output: writer);
            Check(result.Outcome.ExitCode == 4 && writer.Attempts == 1 && result.Outcome.CreationState == "created" &&
                result.Outcome.IdentityState == "not_attempted" && !result.Outcome.TerminalWritten, "bootstrap-uncertain-no-retry");
            Check(Directory.Exists(Attempt(dir, result.Outcome)) && Entries(Attempt(dir, result.Outcome)).Length == 0 &&
                result.Error.StartsWith("E_HARNESS_BOOTSTRAP_WRITE ", StringComparison.Ordinal), "bootstrap-failure-no-case-or-terminal");
            PrivateFree(result.Error, "bootstrap-private-free");
        });
        test("harness-bootstrap-flush-before-identity", dir =>
        {
            var writer = new FlushWriter(false); bool observed = false;
            var result = Run([dir], fault: (point, token) =>
            {
                if (point != HarnessPoint.AfterAttemptCreated) return;
                observed = true;
                Check(writer.Flushes == 1 && writer.ToString().TrimEnd() == "PDFFORM_TEST_ATTEMPT " + token,
                    "bootstrap-flushed-before-identity-or-case");
            }, output: writer);
            Check(observed && result.Outcome.ExitCode == 0 && result.Outcome.TerminalWritten, "bootstrap-flush-positive");
        });
        test("harness-bootstrap-flush-failure-no-retry", dir =>
        {
            var writer = new FlushWriter(true); var result = Run([dir], output: writer);
            Check(result.Outcome.ExitCode == 4 && writer.Flushes == 1 && result.Outcome.CreationState == "created" &&
                result.Outcome.IdentityState == "not_attempted" && !result.Outcome.TerminalWritten &&
                writer.ToString().TrimEnd() == "PDFFORM_TEST_ATTEMPT " + result.Outcome.AttemptId, "bootstrap-flush-uncertain-nonzero");
            Check(Entries(Attempt(dir, result.Outcome)).Length == 0 && result.Error.StartsWith("E_HARNESS_BOOTSTRAP_WRITE ", StringComparison.Ordinal),
                "bootstrap-flush-no-fallback");
            PrivateFree(result.Error, "bootstrap-flush-private-free");
        });
        foreach (var write in new[] { 2, 4 })
            test("harness-log-write-" + (write == 2 ? "case" : "summary") + "-failure-terminal", dir =>
            {
                var result = Run([dir], output: new FailingWriter(write)); var terminal = Terminal(result);
                Check(result.Outcome.ExitCode == 4 && terminal.RunFailureCode == "E_HARNESS_LOG_WRITE" && terminal.Status == "failed", "log-failure-nonzero-terminal");
                Check(terminal.Counts == (write == 2 ? new HarnessCounts(2, 1, 1, 0, 1) : new HarnessCounts(2, 2, 2, 0, 0)), "log-failure-accurate-accounting");
                PrivateFree(result.Error + File.ReadAllText(Path.Combine(Attempt(dir, result.Outcome), "terminal.json")), "log-failure-private-free");
            });

        foreach (var kind in new[] { "exception", "collision" })
            test("harness-terminal-write-" + kind + "-nonzero", dir =>
            {
                string? token = null;
                var result = Run([dir], fault: (point, id) =>
                {
                    if (point == HarnessPoint.AfterAttemptCreated) token = id;
                    if (point != HarnessPoint.BeforeTerminalWrite) return;
                    if (kind == "collision") File.WriteAllText(Path.Combine(dir, "attempt-" + token, "terminal.json"), PrivateSentinel);
                    else throw new IOException(PrivateSentinel);
                });
                Check(result.Outcome.ExitCode == 4 && !result.Outcome.TerminalWritten && result.Outcome.Terminal?.Counts.Passed == 2 &&
                    result.Error.StartsWith("E_HARNESS_MANIFEST_WRITE ", StringComparison.Ordinal) && result.Error.Contains("terminal=write_failed", StringComparison.Ordinal),
                    "terminal-write-failure-not-pass");
                var path = Path.Combine(Attempt(dir, result.Outcome), "terminal.json");
                Check(Entries(dir).Length == 1 && Directory.GetFiles(Attempt(dir, result.Outcome)).Length == (kind == "collision" ? 1 : 0), "terminal-no-alternate-file");
                if (kind == "collision") Check(File.ReadAllText(path) == PrivateSentinel, "terminal-collision-preserved");
                else Check(!File.Exists(path), "terminal-injected-failure-absent");
                PrivateFree(result.Error, "terminal-write-private-free");
            });

        test("harness-attempt-identity-change-refuses-terminal", dir =>
        {
            string? token = null;
            var result = Run([dir], fault: (point, id) =>
            {
                if (point == HarnessPoint.AfterAttemptCreated) token = id;
                if (point != HarnessPoint.AfterCaseRecorded || id != "inner-second") return;
                var original = Path.Combine(dir, "attempt-" + token);
                Directory.Move(original, Path.Combine(dir, "retained-original")); Directory.CreateDirectory(original);
                File.WriteAllText(Path.Combine(original, "foreign.txt"), PrivateSentinel);
            });
            Check(result.Outcome.ExitCode == 4 && !result.Outcome.TerminalWritten && result.Error.StartsWith("E_HARNESS_ATTEMPT_CHANGED ", StringComparison.Ordinal),
                "attempt-change-detected-at-check");
            Check(File.ReadAllText(Path.Combine(Attempt(dir, result.Outcome), "foreign.txt")) == PrivateSentinel &&
                !File.Exists(Path.Combine(Attempt(dir, result.Outcome), "terminal.json")) && !File.Exists(Path.Combine(dir, "retained-original", "terminal.json")),
                "attempt-change-no-fallback-writes");
        });

        var mutations = new (string Name, Func<HarnessTerminal, HarnessTerminal> Mutate)[]
        {
            ("missing-result", t => t with { CaseResults = t.CaseResults[..1] }),
            ("duplicate-result", t => t with { CaseResults = [t.CaseResults[0], t.CaseResults[0]] }),
            ("unexpected-result", t => t with { CaseResults = [t.CaseResults[0], t.CaseResults[1] with { Id = "unexpected" }] }),
            ("duplicate-selection", t => t with
            {
                SelectedCaseIds = ["inner-first", "inner-first"], ExecutedCaseIds = ["inner-first", "inner-first"],
                CaseResults = [t.CaseResults[0], t.CaseResults[0]], Counts = new(2, 2, 2, 0, 0),
                SelectionSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("inner-first\ninner-first\n"))).ToLowerInvariant()
            }),
            ("omitted-execution", t => t with { ExecutedCaseIds = ["inner-first"] }),
            ("count-mismatch", t => t with { Counts = t.Counts with { Passed = 1 } }),
            ("selection-digest", t => t with { SelectionSha256 = new string('0', 64) }),
            ("unknown-status", t => t with { CaseResults = [t.CaseResults[0] with { Status = "unknown" }, t.CaseResults[1]] }),
            ("body-not-entered", t => t with { CaseResults = [t.CaseResults[0] with { BodyStarted = false }, t.CaseResults[1]] }),
            ("runtime-missing", t => t with { Runtime = t.Runtime with { Version = "" } }),
            ("failure-code-unknown", t => t with { RunFailureCode = PrivateSentinel, Status = "failed", RequestedExitCode = 4 }),
            ("attempt-token", t => t with { AttemptId = "invalid-token" })
        };
        foreach (var fixture in mutations)
            test("harness-terminal-" + fixture.Name + "-rejected", dir =>
            {
                var result = Run([dir]); var terminal = fixture.Mutate(Terminal(result));
                if (fixture.Name == "duplicate-selection")
                {
                    Check(terminal.SelectedCaseIds.SequenceEqual(terminal.CaseResults.Select(item => item.Id)) &&
                        terminal.SelectedCaseIds.SequenceEqual(terminal.ExecutedCaseIds) && terminal.CaseResults.All(item => item.Status == "passed" && item.BodyStarted) &&
                        terminal.Counts == new HarnessCounts(2, 2, 2, 0, 0) && terminal.SelectionSha256 ==
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("inner-first\ninner-first\n"))).ToLowerInvariant(),
                        "duplicate-selection-other-invariants-valid");
                }
                Check(!PublicationHarness.ValidTerminal(terminal) && !Accepted(terminal, 0, result.Outcome.AttemptId!), "invalid-accounting-not-pass");
                var attempt = Attempt(dir, result.Outcome); var before = File.ReadAllBytes(Path.Combine(attempt, "terminal.json")); int writes = 0;
                try
                {
                    PublicationHarness.FinalizeTerminal(attempt, NativeFileSystem.Identity(attempt), terminal,
                        (point, _) => { if (point == HarnessPoint.BeforeTerminalWrite) writes++; });
                    throw new PublicationAssertionException("invalid-terminal-finalized");
                }
                catch (HarnessFailure error) { Check(error.Error == HarnessError.Accounting, "invalid-terminal-accounting-guard"); }
                Check(writes == 0 && before.SequenceEqual(File.ReadAllBytes(Path.Combine(attempt, "terminal.json"))), "invalid-terminal-refused-before-write");
            });
        test("harness-self-consistent-omitted-selection-rejected", dir =>
        {
            var result = Run([dir]); var terminal = Terminal(result);
            // Internally consistent receipt still cannot redefine the independently frozen selection.
            var shortened = terminal with
            {
                SelectedCaseIds = ["inner-first"], ExecutedCaseIds = ["inner-first"], CaseResults = [terminal.CaseResults[0]],
                Counts = new(1, 1, 1, 0, 0), SelectionSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("inner-first\n"))).ToLowerInvariant()
            };
            Check(PublicationHarness.ValidTerminal(shortened) && !Accepted(shortened, 0, result.Outcome.AttemptId!), "external-selection-authority-required");
            Check(!Accepted(terminal, 0, new string('0', 32)), "external-attempt-authority-required");
            Check(!PublicationHarness.IsPassingReceipt(terminal, 0, ["inner-first", "inner-second"], result.Outcome.AttemptId!,
                OperatingSystem.IsWindows() ? "linux" : "windows"), "external-platform-authority-required");
        });
        test("harness-zero-exit-without-terminal-rejected", dir =>
        {
            Check(!Accepted(null, 0, new string('0', 32)), "zero-exit-no-terminal-not-pass");
            var result = Run([dir]); var terminal = Terminal(result);
            Check(!Accepted(terminal, 4, result.Outcome.AttemptId!), "terminal-with-nonzero-exit-not-pass");
        });
    }

    private sealed class FlushWriter(bool fail) : StringWriter
    {
        internal int Flushes { get; private set; }
        public override void Flush()
        {
            Flushes++;
            if (fail) throw new IOException(PrivateSentinel);
            base.Flush();
        }
    }
    private sealed class FailingWriter(int failureAttempt) : StringWriter
    {
        internal int Attempts { get; private set; }
        public override void WriteLine(string? value)
        {
            Attempts++;
            if (Attempts == failureAttempt) throw new IOException(PrivateSentinel);
            base.WriteLine(value);
        }
    }
}
