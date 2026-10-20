using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfForm.Publication;

try
{
var selected = new List<HarnessCase>();
int assertion = 0;
var expected = new[] { new ExpectedArtifact("941", "forms/941.pdf"), new ExpectedArtifact("941sb", "forms/941sb.pdf"), new ExpectedArtifact("packet", "packet.pdf") };
var payload = Encoding.UTF8.GetBytes("SYNTHETIC-PAYLOAD-DO-NOT-LOG-0123456789");
var limits = new PublicationLimits(65536, 1048576, 4, 8192);

void Test(string name, Action<string> action) => selected.Add(new(name, dir =>
{
    assertion = 0;
    action(dir);
}));
void Check(bool condition) { assertion++; if (!condition) throw new PublicationAssertionException("check-" + assertion); }
ArtifactDescriptor Describe(string id, string path, byte[] bytes) =>
    new(id, path, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
CandidateArtifact Candidate(string id = "941", string path = "941.pdf", bool valid = true) => new(id, path, payload, s =>
{
    using var memory = new MemoryStream(); s.CopyTo(memory); return valid && memory.ToArray().SequenceEqual(payload);
});
byte[] Result(string request, IReadOnlyList<ArtifactDescriptor> artifacts) => JsonSerializer.SerializeToUtf8Bytes(new
{
    schema = "pdfform.result/2", requestId = request, operation = "run", status = "ok", exitCode = 0,
    artifactState = "published", validationLevels = new { syntax = "passed", profile = "passed", data = "passed", binding = "passed", packet = "passed" },
    tool = new { name = "pdfform", version = "synthetic", build = "synthetic-build", engine = "synthetic", runtime = ".NET", sourceReference = "synthetic" },
    profiles = new[] { new { id = "synthetic", revision = "2026", profileVersion = 1, sha256 = new string('a', 64) } },
    templates = new[] { new { id = "synthetic", sha256 = new string('b', 64) } },
    outputs = artifacts, errors = Array.Empty<object>(), warnings = Array.Empty<object>(), stats = new { }, filingReadiness = "not_assessed"
}, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
PackageRequest Package(string dir, string request = "request-1") => new(Path.Combine(dir, "published"), request, "synthetic-build", [],
    expected.Select(e => Candidate(e.Id, e.Path)).ToArray(), artifacts => Result(request, artifacts));
bool Reconciled(PackageRequest request) => CompletionProtocol.Reconcile(request.Destination, request.RequestId, request.ToolBuild, expected, limits).IsComplete;
void NoStages(string dir) => Check(!Directory.EnumerateDirectories(dir, ".pdfform-stage-*").Any());

Test("preflight-ready-no-writes", dir =>
{
    var input = Path.Combine(dir, "input.pdf"); File.WriteAllBytes(input, payload);
    var before = Directory.GetFileSystemEntries(dir).Order().ToArray(); int publicationPoints = 0;
    var publisher = new SafePublisher(limits, _ => publicationPoints++);
    var outcome = publisher.PreflightSingle(Path.Combine(dir, "out.pdf"), [input], Path.Combine(dir, "result.json"));
    Check(outcome.IsReady && outcome.ExitCode == 0 && outcome.ErrorCode is null && publicationPoints == 0);
    Check(before.SequenceEqual(Directory.GetFileSystemEntries(dir).Order()) && File.ReadAllBytes(input).SequenceEqual(payload));
});
Test("preflight-existing-output-preserved", dir =>
{
    var destination = Path.Combine(dir, "out.pdf"); File.WriteAllText(destination, "old");
    var outcome = new SafePublisher(limits).PreflightSingle(destination, []);
    Check(!outcome.IsReady && outcome.ExitCode == 2 && outcome.ErrorCode == "E_DESTINATION_EXISTS" && File.ReadAllText(destination) == "old"); NoStages(dir);
});
Test("preflight-existing-result-preserved", dir =>
{
    var result = Path.Combine(dir, "result.json"); File.WriteAllText(result, "old");
    var outcome = new SafePublisher(limits).PreflightSingle(Path.Combine(dir, "out.pdf"), [], result);
    Check(!outcome.IsReady && outcome.ExitCode == 2 && outcome.ErrorCode == "E_DESTINATION_EXISTS" && File.ReadAllText(result) == "old" && !File.Exists(Path.Combine(dir, "out.pdf"))); NoStages(dir);
});
Test("preflight-input-collision-value-free", dir =>
{
    var input = Path.Combine(dir, "SYNTHETIC-PRIVATE-FILENAME.pdf"); File.WriteAllBytes(input, payload);
    var outcome = new SafePublisher(limits).PreflightSingle(input, [input]);
    Check(!outcome.IsReady && outcome.ExitCode == 2 && outcome.ErrorCode == "E_PATH_COLLISION");
    Check(!JsonSerializer.Serialize(outcome).Contains("SYNTHETIC-PRIVATE") && File.ReadAllBytes(input).SequenceEqual(payload)); NoStages(dir);
});
Test("preflight-case-collision", dir =>
{
    var input = Path.Combine(dir, "input.pdf"); File.WriteAllBytes(input, payload);
    var outcome = new SafePublisher(limits).PreflightSingle(Path.Combine(dir, "INPUT.pdf"), [input]);
    Check(outcome.ErrorCode == "E_PATH_COLLISION"); NoStages(dir);
});
Test("preflight-result-collision", dir =>
{
    var destination = Path.Combine(dir, "out.pdf");
    Check(new SafePublisher(limits).PreflightSingle(destination, [], destination).ErrorCode == "E_PATH_COLLISION"); NoStages(dir);
});
Test("preflight-missing-input", dir =>
{
    var outcome = new SafePublisher(limits).PreflightSingle(Path.Combine(dir, "out.pdf"), [Path.Combine(dir, "missing.pdf")]);
    Check(!outcome.IsReady && outcome.ExitCode == 2 && outcome.ErrorCode == "E_PATH_ACCESS"); NoStages(dir);
});
Test("preflight-missing-parent", dir =>
{
    var parent = Path.Combine(dir, "missing");
    var outcome = new SafePublisher(limits).PreflightSingle(Path.Combine(parent, "out.pdf"), []);
    Check(!outcome.IsReady && outcome.ExitCode == 2 && outcome.ErrorCode == "E_PATH_ACCESS" && !Directory.Exists(parent)); NoStages(dir);
});
Test("preflight-force-existing-remains-disabled", dir =>
{
    var destination = Path.Combine(dir, "out.pdf"); File.WriteAllText(destination, "old");
    var outcome = new SafePublisher(limits).PreflightSingle(destination, [], force: true);
    Check(!outcome.IsReady && outcome.ExitCode == 2 && outcome.ErrorCode is "E_ATOMIC_REPLACE_UNSUPPORTED" or "E_ATOMIC_REPLACE_UNQUALIFIED");
    Check(File.ReadAllText(destination) == "old"); NoStages(dir);
});
Test("preflight-force-absent-is-ready", dir =>
{
    var destination = Path.Combine(dir, "out.pdf");
    Check(new SafePublisher(limits).PreflightSingle(destination, [], force: true).IsReady && !File.Exists(destination)); NoStages(dir);
});
Test("preflight-is-not-destination-lease", dir =>
{
    var destination = Path.Combine(dir, "out.pdf"); var publisher = new SafePublisher(limits);
    Check(publisher.PreflightSingle(destination, []).IsReady);
    File.WriteAllText(destination, "racer");
    var outcome = publisher.PublishSingle(new(destination, [], Candidate()));
    Check(!outcome.Success && outcome.ErrorCode == "E_DESTINATION_EXISTS" && File.ReadAllText(destination) == "racer"); NoStages(dir);
});
Test("preflight-input-count-limit", dir =>
{
    var input = Path.Combine(dir, "input.pdf"); File.WriteAllBytes(input, payload);
    var outcome = new SafePublisher(limits).PreflightSingle(Path.Combine(dir, "out.pdf"), Enumerable.Repeat(input, 65).ToArray());
    Check(!outcome.IsReady && outcome.ExitCode == 2 && outcome.ErrorCode == "E_PATH_LIMIT"); NoStages(dir);
});
Test("single-create-new", dir =>
{
    var destination = Path.Combine(dir, "out.pdf");
    var outcome = new SafePublisher(limits).PublishSingle(new(destination, [], Candidate()));
    Check(outcome.Success && outcome.ExitCode == 0 && outcome.ArtifactState == "published" && File.ReadAllBytes(destination).SequenceEqual(payload)); NoStages(dir);
});
Test("single-existing-preserved", dir =>
{
    var destination = Path.Combine(dir, "out.pdf"); File.WriteAllText(destination, "old");
    var outcome = new SafePublisher(limits).PublishSingle(new(destination, [], Candidate()));
    Check(!outcome.Success && outcome.ExitCode == 2 && outcome.ErrorCode == "E_DESTINATION_EXISTS" && File.ReadAllText(destination) == "old"); NoStages(dir);
});
Test("force-gate-preserves-prior", dir =>
{
    var destination = Path.Combine(dir, "out.pdf"); File.WriteAllText(destination, "old");
    var outcome = new SafePublisher(limits).PublishSingle(new(destination, [], Candidate(), Force: true));
    Check(!outcome.Success && outcome.ExitCode == 2 && outcome.ErrorCode is "E_ATOMIC_REPLACE_UNSUPPORTED" or "E_ATOMIC_REPLACE_UNQUALIFIED");
    Check(File.ReadAllText(destination) == "old"); NoStages(dir);
});
Test("candidate-rejection", dir =>
{
    var destination = Path.Combine(dir, "out.pdf");
    var outcome = new SafePublisher(limits).PublishSingle(new(destination, [], Candidate(valid: false)));
    Check(!outcome.Success && outcome.ErrorCode == "E_CANDIDATE_VERIFY" && !File.Exists(destination)); NoStages(dir);
});
Test("single-result-success", dir =>
{
    var outcome = new SafePublisher(limits).PublishSingle(new(Path.Combine(dir, "out.pdf"), [], Candidate(), ResultPath: Path.Combine(dir, "result.json"), ResultBytes: Encoding.UTF8.GetBytes("{}")));
    Check(outcome.Success && File.Exists(Path.Combine(dir, "result.json"))); NoStages(dir);
});
Test("single-postcommit-result-failure", dir =>
{
    var destination = Path.Combine(dir, "out.pdf");
    var publisher = new SafePublisher(limits, p => { if (p == PublicationPoint.BeforeReportCommit) throw new IOException("SYNTHETIC-PRIVATE-ERROR"); });
    var outcome = publisher.PublishSingle(new(destination, [], Candidate(), ResultPath: Path.Combine(dir, "result.json"), ResultBytes: Encoding.UTF8.GetBytes("{}")));
    Check(!outcome.Success && outcome.ExitCode == 4 && outcome.ArtifactState == "published" && outcome.ErrorCode == "E_POSTCOMMIT_REPORTING" && File.Exists(destination));
    Check(!JsonSerializer.Serialize(outcome).Contains("SYNTHETIC-PRIVATE-ERROR")); NoStages(dir);
});
Test("single-postcommit-report-racer-preserved", dir =>
{
    var report = Path.Combine(dir, "result.json");
    var publisher = new SafePublisher(limits, p => { if (p == PublicationPoint.BeforeReportCommit) File.WriteAllText(report, "racer"); });
    var outcome = publisher.PublishSingle(new(Path.Combine(dir, "out.pdf"), [], Candidate(), ResultPath: report, ResultBytes: Encoding.UTF8.GetBytes("{}")));
    Check(!outcome.Success && outcome.ArtifactState == "published" && File.ReadAllText(report) == "racer"); NoStages(dir);
});
Test("package-and-reconciliation", dir =>
{
    var request = Package(dir);
    var outcome = new SafePublisher(limits).PublishPackage(request);
    Check(outcome.Success && Reconciled(request)); NoStages(dir);
    var completion = File.ReadAllText(Path.Combine(request.Destination, "completion.json"));
    Check(!completion.Contains("SYNTHETIC-PAYLOAD") && !completion.Contains("selfHash") && !completion.Contains("profiles"));
});
Test("immutable-existing-package", dir =>
{
    var request = Package(dir); Check(new SafePublisher(limits).PublishPackage(request).Success);
    var before = File.ReadAllBytes(Path.Combine(request.Destination, "completion.json"));
    var second = new SafePublisher(limits).PublishPackage(request with { RequestId = "request-2" });
    Check(!second.Success && second.ExitCode == 2 && before.SequenceEqual(File.ReadAllBytes(Path.Combine(request.Destination, "completion.json"))) && Reconciled(request)); NoStages(dir);
});
Test("existing-empty-directory-preserved", dir =>
{
    var request = Package(dir); Directory.CreateDirectory(request.Destination);
    var outcome = new SafePublisher(limits).PublishPackage(request);
    Check(!outcome.Success && Directory.Exists(request.Destination) && !Directory.EnumerateFileSystemEntries(request.Destination).Any()); NoStages(dir);
});
Test("staging-never-consumer-success", dir =>
{
    var request = Package(dir); bool rejected = false;
    var outcome = new SafePublisher(limits, p =>
    {
        if (p != PublicationPoint.BeforeCommit) return;
        var staging = Directory.GetDirectories(dir, ".pdfform-stage-*").Single();
        rejected = CompletionProtocol.Reconcile(staging, request.RequestId, request.ToolBuild, expected, limits).ErrorCode == "E_UNCOMMITTED_STAGE";
    }).PublishPackage(request);
    Check(outcome.Success && rejected && Reconciled(request)); NoStages(dir);
});
Test("package-postcommit-recovery", dir =>
{
    var request = Package(dir);
    var outcome = new SafePublisher(limits, p => { if (p == PublicationPoint.AfterCommit) throw new IOException(); }).PublishPackage(request);
    Check(!outcome.Success && outcome.ExitCode == 4 && outcome.ArtifactState == "published" && Reconciled(request)); NoStages(dir);
});
foreach (var point in Enum.GetValues<PublicationPoint>().Where(p => p is not PublicationPoint.AfterCommit and not PublicationPoint.BeforeReportCommit))
    Test("package-failure-" + point, dir =>
    {
        var request = Package(dir);
        var outcome = new SafePublisher(limits, p => { if (p == point) throw new IOException("SYNTHETIC-PRIVATE-ERROR"); }).PublishPackage(request);
        Check(!outcome.Success && outcome.ArtifactState == "none" && !Directory.Exists(request.Destination)); NoStages(dir);
        Check(!JsonSerializer.Serialize(outcome).Contains("SYNTHETIC-PRIVATE-ERROR"));
    });
Test("second-document-failure", dir =>
{
    var request = Package(dir); int count = 0;
    var outcome = new SafePublisher(limits, p => { if (p == PublicationPoint.BeforeArtifactWrite && ++count == 2) throw new IOException(); }).PublishPackage(request);
    Check(!outcome.Success && !Directory.Exists(request.Destination)); NoStages(dir);
});
Test("concurrent-package-same-destination", dir =>
{
    using var barrier = new Barrier(2);
    var request = Package(dir);
    var tasks = Enumerable.Range(0, 2).Select(i => Task.Run(() =>
    {
        var req = request with { RequestId = "request-" + i, CreateResult = a => Result("request-" + i, a) };
        return new SafePublisher(limits, p => { if (p == PublicationPoint.BeforeCommit && !barrier.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); }).PublishPackage(req);
    })).ToArray();
    Task.WaitAll(tasks); Check(tasks.Count(t => t.Result.Success) == 1);
    var winner = Array.FindIndex(tasks, t => t.Result.Success);
    Check(CompletionProtocol.Reconcile(request.Destination, "request-" + winner, "synthetic-build", expected, limits).IsComplete); NoStages(dir);
});
Test("concurrent-single-same-destination", dir =>
{
    using var barrier = new Barrier(2); var destination = Path.Combine(dir, "out.pdf");
    var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() => new SafePublisher(limits, p =>
    { if (p == PublicationPoint.BeforeCommit && !barrier.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); }).PublishSingle(new(destination, [], Candidate())))).ToArray();
    Task.WaitAll(tasks); Check(tasks.Count(t => t.Result.Success) == 1 && File.ReadAllBytes(destination).SequenceEqual(payload)); NoStages(dir);
});
Test("independent-package-destinations", dir =>
{
    var a = Package(dir); var b = a with { Destination = Path.Combine(dir, "other"), RequestId = "request-2", CreateResult = x => Result("request-2", x) };
    var tasks = new[] { Task.Run(() => new SafePublisher(limits).PublishPackage(a)), Task.Run(() => new SafePublisher(limits).PublishPackage(b)) };
    Task.WaitAll(tasks); Check(tasks.All(t => t.Result.Success) && Reconciled(a) && Reconciled(b)); NoStages(dir);
});
Test("commit-racer-kept", dir =>
{
    var request = Package(dir);
    var outcome = new SafePublisher(limits, p => { if (p == PublicationPoint.BeforeCommit) { Directory.CreateDirectory(request.Destination); File.WriteAllText(Path.Combine(request.Destination, "other"), "keep"); } }).PublishPackage(request);
    Check(!outcome.Success && File.ReadAllText(Path.Combine(request.Destination, "other")) == "keep"); NoStages(dir);
});
Test("missing-input-is-exit-two", dir =>
{
    var outcome = new SafePublisher(limits).PublishSingle(new(Path.Combine(dir, "out.pdf"), [Path.Combine(dir, "missing.pdf")], Candidate()));
    Check(!outcome.Success && outcome.ExitCode == 2 && outcome.ErrorCode == "E_PATH_ACCESS"); NoStages(dir);
});
Test("force-absent-destination-is-create-new", dir =>
{
    var destination = Path.Combine(dir, "out.pdf");
    var outcome = new SafePublisher(limits).PublishSingle(new(destination, [], Candidate(), Force: true));
    Check(outcome.Success && File.ReadAllBytes(destination).SequenceEqual(payload)); NoStages(dir);
});
Test("same-path-input-output", dir =>
{
    var path = Path.Combine(dir, "input.pdf"); File.WriteAllText(path, "old");
    var outcome = new SafePublisher(limits).PublishSingle(new(path, [path], Candidate(), true));
    Check(outcome.ErrorCode == "E_PATH_COLLISION" && File.ReadAllText(path) == "old"); NoStages(dir);
});
Test("case-normalized-input-output", dir =>
{
    var path = Path.Combine(dir, "input.pdf"); File.WriteAllText(path, "old");
    var outcome = new SafePublisher(limits).PublishSingle(new(Path.Combine(dir, "INPUT.pdf"), [path], Candidate()));
    Check(outcome.ErrorCode == "E_PATH_COLLISION"); NoStages(dir);
});
Test("report-output-alias", dir =>
{
    var path = Path.Combine(dir, "out.pdf");
    var outcome = new SafePublisher(limits).PublishSingle(new(path, [], Candidate(), ResultPath: path, ResultBytes: Encoding.UTF8.GetBytes("{}")));
    Check(outcome.ErrorCode == "E_PATH_COLLISION"); NoStages(dir);
});
Test("input-identity-substitution", dir =>
{
    var input = Path.Combine(dir, "input.pdf"); File.WriteAllText(input, "old");
    var publisher = new SafePublisher(limits, p => { if (p == PublicationPoint.BeforeCommit) { File.Move(input, input + ".saved"); File.WriteAllText(input, "new"); } });
    var outcome = publisher.PublishSingle(new(Path.Combine(dir, "out.pdf"), [input], Candidate()));
    Check(outcome.ErrorCode == "E_PATH_CHANGED" && !File.Exists(Path.Combine(dir, "out.pdf"))); NoStages(dir);
});
Test("stage-tamper-rejected", dir =>
{
    var request = Package(dir);
    var outcome = new SafePublisher(limits, p => { if (p == PublicationPoint.BeforeCommit) File.WriteAllText(Path.Combine(Directory.GetDirectories(dir, ".pdfform-stage-*").Single(), "packet.pdf"), "tamper"); }).PublishPackage(request);
    Check(!outcome.Success && !Directory.Exists(request.Destination)); NoStages(dir);
});
Test("unknown-staging-file-preserved", dir =>
{
    var request = Package(dir); string? foreign = null;
    var outcome = new SafePublisher(limits, p =>
    {
        if (p != PublicationPoint.BeforeCommit) return;
        foreign = Path.Combine(Directory.GetDirectories(dir, ".pdfform-stage-*").Single(), "unknown.txt"); File.WriteAllText(foreign, "keep");
    }).PublishPackage(request);
    Check(!outcome.Success && outcome.CleanupIncomplete && outcome.ArtifactState == "staged" && File.ReadAllText(foreign!) == "keep" && !Directory.Exists(request.Destination));
});
Test("output-budget", dir =>
{
    var outcome = new SafePublisher(limits with { MaxArtifactBytes = 10 }).PublishPackage(Package(dir));
    Check(!outcome.Success && outcome.ErrorCode == "E_OUTPUT_LIMIT"); NoStages(dir);
});
Test("package-result-wrong-set", dir =>
{
    var request = Package(dir); request = request with { CreateResult = a => Result(request.RequestId, a.Take(1).ToArray()) };
    var outcome = new SafePublisher(limits).PublishPackage(request);
    Check(!outcome.Success && !Directory.Exists(request.Destination)); NoStages(dir);
});
foreach (var (name, invalid) in new[]
    {
        ("parent-traversal", "../escape.pdf"), ("absolute", "/absolute.pdf"), ("drive-relative", "C:drive.pdf"),
        ("backslash", "a\\b.pdf"), ("nested-traversal", "a/../escape.pdf"), ("reserved-device", "con.pdf"),
        ("trailing-space", "trailing. "), ("reserved-result", "result.json"), ("empty-segment", "forms//a.pdf")
    })
    Test("invalid-output-" + name, dir =>
    {
        var request = Package(dir) with { Artifacts = [Candidate(path: invalid)] };
        Check(!new SafePublisher(limits).PublishPackage(request).Success); NoStages(dir);
    });
foreach (var mutation in new[] { "missing", "extra", "empty-dir", "tamper", "id", "hash", "omitted", "duplicate", "unknown", "result-failure", "result-build", "result-hash", "stale-id" })
    Test("reconcile-reject-" + mutation, dir =>
    {
        var request = Package(dir); Check(new SafePublisher(limits).PublishPackage(request).Success);
        var completionPath = Path.Combine(request.Destination, "completion.json");
        var completion = JsonNode.Parse(File.ReadAllText(completionPath))!;
        switch (mutation)
        {
            case "missing": File.Delete(Path.Combine(request.Destination, "packet.pdf")); break;
            case "extra": File.WriteAllText(Path.Combine(request.Destination, "extra.pdf"), "extra"); break;
            case "empty-dir": Directory.CreateDirectory(Path.Combine(request.Destination, "extra")); break;
            case "tamper":
                var changedArtifact = payload.ToArray(); changedArtifact[0] ^= 1;
                File.WriteAllBytes(Path.Combine(request.Destination, "packet.pdf"), changedArtifact);
                Check(new FileInfo(Path.Combine(request.Destination, "packet.pdf")).Length == payload.Length);
                break;
            case "id": completion["requestId"] = "other"; break;
            case "hash":
                // Both records agree; only the unchanged artifact bytes disagree with their hash.
                completion["artifacts"]![0]!["sha256"] = new string('0', 64);
                var hashResultPath = Path.Combine(request.Destination, "result.json");
                var hashResult = JsonNode.Parse(File.ReadAllBytes(hashResultPath))!;
                hashResult["outputs"]![0]!["sha256"] = new string('0', 64);
                var hashResultBytes = Encoding.UTF8.GetBytes(hashResult.ToJsonString()); File.WriteAllBytes(hashResultPath, hashResultBytes);
                var hashDescriptor = Describe("result", "result.json", hashResultBytes);
                completion["result"]!["bytes"] = hashDescriptor.Bytes; completion["result"]!["sha256"] = hashDescriptor.Sha256;
                break;
            case "omitted": completion["artifacts"]!.AsArray().RemoveAt(1); break;
            case "duplicate": completion["artifacts"]!.AsArray().Add(completion["artifacts"]![0]!.DeepClone()); break;
            case "unknown": completion["extra"] = "bad"; break;
            case "result-failure":
            case "result-build":
            case "result-hash":
                var resultPath = Path.Combine(request.Destination, "result.json"); var result = JsonNode.Parse(File.ReadAllText(resultPath))!;
                if (mutation == "result-failure") result["status"] = "internal_error";
                else if (mutation == "result-build") result["tool"]!["build"] = "other";
                else result["tool"]!["sourceReference"] = "synthetix"; // Same length, valid metadata: only the retained digest is wrong.
                File.WriteAllText(resultPath, result.ToJsonString());
                if (mutation == "result-hash") Check(new FileInfo(resultPath).Length == completion["result"]!["bytes"]!.GetValue<long>());
                if (mutation != "result-hash")
                {
                    var descriptor = Describe("result", "result.json", File.ReadAllBytes(resultPath));
                    completion["result"]!["bytes"] = descriptor.Bytes; completion["result"]!["sha256"] = descriptor.Sha256;
                }
                break;
            case "stale-id": Check(!CompletionProtocol.Reconcile(request.Destination, "new-request", "synthetic-build", expected, limits).IsComplete); return;
        }
        File.WriteAllText(completionPath, completion.ToJsonString());
        var recovered = CompletionProtocol.Reconcile(request.Destination, request.RequestId, request.ToolBuild, expected, limits);
        Check(!recovered.IsComplete);
        if (mutation is "tamper" or "hash" or "result-hash") Check(recovered.ErrorCode == "E_ARTIFACT_INTEGRITY");
    });
Test("duplicate-output-id", dir =>
{
    var request = Package(dir) with { Artifacts = [Candidate("941", "a.pdf"), Candidate("941", "b.pdf")] };
    Check(new SafePublisher(limits).PublishPackage(request).ErrorCode == "E_OUTPUT_SET"); NoStages(dir);
});
Test("duplicate-output-path", dir =>
{
    var request = Package(dir) with { Artifacts = [Candidate("941", "a.pdf"), Candidate("941sb", "A.pdf")] };
    Check(new SafePublisher(limits).PublishPackage(request).ErrorCode == "E_OUTPUT_SET"); NoStages(dir);
});
Test("output-file-directory-collision", dir =>
{
    var request = Package(dir) with { Artifacts = [Candidate("941", "a.pdf"), Candidate("941sb", "a.pdf/b.pdf")] };
    Check(new SafePublisher(limits).PublishPackage(request).ErrorCode == "E_OUTPUT_SET"); NoStages(dir);
});
Test("result-duplicate-properties", dir =>
{
    var request = Package(dir); request = request with { CreateResult = a => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Result(request.RequestId, a)).Replace("\"status\":\"ok\"", "\"status\":\"ok\",\"status\":\"ok\"", StringComparison.Ordinal)) };
    Check(new SafePublisher(limits).PublishPackage(request).ErrorCode == "E_JSON_DUPLICATE"); NoStages(dir);
});
foreach (var defect in new[] { "binding-failed", "packet-not-run", "stage-type", "stat-negative", "stat-string", "stat-fraction", "warning-string", "warning-type", "warning-extra", "warning-no-severity", "warning-null-key", "warning-unknown-stage", "profile-id", "profile-revision", "empty-profiles", "empty-templates" })
    Test("result-contract-" + defect, dir =>
    {
        var request = Package(dir);
        request = request with { CreateResult = a =>
        {
            var value = JsonNode.Parse(Result(request.RequestId, a))!;
            switch (defect)
            {
                case "binding-failed": value["validationLevels"]!["binding"] = "failed"; break;
                case "packet-not-run": value["validationLevels"]!["packet"] = "not_run"; break;
                case "stage-type": value["validationLevels"]!["syntax"] = false; break;
                case "stat-negative": value["stats"]!["checked"] = -1; break;
                case "stat-string": value["stats"]!["checked"] = "1"; break;
                case "stat-fraction": value["stats"]!["checked"] = 1.5; break;
                case "warning-string": value["warnings"]!.AsArray().Add("bad"); break;
                case "warning-type": value["warnings"]!.AsArray().Add(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "data", ["message"] = false, ["severity"] = "warning" }); break;
                case "warning-extra": value["warnings"]!.AsArray().Add(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "data", ["message"] = "Synthetic warning.", ["severity"] = "warning", ["extra"] = true }); break;
                case "warning-no-severity": value["warnings"]!.AsArray().Add(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "data", ["message"] = "Synthetic warning." }); break;
                case "warning-null-key": value["warnings"]!.AsArray().Add(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "data", ["message"] = "Synthetic warning.", ["severity"] = "warning", ["key"] = null }); break;
                case "warning-unknown-stage": value["warnings"]!.AsArray().Add(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "unknown", ["message"] = "Synthetic warning.", ["severity"] = "warning" }); break;
                case "profile-id": value["profiles"]![0]!["id"] = "bad id"; break;
                case "profile-revision": value["profiles"]![0]!["revision"] = new string('a', 129); break;
                case "empty-profiles": value["profiles"] = new JsonArray(); break;
                case "empty-templates": value["templates"] = new JsonArray(); break;
            }
            return Encoding.UTF8.GetBytes(value.ToJsonString());
        } };
        Check(!new SafePublisher(limits).PublishPackage(request).Success && !Directory.Exists(request.Destination)); NoStages(dir);
    });
