using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using PdfForm.Publication;

namespace PdfForm.Publication.Tests;

/// <summary>
/// Source-only fixtures, not executed or registered by this lane. The harness owner provides
/// Core's valid configuration receipt factory and calls Register explicitly in its ordered list.
/// No edits to the existing Program.cs selection or result protocol are needed by this leaf.
/// </summary>
internal static class ConfigurationPublicationFailureFixtures
{
    private const string TemplateMarker = "SYNTHETIC-TEMPLATE-NOT-A-PDF-DO-NOT-RENDER";
    // Publication-layer fixture only, not evidence that Core assessed a real configuration.
    // Parent integration may supply Core's typed receipt serializer through the other overload.
    internal static void Register(Action<string, Action<string>> register, Action<bool> check) =>
        Register(register, check, FixtureReceipt);

    private static byte[] FixtureReceipt(string requestId, string operation, IReadOnlyList<ArtifactDescriptor> outputs)
    {
        var profile = outputs.Single(a => a.Id == "profile");
        var marker = Encoding.UTF8.GetBytes(TemplateMarker);
        var template = operation == "save-config" ? outputs.Single(a => a.Id == "template") :
            new ArtifactDescriptor("template", "template.pdf", marker.LongLength, Convert.ToHexStringLower(SHA256.HashData(marker)));
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "pdfform.configuration-result/1", operation, status = "ok", exitCode = 0,
            artifactState = "published", validationLevels = ConfigurationPublicationStages.Create("passed", "passed", "passed", "passed"),
            template = new { path = template.Path, bytes = template.Bytes, sha256 = template.Sha256 },
            profile = new { path = profile.Path, bytes = profile.Bytes, sha256 = profile.Sha256, id = "synthetic", revision = "r1", profileVersion = 3 },
            outputs, errors = Array.Empty<object>(), warnings = Array.Empty<object>(), filingReadiness = "not_assessed"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    internal static void Register(Action<string, Action<string>> register, Action<bool> check,
        Func<string, string, IReadOnlyList<ArtifactDescriptor>, byte[]> receipt)
    {
        var limits = new PublicationLimits(65536, 1048576, 4, 8192);
        byte[] catalog = Encoding.UTF8.GetBytes("SYNTHETIC-CATALOG");
        byte[] profile = Encoding.UTF8.GetBytes("SYNTHETIC-PROFILE");
        byte[] template = Encoding.UTF8.GetBytes(TemplateMarker);
        bool Same(Stream input, byte[] expected)
        { using var copy = new MemoryStream(); input.CopyTo(copy); return copy.ToArray().SequenceEqual(expected); }
        ConfigurationPackageRequest Request(string dir, string operation = "configure") => new(
            Path.Combine(dir, "configuration"), "configuration-request", "synthetic-build", operation, [],
            catalog, profile, input => Same(input, catalog), input => Same(input, profile),
            outputs => receipt("configuration-request", operation, outputs),
            operation == "save-config" ? new ReadOnlyMemory<byte>(template) : null,
            operation == "save-config" ? input => Same(input, template) : null);
        void NoStages(string dir) => check(!Directory.EnumerateDirectories(dir, ".pdfform-stage-*").Any());
        byte[] Mutate(byte[] source, Action<JsonObject> change)
        { var value = JsonNode.Parse(source)!.AsObject(); change(value); return JsonSerializer.SerializeToUtf8Bytes(value); }
        void PreparationRefusal(string dir, ConfigurationPackageRequest request, PublicationLimits selectedLimits,
            int exitCode, string errorCode)
        {
            Directory.CreateDirectory(request.Destination);
            string marker = Path.Combine(request.Destination, "prior.bin");
            byte[] prior = Encoding.UTF8.GetBytes("SYNTHETIC-EXISTING-OUTPUT");
            File.WriteAllBytes(marker, prior);
            int accesses = 0, callbacks = 0;
            var guarded = request with
            {
                VerifyCatalog = _ => { callbacks++; return false; },
                VerifyProfile = _ => { callbacks++; return false; },
                CreateResult = _ => { callbacks++; return []; }
            };
            var result = new ConfigurationPublisher(selectedLimits, _ => { }, beforePathAccess: () => accesses++).Publish(guarded);
            check(!result.Success && result.ExitCode == exitCode && result.ErrorCode == errorCode &&
                result.ArtifactState == "none" && result.Outputs.Count == 0 && !result.CleanupIncomplete &&
                ConfigurationPublisher.TerminalStatus(result) == (exitCode == 2 ? "input_error" : "internal_error"));
            check(accesses == 0 && callbacks == 0 && File.ReadAllBytes(marker).SequenceEqual(prior) &&
                Directory.GetFiles(request.Destination).SequenceEqual(new[] { marker }));
            NoStages(dir);
        }

        register("configuration-preparation-malformed-keeps-input-error", dir =>
            PreparationRefusal(dir, Request(dir) with { Operation = "synthetic-unsupported-operation" }, limits,
                2, "E_CONFIGURATION_OPERATION"));
        register("configuration-preparation-resource-refusal-keeps-input-error", dir =>
            PreparationRefusal(dir, Request(dir), new PublicationLimits(512, 1024, 4, 512),
                2, "E_OUTPUT_LIMIT"));
        register("configuration-preparation-unexpected-exception-is-internal-error", dir =>
            PreparationRefusal(dir, Request(dir) with { InputPaths = new ThrowingInputPaths() }, limits,
                4, "E_CONFIGURATION_INTERNAL"));

        foreach (var mode in new[] { "throw", "null", "oversize" })
        {
            string failureMode = mode;
            register("configuration-result-factory-" + mode + "-keeps-terminal-mapping", dir =>
            {
                string marker = Path.Combine(dir, "prior.bin");
                byte[] prior = Encoding.UTF8.GetBytes("SYNTHETIC-UNRELATED-OUTPUT"); File.WriteAllBytes(marker, prior);
                var request = Request(dir) with { CreateResult = _ => failureMode switch
                {
                    "throw" => throw new InvalidOperationException("synthetic"),
                    "null" => null!,
                    _ => new byte[checked((int)limits.MaxManifestBytes + 1)]
                } };
                var result = new ConfigurationPublisher(limits).Publish(request);
                int expectedExit = failureMode == "oversize" ? 2 : 4;
                string expectedCode = failureMode switch
                {
                    "throw" => "E_PUBLICATION", "null" => "E_CONFIGURATION_RESULT_CONTRACT", _ => "E_MANIFEST_LIMIT"
                };
                check(!result.Success && result.ExitCode == expectedExit && result.ErrorCode == expectedCode &&
                    result.ArtifactState == "none" && result.Outputs.Count == 0 && !result.CleanupIncomplete &&
                    ConfigurationPublisher.TerminalStatus(result) == (expectedExit == 2 ? "input_error" : "internal_error"));
                check(!Directory.Exists(request.Destination) && File.ReadAllBytes(marker).SequenceEqual(prior)); NoStages(dir);
            });
        }

        register("configuration-create-new-exact-two-payloads", dir =>
        {
            var request = Request(dir); var result = new ConfigurationPublisher(limits).Publish(request);
            check(result.Success && result.ArtifactState == "published" && result.Outputs.Count == 2);
            check(Directory.GetFiles(request.Destination).Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "catalog.json", "completion.json", "profile.json", "result.json" }));
            check(CompletionProtocol.Reconcile(request.Destination, request.RequestId, request.ToolBuild,
                ConfigurationPublisher.ExpectedArtifacts(request.Operation), limits).IsComplete);
        });
        register("configuration-save-config-exact-three-payloads", dir =>
        {
            var request = Request(dir, "save-config"); var result = new ConfigurationPublisher(limits).Publish(request);
            check(result.Success && result.Outputs.Count == 3 && File.ReadAllBytes(Path.Combine(request.Destination, "template.pdf")).SequenceEqual(template));
            check(Directory.GetFiles(request.Destination).Length == 5);
        });
        register("configuration-existing-directory-preserved", dir =>
        {
            var request = Request(dir); Directory.CreateDirectory(request.Destination);
            var marker = Path.Combine(request.Destination, "old.txt"); File.WriteAllText(marker, "prior");
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.ErrorCode == "E_DESTINATION_EXISTS" && result.Outputs.Count == 0 && File.ReadAllText(marker) == "prior"); NoStages(dir);
        });
        register("configuration-template-shape-before-path-access", dir =>
        {
            int accesses = 0; var publisher = new ConfigurationPublisher(limits, _ => { }, beforePathAccess: () => accesses++);
            var unexpected = Request(dir) with { TemplatePdf = new ReadOnlyMemory<byte>(template), VerifyTemplate = input => Same(input, template) };
            var missing = Request(dir, "save-config") with { TemplatePdf = null, VerifyTemplate = null };
            check(publisher.Publish(unexpected).ErrorCode == "E_CONFIGURATION_TEMPLATE_ARGUMENT");
            check(publisher.Publish(missing).ErrorCode == "E_CONFIGURATION_TEMPLATE_ARGUMENT" && accesses == 0); NoStages(dir);
        });
        register("configuration-byte-budget-before-path-access", dir =>
        {
            int accesses = 0; var small = new PublicationLimits(512, 1024, 4, 512);
            var result = new ConfigurationPublisher(small, _ => { }, beforePathAccess: () => accesses++).Publish(Request(dir));
            check(!result.Success && result.ErrorCode == "E_OUTPUT_LIMIT" && accesses == 0); NoStages(dir);
        });
        register("configuration-verifier-refusal-no-published-directory", dir =>
        {
            var request = Request(dir) with { VerifyProfile = _ => false };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.ArtifactState == "none" && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        foreach (string operationName in new[] { "configure", "save-config" })
        {
            string operation = operationName;
            foreach (string identityName in new[] { "template", "profile" })
            {
                string identity = identityName;
                string requiredPath = identity == "template" ? "template.pdf" : "profile.json";
                foreach (string pathModeName in new[] { "other", "case", "absolute" })
                {
                    string pathMode = pathModeName;
                    string invalidPath = pathMode switch
                    {
                        "other" => "other-" + requiredPath,
                        "case" => char.ToUpperInvariant(requiredPath[0]) + requiredPath[1..],
                        _ => "Q:\\SYNTHETIC-PRIVATE\\" + requiredPath
                    };
                    register("configuration-" + operation + "-reject-" + identity + "-path-" + pathMode, dir =>
                    {
                        var request = Request(dir, operation) with { CreateResult = outputs => Mutate(receipt("configuration-request", operation, outputs),
                            root => root[identity]!["path"] = invalidPath) };
                        var result = new ConfigurationPublisher(limits).Publish(request);
                        check(!result.Success && result.ExitCode == 4 && result.ErrorCode == "E_CONFIGURATION_RESULT_CONTRACT" &&
                            result.ArtifactState == "none" && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
                    });
                }
            }
            foreach (string stageName in new[] { "binding", "bindingStructure" })
            {
                string stage = stageName;
                register("configuration-" + operation + (operation == "save-config" ? "-reject" : "-accept") + "-not-run-" + stage + "-success", dir =>
                {
                    var request = Request(dir, operation) with { CreateResult = outputs => Mutate(receipt("configuration-request", operation, outputs),
                        root => root["validationLevels"]![stage] = "not_run") };
                    var result = new ConfigurationPublisher(limits).Publish(request);
                    if (operation == "save-config")
                        check(!result.Success && result.ExitCode == 4 && result.ErrorCode == "E_CONFIGURATION_RESULT_CONTRACT" &&
                            result.ArtifactState == "none" && result.Outputs.Count == 0 && !Directory.Exists(request.Destination));
                    else
                    {
                        check(result.Success && result.ExitCode == 0 && result.ArtifactState == "published" && result.Outputs.Count == 2);
                        check(CompletionProtocol.Reconcile(request.Destination, request.RequestId, request.ToolBuild,
                            ConfigurationPublisher.ExpectedArtifacts(operation), limits).IsComplete);
                    }
                    NoStages(dir);
                });
            }
            // Wire-only controls use synthetic descriptors, not multi-megabyte buffers or PDF inputs.
            IReadOnlyList<ArtifactDescriptor> WireArtifacts(long profileBytes, long templateBytes, long catalogBytes = 17)
            {
                var outputs = new List<ArtifactDescriptor>
                {
                    new("catalog", "catalog.json", catalogBytes, new string('a', 64)),
                    new("profile", "profile.json", profileBytes, new string('b', 64))
                };
                if (operation == "save-config") outputs.Add(new("template", "template.pdf", templateBytes, new string('c', 64)));
                return outputs.AsReadOnly();
            }
            byte[] WireReceipt(IReadOnlyList<ArtifactDescriptor> outputs, long templateBytes) =>
                Mutate(FixtureReceipt("configuration-request", operation, outputs), root => root["template"]!["bytes"] = templateBytes);
            foreach (string identityName in new[] { "template", "profile" })
            {
                string identity = identityName;
                register("configuration-" + operation + "-reject-" + identity + "-identity-over-cap", _ =>
                {
                    long templateBytes = 50L * 1024 * 1024 + (identity == "template" ? 1 : 0);
                    long profileBytes = 10L * 1024 * 1024 + (identity == "profile" ? 1 : 0);
                    var outputs = WireArtifacts(profileBytes, templateBytes);
                    bool rejected = false;
                    try { CompletionProtocol.ValidateResult(WireReceipt(outputs, templateBytes), "configuration-request", "synthetic-build", outputs); }
                    catch (PublicationException e) { rejected = e.Code == "E_CONFIGURATION_RESULT_CONTRACT" && e.ExitCode == 4; }
                    check(rejected);
                });
            }
            register("configuration-" + operation + "-reject-catalog-descriptor-over-cap", _ =>
            {
                var outputs = WireArtifacts(10L * 1024 * 1024, 50L * 1024 * 1024, 10L * 1024 * 1024 + 1);
                bool rejected = false;
                try { CompletionProtocol.ValidateResult(WireReceipt(outputs, 50L * 1024 * 1024), "configuration-request", "synthetic-build", outputs); }
                catch (PublicationException e) { rejected = e.Code == "E_CONFIGURATION_RESULT_CONTRACT" && e.ExitCode == 4; }
                check(rejected);
            });
            register("configuration-" + operation + "-identity-cap-boundaries-valid-wire", _ =>
            {
                var outputs = WireArtifacts(10L * 1024 * 1024, 50L * 1024 * 1024, 10L * 1024 * 1024);
                CompletionProtocol.ValidateResult(WireReceipt(outputs, 50L * 1024 * 1024), "configuration-request", "synthetic-build", outputs);
            });
        }
        register("configuration-draft-both-binding-levels-not-run", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                { root["validationLevels"]!["binding"] = "not_run"; root["validationLevels"]!["bindingStructure"] = "not_run"; }) };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(result.Success && result.ExitCode == 0 && result.ArtifactState == "published" && result.Outputs.Count == 2);
            check(CompletionProtocol.Reconcile(request.Destination, request.RequestId, request.ToolBuild,
                ConfigurationPublisher.ExpectedArtifacts("configure"), limits).IsComplete); NoStages(dir);
        });
        foreach (var stage in new[] { "data", "packet", "render" })
        {
            var changedStage = stage;
            register("configuration-reject-claimed-" + stage + "-validation", dir =>
            {
                var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root => root["validationLevels"]![changedStage] = "passed") };
                var result = new ConfigurationPublisher(limits).Publish(request);
                check(!result.Success && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
            });
        }
        register("configuration-reject-run-schema", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root => root["schema"] = "pdfform.result/2") };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        register("configuration-reject-receipt-output-substitution", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root => root["outputs"]![0]!["sha256"] = new string('0', 64)) };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        register("configuration-reject-failure-receipt-as-completion", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                { root["status"] = "internal_error"; root["exitCode"] = 4; root["artifactState"] = "published"; }) };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.ExitCode == 4 && result.ArtifactState == "none" && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        register("configuration-unknown-never-completion-eligible", dir =>
        {
            var unknown = ConfigurationPublisher.UnconfirmedOutcome();
            check(!unknown.Success && unknown.ExitCode == 4 && unknown.ArtifactState == "unknown" && unknown.Outputs.Count == 0 &&
                ConfigurationPublisher.TerminalStatus(unknown) == "internal_error");
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                { root["status"] = "internal_error"; root["exitCode"] = 4; root["artifactState"] = "unknown"; }) };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        register("configuration-result-create-failure-stays-staged", dir =>
        {
            int filesCreated = 0; bool completionReached = false; var request = Request(dir);
            var publisher = new ConfigurationPublisher(limits, point => { if (point == PublicationPoint.BeforeCompletionWrite) completionReached = true; },
                point => { if (point == StageCreationPoint.File && ++filesCreated == 3) throw new IOException("synthetic"); });
            var result = publisher.Publish(request);
            check(!result.Success && result.ExitCode == 4 && result.ArtifactState == "staged" && result.CleanupIncomplete &&
                result.Outputs.Count == 0 && !completionReached && !Directory.Exists(request.Destination));
        });
        register("configuration-reject-private-warning-message", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                root["warnings"] = new JsonArray(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "binding",
                    ["message"] = "SYNTHETIC-PRIVATE-TARGET", ["severity"] = "warning" })) };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        register("configuration-reject-extra-publication-stage", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                root["validationLevels"]!["publication"] = "passed") };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        register("configuration-safe-warning-field-id", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                root["warnings"] = new JsonArray(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "publication",
                    ["message"] = "W_SYNTHETIC", ["severity"] = "warning", ["field"] = "field_" + new string('a', 64) })) };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(result.Success && result.ArtifactState == "published" && result.Outputs.Count == 2);
        });
        foreach (string warningStageName in new[] { "bindingStructure", "synthetic-unsupported-stage" })
        {
            string warningStage = warningStageName;
            register("configuration-reject-unsupported-warning-stage-" + warningStage, dir =>
            {
                var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                    root["warnings"] = new JsonArray(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = warningStage,
                        ["message"] = "W_SYNTHETIC", ["severity"] = "warning", ["field"] = "field_" + new string('a', 64) })) };
                var result = new ConfigurationPublisher(limits).Publish(request);
                check(!result.Success && result.ExitCode == 4 && result.ErrorCode == "E_CONFIGURATION_RESULT_CONTRACT" &&
                    result.ArtifactState == "none" && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
            });
        }
        register("configuration-reject-private-warning-field", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                root["warnings"] = new JsonArray(new JsonObject { ["code"] = "W_SYNTHETIC", ["stage"] = "binding",
                    ["message"] = "W_SYNTHETIC", ["severity"] = "warning", ["field"] = "SYNTHETIC-PRIVATE-TARGET" })) };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        foreach (var identityName in new[] { "template", "profile" })
        {
            string identity = identityName;
            register("configuration-success-requires-" + identity + "-identity", dir =>
            {
                var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root => root.Remove(identity)) };
                var result = new ConfigurationPublisher(limits).Publish(request);
                check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
            });
            register("configuration-reject-reduced-" + identity + "-identity", dir =>
            {
                var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root =>
                    { var value = root[identity]!.AsObject(); value.Remove("path"); if (identity == "profile") value.Remove("bytes"); }) };
                var result = new ConfigurationPublisher(limits).Publish(request);
                check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
            });
            register("configuration-reject-null-" + identity + "-identity", dir =>
            {
                var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root => root[identity] = null) };
                var result = new ConfigurationPublisher(limits).Publish(request);
                check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
            });
        }
        foreach (var memberName in new[] { "path", "bytes", "sha256" })
        {
            string member = memberName;
            void ChangeIdentity(JsonObject value)
            {
                if (member == "path") value[member] = "other-artifact.json";
                else if (member == "bytes") value[member] = value[member]!.GetValue<long>() + 1;
                else value[member] = new string('0', 64);
            }
            register("configuration-profile-identity-" + member + "-must-match-output", dir =>
            {
                var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root => ChangeIdentity(root["profile"]!.AsObject())) };
                var result = new ConfigurationPublisher(limits).Publish(request);
                check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
            });
            register("configuration-saved-template-identity-" + member + "-must-match-output", dir =>
            {
                var request = Request(dir, "save-config") with { CreateResult = outputs => Mutate(receipt("configuration-request", "save-config", outputs), root => ChangeIdentity(root["template"]!.AsObject())) };
                var result = new ConfigurationPublisher(limits).Publish(request);
                check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
            });
        }
        register("configuration-reject-extra-identity-member", dir =>
        {
            var request = Request(dir) with { CreateResult = outputs => Mutate(receipt("configuration-request", "configure", outputs), root => root["profile"]!["extra"] = "synthetic") };
            var result = new ConfigurationPublisher(limits).Publish(request);
            check(!result.Success && result.ExitCode == 4 && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
        });
        foreach (var point in new[] { PublicationPoint.BeforeResultWrite, PublicationPoint.BeforeCompletionWrite, PublicationPoint.BeforeCommit })
        {
            var target = point;
            register("configuration-failure-" + point, dir =>
            {
                var request = Request(dir);
                var result = new ConfigurationPublisher(limits, current => { if (current == target) throw new IOException("synthetic"); }).Publish(request);
                check(!result.Success && result.ArtifactState == "none" && result.Outputs.Count == 0 && !Directory.Exists(request.Destination)); NoStages(dir);
            });
        }
        register("configuration-postcommit-failure-stays-published", dir =>
        {
            var request = Request(dir);
            var result = new ConfigurationPublisher(limits, point => { if (point == PublicationPoint.AfterCommit) throw new IOException("synthetic"); }).Publish(request);
            check(!result.Success && result.ArtifactState == "published" && result.ErrorCode == "E_POSTCOMMIT_REPORTING" && result.Outputs.Count == 2);
            check(CompletionProtocol.Reconcile(request.Destination, request.RequestId, request.ToolBuild,
                ConfigurationPublisher.ExpectedArtifacts(request.Operation), limits).IsComplete);
        });
        register("configuration-unacquired-root-preserved", dir =>
        {
            var request = Request(dir);
            var result = new ConfigurationPublisher(limits, _ => { }, point => { if (point == StageCreationPoint.RootDirectory) throw new IOException("synthetic"); }).Publish(request);
            check(!result.Success && result.ArtifactState == "staged" && result.CleanupIncomplete && result.Outputs.Count == 0 && !Directory.Exists(request.Destination));
            check(Directory.EnumerateDirectories(dir, ".pdfform-stage-*").Count() == 1);
        });
    }

    private sealed class ThrowingInputPaths : IReadOnlyList<string>
    {
        public int Count => throw new InvalidOperationException("synthetic");
        public string this[int index] => throw new InvalidOperationException("synthetic");
        public IEnumerator<string> GetEnumerator() => throw new InvalidOperationException("synthetic");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
