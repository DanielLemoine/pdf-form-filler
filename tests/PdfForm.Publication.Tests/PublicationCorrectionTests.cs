using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfForm.Publication;

// Focused A1–A3 cases. The byte oracle and golden wire records do not call production
// descriptor/reconciliation helpers. Windows-only cases remain a separate runtime selection.
internal static class PublicationCorrectionTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("tiny-artifact");
    private static readonly PublicationLimits Limits = new(64, 32768, 4, 8192);
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions PrettyCamel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static void Run(Action<string, Action<string>> test)
    {
        foreach (var kind in new[] { "single", "report", "package" })
            foreach (bool moved in new[] { false, true })
                test($"correction-created-root-{kind}-{(moved ? "moved" : "present")}", dir =>
                {
                    int roots = 0; string? original = null, residue = null;
                    var publisher = new SafePublisher(Limits, null, point =>
                    {
                        if (point != StageCreationPoint.RootDirectory || ++roots != (kind == "report" ? 2 : 1)) return;
                        original = Directory.GetDirectories(dir, ".pdfform-stage-*").Single(path => !Directory.EnumerateFileSystemEntries(path).Any());
                        residue = original;
                        if (moved) { residue = Path.Combine(dir, "relocated-unverified-root"); Directory.Move(original, residue); }
                        throw new IOException("PRIVATE-CREATION-SENTINEL");
                    });
                    var singleRequest = new SingleFileRequest(Path.Combine(dir, "out.pdf"), [], Candidate());
                    if (kind == "report") singleRequest = singleRequest with
                    { ResultPath = Path.Combine(dir, "report.json"), ResultBytes = Encoding.UTF8.GetBytes("{}") };
                    var outcome = kind == "package" ? publisher.PublishPackage(Request(dir)) : publisher.PublishSingle(singleRequest);
                    Check(original is not null && residue is not null, "creation-seam-reached");
                    Check(!outcome.Success && outcome.ArtifactState == "staged" && outcome.CleanupIncomplete, "creation-residue-reported");
                    Check(!Directory.Exists(Path.Combine(dir, "published")) && !File.Exists(Path.Combine(dir, "out.pdf")) &&
                        !File.Exists(Path.Combine(dir, "report.json")), "creation-no-commit");
                    Check(Directory.Exists(residue!) && !Directory.EnumerateFileSystemEntries(residue!).Any(), "creation-residue-inventory");
                    Check(Directory.GetFileSystemEntries(dir).Length == 1, "creation-other-stage-cleaned");
                    Check(!moved || !Directory.Exists(original!), "creation-original-name-absent");
                    Check(!JsonSerializer.Serialize(outcome).Contains("PRIVATE-CREATION-SENTINEL"), "creation-value-free-diagnostic");
                });

        foreach (bool moved in new[] { false, true })
            test("correction-created-child-directory-" + (moved ? "moved" : "present"), dir =>
            {
                string? root = null, original = null, residue = null;
                var outcome = new SafePublisher(Limits, null, point =>
                {
                    if (point != StageCreationPoint.ChildDirectory) return;
                    root = Directory.GetDirectories(dir, ".pdfform-stage-*").Single();
                    original = Path.Combine(root, "Forms"); residue = original;
                    if (moved) { residue = Path.Combine(dir, "relocated-unverified-child"); Directory.Move(original, residue); }
                    throw new IOException();
                }).PublishPackage(Request(dir, artifacts: [Candidate(path: "Forms/a.pdf")]));
                Check(root is not null && original is not null && residue is not null, "child-directory-seam-reached");
                Check(!outcome.Success && outcome.CleanupIncomplete && outcome.ArtifactState == "staged", "child-directory-residue-reported");
                Check(Directory.Exists(root!) && Directory.Exists(residue!) && !Directory.EnumerateFileSystemEntries(residue!).Any(), "child-directory-preserved");
                Check(!moved || !Directory.Exists(original!), "child-directory-original-absent");
                Check(!Directory.Exists(Path.Combine(dir, "published")), "child-directory-no-commit");
            });

        test("correction-created-file-identity-failure", dir =>
        {
            string? file = null;
            var outcome = new SafePublisher(Limits, null, point =>
            {
                if (point != StageCreationPoint.File) return;
                file = Directory.GetFiles(Directory.GetDirectories(dir, ".pdfform-stage-*").Single(), "*", SearchOption.AllDirectories).Single();
                throw new IOException();
            }).PublishPackage(Request(dir, artifacts: [Candidate(path: "Forms/a.pdf")]));
            Check(file is not null && File.Exists(file) && new FileInfo(file).Length == 0, "unverified-file-preserved-before-write");
            Check(!outcome.Success && outcome.CleanupIncomplete && outcome.ArtifactState == "staged" &&
                !Directory.Exists(Path.Combine(dir, "published")), "unverified-file-no-commit");
        });

        test("correction-owned-file-absent-is-uncertain", dir =>
        {
            string? moved = null;
            var outcome = new SafePublisher(Limits, point =>
            {
                if (point != PublicationPoint.BeforeCommit) return;
                var stage = Directory.GetDirectories(dir, ".pdfform-stage-*").Single();
                moved = Path.Combine(dir, "relocated-owned.pdf"); File.Move(Path.Combine(stage, "a.pdf"), moved);
            }).PublishSingle(new(Path.Combine(dir, "out.pdf"), [], Candidate()));
            Check(!outcome.Success && outcome.CleanupIncomplete && outcome.ArtifactState == "staged", "absent-owned-file-uncertainty");
            Check(moved is not null && File.ReadAllBytes(moved).SequenceEqual(Payload) && !File.Exists(Path.Combine(dir, "out.pdf")), "absent-owned-file-preserved");
        });

        test("correction-independent-manifest-caps-package", dir =>
        {
            var request = Request(dir); var outcome = new SafePublisher(Limits).PublishPackage(request);
            Check(outcome.Success && !outcome.CleanupIncomplete, "small-artifact-package-success");
            Check(outcome.Outputs.SequenceEqual(new[] { Describe("artifact", "a.pdf", Payload) }), "returned-descriptor-matches-original-bytes");
            foreach (var name in new[] { "result.json", "completion.json" })
                Check(new FileInfo(Path.Combine(request.Destination, name)).Length > Limits.MaxArtifactBytes, "manifest-exceeds-artifact-cap");
            VerifyPackage(request.Destination, request.RequestId, request.ToolBuild, outcome.Outputs);
            NoStages(dir);
        });

        test("correction-artifact-and-result-exact-caps", dir =>
        {
            var result = Result("request-1", "synthetic-build", [Describe("artifact", "a.pdf", Payload)]);
            var exact = Limits with { MaxArtifactBytes = Payload.Length, MaxManifestBytes = result.Length };
            var request = Request(dir); var outcome = new SafePublisher(exact).PublishPackage(request);
            Check(outcome.Success, "artifact-and-result-exact-caps-pass");
            VerifyPackage(request.Destination, request.RequestId, request.ToolBuild, outcome.Outputs); NoStages(dir);
        });

        test("correction-single-nested-transfer-cleanup", dir =>
        {
            var outcome = new SafePublisher(Limits).PublishSingle(new(Path.Combine(dir, "out.pdf"), [], Candidate(path: "Forms/a.pdf")));
            Check(outcome.Success && !outcome.CleanupIncomplete && File.ReadAllBytes(Path.Combine(dir, "out.pdf")).SequenceEqual(Payload), "nested-transfer-journal-matches-written-path");
            NoStages(dir);
        });

        test("correction-independent-manifest-caps-standalone", dir =>
        {
            var report = Result("request-1", "synthetic-build", [Describe("artifact", "out.pdf", Payload)]);
            Check(report.Length > Limits.MaxArtifactBytes && report.Length <= Limits.MaxManifestBytes, "standalone-larger-valid-report");
            var outcome = new SafePublisher(Limits).PublishSingle(new(Path.Combine(dir, "out.pdf"), [], Candidate(),
                ResultPath: Path.Combine(dir, "report.json"), ResultBytes: report));
            Check(outcome.Success && !outcome.CleanupIncomplete && File.ReadAllBytes(Path.Combine(dir, "report.json")).SequenceEqual(report), "standalone-independent-manifest-cap");
            NoStages(dir);
        });

        foreach (var kind in new[] { "artifact", "result", "report" })
            test("correction-one-byte-over-" + kind + "-cap", dir =>
            {
                var descriptor = Describe("artifact", "a.pdf", Payload);
                var result = Result("request-1", "synthetic-build", [descriptor]);
                var limits = kind == "artifact" ? Limits with { MaxArtifactBytes = Payload.Length - 1 } :
                    Limits with { MaxManifestBytes = result.Length - 1 };
                var publisher = new SafePublisher(limits);
                var outcome = kind == "report" ? publisher.PublishSingle(new(Path.Combine(dir, "out.pdf"), [], Candidate(),
                    ResultPath: Path.Combine(dir, "report.json"), ResultBytes: result)) : publisher.PublishPackage(Request(dir));
                Check(!outcome.Success && outcome.ErrorCode == "E_OUTPUT_LIMIT" && !outcome.CleanupIncomplete, "one-byte-over-refused");
                Check(!Directory.Exists(Path.Combine(dir, "published")) && !File.Exists(Path.Combine(dir, "out.pdf")), "one-byte-over-no-commit");
                NoStages(dir);
            });

        test("correction-completion-cap-exact-and-one-over", dir =>
        {
            var artifacts = Enumerable.Range(0, 16).Select(i => Candidate("artifact" + i, "a" + i + ".pdf")).ToArray();
            var descriptors = artifacts.Select(a => Describe(a.Id, a.Path, Payload)).ToArray();
            var result = Result("request-1", "synthetic-build", descriptors);
            var completion = Completion("request-1", "synthetic-build", result, descriptors);
            Check(completion.Length > result.Length, "completion-cap-fixture-discriminates");
            var exact = Limits with { MaxArtifacts = 16, MaxManifestBytes = completion.Length };
            var good = Request(dir, artifacts: artifacts) with { Destination = Path.Combine(dir, "exact") };
            Check(new SafePublisher(exact).PublishPackage(good).Success, "completion-exact-cap-passes");
            VerifyPackage(good.Destination, good.RequestId, good.ToolBuild, descriptors);
            int reached = 0;
            var bad = Request(dir, artifacts: artifacts);
            var outcome = new SafePublisher(exact with { MaxManifestBytes = completion.Length - 1 }, point =>
                { if (point == PublicationPoint.BeforeCompletionWrite) reached++; }).PublishPackage(bad);
            Check(reached == 1 && !outcome.Success && outcome.ErrorCode == "E_MANIFEST_LIMIT", "completion-one-over-specific-guard");
            Check(!Directory.Exists(bad.Destination), "completion-one-over-no-commit"); NoStages(dir);
        });

        foreach (bool over in new[] { false, true })
            test("correction-package-reservation-" + (over ? "one-over" : "exact"), dir =>
            {
                long planned = Payload.LongLength + 2L * Limits.MaxManifestBytes;
                var limits = Limits with { MaxTotalBytes = planned - (over ? 1 : 0) };
                int stages = 0;
                var request = Request(dir);
                var outcome = new SafePublisher(limits, point => { if (point == PublicationPoint.StageCreated) stages++; }).PublishPackage(request);
                Check(outcome.Success == !over, "reserved-total-admission");
                if (over) Check(stages == 0 && outcome.ErrorCode == "E_OUTPUT_LIMIT", "reserved-total-refused-before-stage");
                else VerifyPackage(request.Destination, request.RequestId, request.ToolBuild, outcome.Outputs);
                NoStages(dir);
            });

        foreach (bool over in new[] { false, true })
            test("correction-standalone-aggregate-" + (over ? "one-over" : "exact"), dir =>
            {
                var report = Result("request-1", "synthetic-build", [Describe("artifact", "out.pdf", Payload)]);
                var limits = Limits with { MaxTotalBytes = Payload.LongLength + report.Length - (over ? 1 : 0) };
                var outcome = new SafePublisher(limits).PublishSingle(new(Path.Combine(dir, "out.pdf"), [], Candidate(),
                    ResultPath: Path.Combine(dir, "report.json"), ResultBytes: report));
                Check(outcome.Success == !over, "standalone-total-admission");
                if (over) Check(outcome.ErrorCode == "E_OUTPUT_LIMIT" && !File.Exists(Path.Combine(dir, "out.pdf")), "standalone-total-refusal");
                NoStages(dir);
            });

        foreach (var mutation in new[] { "artifact-and-records", "result-metadata", "completion-whitespace", "artifact-only-equal-length" })
            test("correction-producer-anchor-" + mutation, dir =>
            {
                int verified = 0, mutated = 0;
                var artifact = Candidate() with { Verify = stream => { verified++; return ReadAll(stream).SequenceEqual(Payload); } };
                var request = Request(dir, artifacts: [artifact]);
                var outcome = new SafePublisher(Limits, point =>
                {
                    if (point != PublicationPoint.BeforeCommit) return;
                    var stage = Directory.GetDirectories(dir, ".pdfform-stage-*").Single();
                    var names = new[] { "a.pdf", "result.json", "completion.json" };
                    var identities = names.ToDictionary(name => name, name => NativeFileSystem.Identity(Path.Combine(stage, name)));
                    var completionPath = Path.Combine(stage, "completion.json");
                    var resultPath = Path.Combine(stage, "result.json");
                    var result = JsonNode.Parse(File.ReadAllBytes(resultPath))!;
                    var completion = JsonNode.Parse(File.ReadAllBytes(completionPath))!;
                    var current = new[] { Describe("artifact", "a.pdf", Payload) };
                    if (mutation is "artifact-and-records" or "artifact-only-equal-length")
                    {
                        var bytes = Payload.ToArray(); bytes[0] ^= 1;
                        File.WriteAllBytes(Path.Combine(stage, "a.pdf"), bytes);
                        current = [Describe("artifact", "a.pdf", bytes)];
                        if (mutation == "artifact-and-records")
                        {
                            result["outputs"]![0]!["sha256"] = current[0].Sha256;
                            completion["artifacts"]![0]!["sha256"] = current[0].Sha256;
                        }
                    }
                    else if (mutation == "result-metadata") result["tool"]!["sourceReference"] = "different-valid-source";
                    if (mutation is "artifact-and-records" or "result-metadata")
                    {
                        var bytes = Encoding.UTF8.GetBytes(result.ToJsonString()); File.WriteAllBytes(resultPath, bytes);
                        completion["result"]!["bytes"] = bytes.Length; completion["result"]!["sha256"] = Hash(bytes);
                        File.WriteAllText(completionPath, completion.ToJsonString());
                    }
                    if (mutation == "completion-whitespace")
                    {
                        var before = File.ReadAllText(completionPath); File.WriteAllText(completionPath, before + "\n ");
                        Check(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(File.ReadAllText(completionPath))), "completion-semantics-unchanged");
                    }
                    foreach (var name in names) Check(NativeFileSystem.Identity(Path.Combine(stage, name)) == identities[name], "same-object-rewrite");
                    if (mutation != "artifact-only-equal-length") VerifyPackage(stage, request.RequestId, request.ToolBuild, current);
                    mutated++;
                }).PublishPackage(request);
                Check(verified == 1 && mutated == 1, "original-verifier-and-mutation-reached");
                Check(!outcome.Success && outcome.ErrorCode == "E_ARTIFACT_INTEGRITY" && !outcome.CleanupIncomplete &&
                    outcome.Outputs.Count == 0 && !Directory.Exists(request.Destination), "producer-anchor-refusal");
                NoStages(dir);
            });

        var casing = new[]
        {
            ("root-forward", "Forms/a.pdf", "forms/b.pdf"),
            ("root-reverse", "forms/b.pdf", "Forms/a.pdf"),
            ("deep-forward", "Forms/Sub/a.pdf", "Forms/sub/b.pdf"),
            ("deep-reverse", "Forms/sub/b.pdf", "Forms/Sub/a.pdf"),
            ("deep-parent", "Forms/Sub/a.pdf", "forms/Sub/b.pdf")
        };
        foreach (var (name, first, second) in casing)
            test("correction-prefix-case-" + name, dir =>
            {
                int pathAccesses = 0, stages = 0;
                var request = Request(dir, artifacts: [Candidate("a", first), Candidate("b", second)]);
                var outcome = new SafePublisher(Limits, point => { if (point == PublicationPoint.StageCreated) stages++; },
                    beforePathAccess: () => pathAccesses++).PublishPackage(request);
                Check(!outcome.Success && outcome.ErrorCode == "E_OUTPUT_SET" && stages == 0 && pathAccesses == 0, "prefix-refused-before-access");
                Check(!Directory.EnumerateFileSystemEntries(dir).Any(), "prefix-refusal-zero-writes");
            });
        test("correction-prefix-case-consistent-positive", dir =>
        {
            var request = Request(dir, artifacts: [Candidate("a", "Forms/Sub/a.pdf"), Candidate("b", "Forms/Sub/b.pdf")]);
            var outcome = new SafePublisher(Limits).PublishPackage(request);
            Check(outcome.Success, "consistent-prefix-admitted"); VerifyPackage(request.Destination, request.RequestId, request.ToolBuild, outcome.Outputs); NoStages(dir);
        });

        var nativeErrors = new[]
        {
            ("windows-file-exists", NativeErrorNamespace.Windows, 80, true),
            ("windows-already-exists", NativeErrorNamespace.Windows, 183, true),
            ("windows-different-device", NativeErrorNamespace.Windows, 17, false),
            ("windows-access", NativeErrorNamespace.Windows, 5, false),
            ("windows-sharing", NativeErrorNamespace.Windows, 32, false),
            ("linux-exists", NativeErrorNamespace.Linux, 17, true),
            ("linux-cross-device", NativeErrorNamespace.Linux, 18, false),
            ("linux-access", NativeErrorNamespace.Linux, 13, false),
            ("linux-not-windows-80", NativeErrorNamespace.Linux, 80, false),
            ("linux-not-windows-183", NativeErrorNamespace.Linux, 183, false)
        };
        foreach (var (name, ns, error, collision) in nativeErrors)
            test("correction-native-error-" + name, _ =>
            {
                var result = NativeFileSystem.CommitError(ns, error);
                Check(result.Code == (collision ? "E_DESTINATION_EXISTS" : "E_COMMIT") && result.ExitCode == (collision ? 2 : 4), "os-specific-error-classification");
            });

        foreach (var (name, build, valid) in new[]
        {
            ("length-255", new string('b', 255), true), ("length-256", new string('b', 256), true),
            ("length-257", new string('b', 257), false), ("whitespace", " \t ", false), ("empty", "", false)
        })
            test("correction-build-policy-" + name, dir =>
            {
                var request = Request(dir, build);
                var outcome = new SafePublisher(Limits).PublishPackage(request);
                Check(outcome.Success == valid, "producer-build-admission");
                if (!valid) Check(outcome.ErrorCode == "E_REQUEST_ID", "producer-build-refusal-code");
                var golden = Path.Combine(dir, "golden"); WriteGolden(golden, "request-1", build);
                var recovered = CompletionProtocol.Reconcile(golden, "request-1", build, [new("artifact", "a.pdf")], Limits);
                Check(recovered.IsComplete == valid, "consumer-build-admission");
                if (!valid) Check(recovered.ErrorCode == "E_COMPLETION_ID", "consumer-build-refusal-code");
                NoStages(dir);
            });
        test("correction-build-policy-mismatch", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Result("request-1", "other-build", outputs) };
            Check(new SafePublisher(Limits).PublishPackage(request).ErrorCode == "E_RESULT_CONTRACT", "producer-build-mismatch");
            var golden = Path.Combine(dir, "golden"); WriteGolden(golden, "request-1", "synthetic-build");
            Check(CompletionProtocol.Reconcile(golden, "request-1", "other-build", [new("artifact", "a.pdf")], Limits).ErrorCode == "E_COMPLETION_ID", "consumer-build-mismatch");
            NoStages(dir);
        });

        test("correction-local-space-unicode-control", dir =>
        {
            var parent = Path.Combine(dir, "local space é文"); Directory.CreateDirectory(parent);
            var input = Path.Combine(parent, "input ü.pdf"); File.WriteAllBytes(input, Payload);
            int pathAccesses = 0;
            var outcome = new SafePublisher(Limits, null, beforePathAccess: () => pathAccesses++).PublishSingle(
                new(Path.Combine(parent, "output 文.pdf"), [input], Candidate()));
            Check(outcome.Success && pathAccesses == 1 && File.ReadAllBytes(input).SequenceEqual(Payload), "local-unicode-admitted-and-input-preserved");
            NoStages(parent);
        });
        test("correction-relative-local-control", dir =>
        {
            var destination = Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(dir, "out.pdf"));
            Check(new SafePublisher(Limits).PublishSingle(new(destination, [], Candidate())).Success, "relative-local-admitted"); NoStages(dir);
        });

        RunDiscriminatingContractCases(test);
        if (OperatingSystem.IsWindows()) RunWindowsPaths(test);
    }

    private static void RunDiscriminatingContractCases(Action<string, Action<string>> test)
    {
        foreach (var stage in new[] { "syntax", "profile", "data", "binding", "packet", "render", "publication" })
            foreach (var state in new[] { "missing", "passed", "failed", "not_run", "wrong-type" })
                test("correction-result-stage-" + stage + "-" + state, dir =>
                {
                    bool required = stage is not ("render" or "publication");
                    bool valid = state == "passed" || state == "missing" && !required;
                    var request = Request(dir) with { CreateResult = outputs =>
                    {
                        var result = JsonNode.Parse(Result("request-1", "synthetic-build", outputs))!;
                        var levels = result["validationLevels"]!.AsObject();
                        if (state == "missing") levels.Remove(stage);
                        else if (state == "wrong-type") levels[stage] = false;
                        else levels[stage] = state;
                        return Encoding.UTF8.GetBytes(result.ToJsonString());
                    } };
                    var outcome = new SafePublisher(Limits).PublishPackage(request);
                    Check(outcome.Success == valid, "single-stage-condition");
                    if (valid) VerifyPackage(request.Destination, request.RequestId, request.ToolBuild, outcome.Outputs);
                    else Check(outcome.ErrorCode == "E_RESULT_VALIDATION" && !Directory.Exists(request.Destination), "stage-specific-refusal");
                    NoStages(dir);
                });

        foreach (var level in new[] { "root", "nested" })
            test("correction-decoded-duplicate-" + level, dir =>
            {
                var request = Request(dir) with { CreateResult = outputs =>
                {
                    var text = Encoding.UTF8.GetString(Result("request-1", "synthetic-build", outputs));
                    text = level == "root" ? text.Replace("\"status\":\"ok\"", "\"status\":\"ok\",\"st\\u0061tus\":\"ok\"", StringComparison.Ordinal) :
                        text.Replace("\"build\":\"synthetic-build\"", "\"build\":\"synthetic-build\",\"bui\\u006cd\":\"synthetic-build\"", StringComparison.Ordinal);
                    return Encoding.UTF8.GetBytes(text);
                } };
                Check(new SafePublisher(Limits).PublishPackage(request).ErrorCode == "E_JSON_DUPLICATE", "decoded-property-duplicate-refused"); NoStages(dir);
            });

        foreach (int count in new[] { 1000, 1001 })
            test("correction-warning-count-" + count, dir =>
            {
                var request = Request(dir) with { CreateResult = outputs =>
                {
                    var result = JsonNode.Parse(Result("request-1", "synthetic-build", outputs))!;
                    for (int i = 0; i < count; i++) result["warnings"]!.AsArray().Add(new JsonObject
                    { ["code"] = "W_SYNTHETIC", ["stage"] = "data", ["message"] = "Synthetic warning.", ["severity"] = "warning" });
                    return Encoding.UTF8.GetBytes(result.ToJsonString());
                } };
                var limits = Limits with { MaxManifestBytes = 256 * 1024, MaxTotalBytes = 1024 * 1024 };
                var outcome = new SafePublisher(limits).PublishPackage(request);
                Check(outcome.Success == (count == 1000), "warning-count-bound");
                if (count == 1001) Check(outcome.ErrorCode == "E_RESULT_CONTRACT", "warning-count-specific-guard");
                NoStages(dir);
            });

        foreach (var (name, number, valid) in new[] { ("int64-max", "9223372036854775807", true), ("int64-overflow", "9223372036854775808", false) })
            test("correction-stat-" + name, dir =>
            {
                var request = Request(dir) with { CreateResult = outputs => Encoding.UTF8.GetBytes(
                    Encoding.UTF8.GetString(Result("request-1", "synthetic-build", outputs)).Replace("\"stats\":{}", "\"stats\":{\"count\":" + number + "}", StringComparison.Ordinal)) };
                var outcome = new SafePublisher(Limits).PublishPackage(request);
                Check(outcome.Success == valid, "stat-int64-bound");
                if (!valid) Check(outcome.ErrorCode == "E_RESULT_CONTRACT", "stat-specific-guard"); NoStages(dir);
            });

        test("correction-safe-assertion-checkpoint", _ =>
        {
            var error = new PublicationAssertionException("stable-checkpoint-1");
            Check(error.Checkpoint == "stable-checkpoint-1", "static-checkpoint-preserved");
            var refused = new PublicationAssertionException("PRIVATE path/value sentinel");
            Check(refused.Checkpoint == "invalid-checkpoint", "unsafe-checkpoint-not-exposed");
        });
    }

    private static void RunWindowsPaths(Action<string, Action<string>> test)
    {
        var paths = new[]
        {
            ("unc", @"\\unused.invalid\share\a.pdf"), ("unc-forward", "//unused.invalid/share/a.pdf"),
            ("unc-mixed", @"\/unused.invalid\share/a.pdf"), ("device", @"\\?\C:\a.pdf"),
            ("device-forward", "//?/C:/a.pdf"), ("device-dot", @"\\.\C:\a.pdf"),
            ("extended-unc", @"\\?\UNC\unused.invalid\share\a.pdf"),
            ("drive-relative", "C:a.pdf"), ("drive-only", "C:"),
            ("root-without-drive", @"\a.pdf"), ("root-without-drive-forward", "/a.pdf")
        };
        foreach (var (name, badPath) in paths)
            foreach (var position in new[] { "destination", "report", "input-first", "input-last", "package-destination", "package-input" })
                test("correction-windows-path-" + name + "-" + position, dir =>
                {
                    int pathAccesses = 0, stages = 0;
                    var publisher = new SafePublisher(Limits, _ => stages++, beforePathAccess: () => pathAccesses++);
                    var local = Path.Combine(dir, "local.pdf");
                    PublicationOutcome? published = null; PublicationPreflightOutcome? preflight = null;
                    if (position.StartsWith("package", StringComparison.Ordinal))
                    {
                        var request = Request(dir) with { Destination = position == "package-destination" ? badPath : Path.Combine(dir, "published"),
                            InputPaths = position == "package-input" ? [local, badPath] : [] };
                        published = publisher.PublishPackage(request);
                    }
                    else
                    {
                        string[] inputs = position == "input-first" ? [badPath, local] : position == "input-last" ? [local, badPath] : [];
                        preflight = publisher.PreflightSingle(position == "destination" ? badPath : Path.Combine(dir, "out.pdf"), inputs,
                            position == "report" ? badPath : null);
                    }
                    Check((published?.ErrorCode ?? preflight?.ErrorCode) == "E_PATH_UNSUPPORTED", "unsupported-canonical-root-refusal");
                    Check(pathAccesses == 0 && stages == 0 && !Directory.EnumerateFileSystemEntries(dir).Any(), "unsupported-request-zero-access-and-writes");
                });
        test("correction-windows-forward-local-control", dir =>
        {
            var destination = Path.Combine(dir, "local space 文.pdf").Replace('\\', '/');
            Check(new SafePublisher(Limits).PublishSingle(new(destination, [], Candidate())).Success, "windows-forward-local-admitted"); NoStages(dir);
        });
    }

    private static CandidateArtifact Candidate(string id = "artifact", string path = "a.pdf") =>
        new(id, path, Payload, stream => ReadAll(stream).SequenceEqual(Payload));
    private static PackageRequest Request(string dir, string build = "synthetic-build", CandidateArtifact[]? artifacts = null) =>
        new(Path.Combine(dir, "published"), "request-1", build, [], artifacts ?? [Candidate()], outputs => Result("request-1", build, outputs));
    private static byte[] ReadAll(Stream stream) { using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray(); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static ArtifactDescriptor Describe(string id, string path, byte[] bytes) => new(id, path, bytes.LongLength, Hash(bytes));
    private static void Check(bool condition, string checkpoint) { if (!condition) throw new PublicationAssertionException(checkpoint); }
    private static void NoStages(string dir) => Check(!Directory.EnumerateDirectories(dir, ".pdfform-stage-*").Any(), "no-owned-stage-residue");
    private static byte[] Result(string request, string build, IReadOnlyList<ArtifactDescriptor> artifacts) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schema = "pdfform.result/2", requestId = request, operation = "run", status = "ok", exitCode = 0,
        artifactState = "published", validationLevels = new { syntax = "passed", profile = "passed", data = "passed", binding = "passed", packet = "passed" },
        tool = new { name = "pdfform", version = "synthetic", build, engine = "synthetic", runtime = ".NET", sourceReference = "synthetic" },
        profiles = new[] { new { id = "synthetic", revision = "2026", profileVersion = 1, sha256 = new string('a', 64) } },
        templates = new[] { new { id = "synthetic", sha256 = new string('b', 64) } },
        outputs = artifacts, errors = Array.Empty<object>(), warnings = Array.Empty<object>(), stats = new { }, filingReadiness = "not_assessed"
    }, Camel);
    private static byte[] Completion(string request, string build, byte[] result, IReadOnlyList<ArtifactDescriptor> artifacts) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schema = "pdfform.completion/1", requestId = request, toolBuild = build,
        result = new { path = "result.json", bytes = result.LongLength, sha256 = Hash(result) }, artifacts
    }, PrettyCamel);
    private static void WriteGolden(string directory, string request, string build)
    {
        Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "a.pdf"), Payload);
        var descriptors = new[] { Describe("artifact", "a.pdf", Payload) };
        var result = Result(request, build, descriptors); File.WriteAllBytes(Path.Combine(directory, "result.json"), result);
        File.WriteAllBytes(Path.Combine(directory, "completion.json"), Completion(request, build, result, descriptors));
    }
    private static void VerifyPackage(string directory, string request, string build, IReadOnlyList<ArtifactDescriptor> expected)
    {
        var resultBytes = File.ReadAllBytes(Path.Combine(directory, "result.json"));
        using var result = JsonDocument.Parse(resultBytes);
        using var completion = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "completion.json")));
        var r = result.RootElement; var c = completion.RootElement;
        Check(r.GetProperty("schema").GetString() == "pdfform.result/2" && c.GetProperty("schema").GetString() == "pdfform.completion/1", "oracle-schema");
        Check(r.GetProperty("requestId").GetString() == request && c.GetProperty("requestId").GetString() == request &&
            r.GetProperty("tool").GetProperty("build").GetString() == build && c.GetProperty("toolBuild").GetString() == build, "oracle-job-identity");
        Check(r.GetProperty("status").GetString() == "ok" && r.GetProperty("operation").GetString() == "run" &&
            r.GetProperty("exitCode").GetInt32() == 0 && r.GetProperty("artifactState").GetString() == "published", "oracle-result-success");
        foreach (var stage in new[] { "syntax", "profile", "data", "binding", "packet" })
            Check(r.GetProperty("validationLevels").GetProperty(stage).GetString() == "passed", "oracle-required-stage");
        var rd = c.GetProperty("result");
        Check(rd.GetProperty("path").GetString() == "result.json" && rd.GetProperty("bytes").GetInt64() == resultBytes.LongLength &&
            rd.GetProperty("sha256").GetString() == Hash(resultBytes), "oracle-result-byte-binding");
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray();
        Check(files.SequenceEqual(expected.Select(a => a.Path).Append("result.json").Append("completion.json").Order(StringComparer.Ordinal)), "oracle-exact-files");
        foreach (var record in new[] { r.GetProperty("outputs"), c.GetProperty("artifacts") })
        {
            Check(record.GetArrayLength() == expected.Count, "oracle-artifact-count");
            foreach (var descriptor in expected)
            {
                var entry = record.EnumerateArray().Single(item => item.GetProperty("id").GetString() == descriptor.Id);
                var bytes = File.ReadAllBytes(Path.Combine(directory, descriptor.Path));
                Check(bytes.LongLength == descriptor.Bytes && Hash(bytes) == descriptor.Sha256, "oracle-original-returned-descriptor");
                Check(entry.GetProperty("path").GetString() == descriptor.Path && entry.GetProperty("bytes").GetInt64() == bytes.LongLength &&
                    entry.GetProperty("sha256").GetString() == Hash(bytes), "oracle-manifest-artifact-binding");
            }
        }
    }
}

internal sealed class PublicationAssertionException : Exception
{
    internal string Checkpoint { get; }
    internal PublicationAssertionException(string checkpoint)
    {
        Checkpoint = checkpoint.Length is > 0 and <= 96 && checkpoint.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
            ? checkpoint : "invalid-checkpoint";
    }
}