Test("valid-warning-and-optional-stages", dir =>
{
    var request = Package(dir);
    request = request with { CreateResult = a =>
    {
        var value = JsonNode.Parse(Result(request.RequestId, a))!;
        value["validationLevels"]!["render"] = "passed"; value["validationLevels"]!["publication"] = "passed";
        value["warnings"]!.AsArray().Add(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "data", ["message"] = "Synthetic warning.", ["severity"] = "warning", ["key"] = "example.key", ["field"] = "synthetic-field", ["ruleId"] = "example-rule" });
        return Encoding.UTF8.GetBytes(value.ToJsonString());
    } };
    Check(new SafePublisher(limits).PublishPackage(request).Success && Reconciled(request)); NoStages(dir);
});
foreach (var unicode in new[] { "high", "low", "invalid-utf8" })
    Test("result-unicode-" + unicode, dir =>
    {
        var request = Package(dir);
        request = request with { CreateResult = a =>
        {
            var json = Encoding.UTF8.GetString(Result(request.RequestId, a));
            if (unicode != "invalid-utf8") return Encoding.UTF8.GetBytes(json.Replace("\"sourceReference\":\"synthetic\"", "\"sourceReference\":\"" + (unicode == "high" ? "\\uD800" : "\\uDC00") + "\"", StringComparison.Ordinal));
            var bytes = Encoding.UTF8.GetBytes(json); bytes[json.IndexOf("synthetic", json.IndexOf("sourceReference", StringComparison.Ordinal), StringComparison.Ordinal)] = 0xff; return bytes;
        } };
        Check(!new SafePublisher(limits).PublishPackage(request).Success && !Directory.Exists(request.Destination)); NoStages(dir);
    });
Test("result-valid-surrogate-pair", dir =>
{
    var request = Package(dir);
    request = request with { CreateResult = a => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Result(request.RequestId, a)).Replace("\"sourceReference\":\"synthetic\"", "\"sourceReference\":\"\\uD83D\\uDE00\"", StringComparison.Ordinal)) };
    Check(new SafePublisher(limits).PublishPackage(request).Success && Reconciled(request)); NoStages(dir);
});
if (OperatingSystem.IsLinux())
{
    Test("preflight-hardlink-alias", dir =>
    {
        var input = Path.Combine(dir, "input.pdf"); var alias = Path.Combine(dir, "alias.pdf"); File.WriteAllBytes(input, payload);
        Check(NativeTests.Link(input, alias) == 0);
        Check(new SafePublisher(limits).PreflightSingle(alias, [input]).ErrorCode == "E_PATH_COLLISION"); NoStages(dir);
    });
    Test("preflight-link-parent", dir =>
    {
        var target = Path.Combine(dir, "target"); Directory.CreateDirectory(target);
        var alias = Path.Combine(dir, "alias"); Directory.CreateSymbolicLink(alias, target);
        var outcome = new SafePublisher(limits).PreflightSingle(Path.Combine(alias, "out.pdf"), []);
        Check(outcome.ErrorCode == "E_PATH_LINK_UNSUPPORTED" && !Directory.EnumerateFileSystemEntries(target).Any()); NoStages(dir);
    });
    Test("symlink-output-rejected", dir =>
    {
        var target = Path.Combine(dir, "target.pdf"); File.WriteAllText(target, "old");
        var alias = Path.Combine(dir, "alias.pdf"); File.CreateSymbolicLink(alias, target);
        var outcome = new SafePublisher(limits).PublishSingle(new(alias, [], Candidate(), true));
        Check(outcome.ErrorCode == "E_PATH_LINK_UNSUPPORTED" && File.ReadAllText(target) == "old");
    });
    Test("symlink-parent-rejected", dir =>
    {
        var target = Path.Combine(dir, "target"); Directory.CreateDirectory(target);
        var alias = Path.Combine(dir, "alias"); Directory.CreateSymbolicLink(alias, target);
        var outcome = new SafePublisher(limits).PublishSingle(new(Path.Combine(alias, "out.pdf"), [], Candidate()));
        Check(outcome.ErrorCode == "E_PATH_LINK_UNSUPPORTED" && !File.Exists(Path.Combine(target, "out.pdf")));
    });
    Test("hardlink-input-output-rejected", dir =>
    {
        var input = Path.Combine(dir, "input.pdf"); File.WriteAllText(input, "old");
        var alias = Path.Combine(dir, "alias.pdf"); Check(NativeTests.Link(input, alias) == 0);
        var outcome = new SafePublisher(limits).PublishSingle(new(alias, [input], Candidate(), true));
        Check(outcome.ErrorCode == "E_PATH_COLLISION" && File.ReadAllText(input) == "old");
    });
}
PublicationCorrectionTests.Run(Test);
PublicationHarnessTests.Register(Test);
PublicationGoldenRecoveryTests.Register(Test);
return PublicationHarness.Run(args, selected).ExitCode;
}
catch
{
    try { Console.Error.WriteLine("E_HARNESS_UNHANDLED"); Console.Error.Flush(); } catch { }
    return 4;
}

internal static class NativeTests
{
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    internal static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string oldPath, [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);
}
