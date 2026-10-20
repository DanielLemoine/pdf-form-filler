using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace PdfForm.Publication;

// Live producer authority only. Recovery has no such external byte anchors and does not imply authenticity.
internal sealed record ProducerAnchors(IReadOnlyList<ArtifactDescriptor> Artifacts,
    ArtifactDescriptor Result, ArtifactDescriptor Completion);

public static class CompletionProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static byte[] Create(string requestId, string build, ArtifactDescriptor result, IReadOnlyList<ArtifactDescriptor> artifacts) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "pdfform.completion/1", requestId, toolBuild = build,
            result = new { result.Path, result.Bytes, result.Sha256 }, artifacts
        }, JsonOptions);

    /// <summary>Reconciles the exact caller-requested directory and expected job output set. Never scans for a matching request.</summary>
    public static ReconciliationOutcome Reconcile(string directory, string requestId, string toolBuild,
        IReadOnlyList<ExpectedArtifact> expected, PublicationLimits? limits = null)
        => ReconcileInternal(directory, requestId, toolBuild, expected, limits ?? new(), allowStaging: false);

    internal static ReconciliationOutcome ReconcileStaged(OwnedStage stage, string requestId, string toolBuild,
        IReadOnlyList<ExpectedArtifact> expected, PublicationLimits limits, ProducerAnchors anchors)
        => ReconcileInternal(stage.Root, requestId, toolBuild, expected, limits, allowStaging: true, anchors);

    private static ReconciliationOutcome ReconcileInternal(string directory, string requestId, string toolBuild,
        IReadOnlyList<ExpectedArtifact> expected, PublicationLimits limits, bool allowStaging, ProducerAnchors? anchors = null)
    {
        try
        {
            limits.Validate();
            ValidateExpected(expected, limits);
            if (!PublicationPaths.IsOpaqueId(requestId) || !PublicationPaths.IsBuildId(toolBuild)) throw new PublicationException("E_COMPLETION_ID", 2);
            directory = PublicationPaths.Normalize(directory);
            NativeFileSystem.RequirePublicationTarget(directory);
            if (!allowStaging && PublicationPaths.IsStagingPath(directory))
                throw new PublicationException("E_UNCOMMITTED_STAGE");
            _ = new PathGuard([], [Path.Combine(directory, "completion.json")]);
            var completionBytes = ReadBounded(Path.Combine(directory, "completion.json"), limits.MaxManifestBytes);
            if (anchors is not null && BytesDescriptor("completion", "completion.json", completionBytes) != anchors.Completion)
                throw new PublicationException("E_ARTIFACT_INTEGRITY");
            using var completion = Parse(completionBytes);
            var c = completion.RootElement;
            Exact(c, "schema", "requestId", "toolBuild", "result", "artifacts");
            if (Text(c, "schema") != "pdfform.completion/1" || Text(c, "requestId") != requestId || Text(c, "toolBuild") != toolBuild)
                throw new PublicationException("E_COMPLETION_ID");
            var result = Descriptor(c.GetProperty("result"), false);
            if (result.Path != "result.json") throw new PublicationException("E_COMPLETION_SET");
            var artifacts = ReadDescriptors(c.GetProperty("artifacts"));
            MatchExpected(artifacts, expected);
            if (anchors is not null && (result != anchors.Result || artifacts.Count != anchors.Artifacts.Count ||
                artifacts.Any(artifact => !anchors.Artifacts.Contains(artifact))))
                throw new PublicationException("E_ARTIFACT_INTEGRITY");
            if (artifacts.Sum(a => a.Bytes) + result.Bytes + completionBytes.Length > limits.MaxTotalBytes || artifacts.Any(a => a.Bytes > limits.MaxArtifactBytes))
                throw new PublicationException("E_OUTPUT_LIMIT");
            var files = EnumerateSafeFiles(directory, expected.Count + 2);
            var names = artifacts.Select(a => a.Path).Append("result.json").Append("completion.json").ToHashSet(StringComparer.Ordinal);
            if (!files.SetEquals(names)) throw new PublicationException("E_COMPLETION_SET");
            var resultBytes = ReadBounded(Path.Combine(directory, result.Path), limits.MaxManifestBytes);
            if (BytesDescriptor("result", result.Path, resultBytes) != result) throw new PublicationException("E_ARTIFACT_INTEGRITY");
            ValidateResult(resultBytes, requestId, toolBuild, artifacts);
            foreach (var artifact in artifacts) OwnedStage.CheckHash(Path.Combine(directory, artifact.Path), artifact);
            return new(true, null, artifacts.AsReadOnly());
        }
        catch (PublicationException e) { return new(false, e.Code, []); }
        catch { return new(false, "E_COMPLETION_INVALID", []); }
    }

    internal static void ValidateExpected(IReadOnlyList<ExpectedArtifact> expected, PublicationLimits limits)
    {
        if (expected.Count < 1 || expected.Count > limits.MaxArtifacts) throw new PublicationException("E_OUTPUT_SET", 2);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "result.json", "completion.json" };
        foreach (var item in expected)
            if (!PublicationPaths.IsOpaqueId(item.Id) || !PublicationPaths.IsSafeRelativeOutput(item.Path) ||
                !ids.Add(item.Id) || !paths.Add(item.Path)) throw new PublicationException("E_OUTPUT_SET", 2);
        foreach (var path in paths)
            if (paths.Any(other => other.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
                throw new PublicationException("E_OUTPUT_SET", 2);
        var directorySpellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in expected.SelectMany(item => Ancestors(item.Path)))
        {
            if (directorySpellings.TryGetValue(prefix, out var spelling) && spelling != prefix)
                throw new PublicationException("E_OUTPUT_SET", 2);
            directorySpellings[prefix] = prefix;
        }
    }

    internal static void ValidateResult(byte[] bytes, string requestId, string build, IReadOnlyList<ArtifactDescriptor> artifacts)
    {
        using var json = Parse(bytes);
        var r = json.RootElement;
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("schema", out var configurationSchema) &&
            configurationSchema.ValueKind == JsonValueKind.String && configurationSchema.GetString() == "pdfform.configuration-result/1")
        {
            ConfigurationResultValidation.ValidatePublished(r, artifacts);
            return;
        }
        Exact(r, "schema", "requestId", "operation", "status", "exitCode", "artifactState", "validationLevels", "tool",
            "profiles", "templates", "outputs", "errors", "warnings", "stats", "filingReadiness");
        if (Text(r, "schema") != "pdfform.result/2" || Text(r, "requestId") != requestId || Text(r, "operation") != "run" ||
            Text(r, "status") != "ok" || r.GetProperty("exitCode").GetInt32() != 0 ||
            Text(r, "artifactState") != "published" || Text(r, "filingReadiness") != "not_assessed")
            throw new PublicationException("E_RESULT_CONTRACT");
        var tool = r.GetProperty("tool");
        Exact(tool, "name", "version", "build", "engine", "runtime", "sourceReference");
        foreach (var property in tool.EnumerateObject())
            if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString())) throw new PublicationException("E_RESULT_CONTRACT");
        if (Text(tool, "build") != build || !PublicationPaths.IsBuildId(build)) throw new PublicationException("E_RESULT_CONTRACT");
        var stages = r.GetProperty("validationLevels");
        string[] requiredStages = ["syntax", "profile", "data", "binding", "packet"];
        string[] allowedStages = [.. requiredStages, "render", "publication"];
        if (stages.ValueKind != JsonValueKind.Object || requiredStages.Any(name => !stages.TryGetProperty(name, out _)) ||
            stages.EnumerateObject().Any(p => !allowedStages.Contains(p.Name, StringComparer.Ordinal) || p.Value.ValueKind != JsonValueKind.String || p.Value.GetString() != "passed"))
            throw new PublicationException("E_RESULT_VALIDATION");
        var stats = r.GetProperty("stats");
        if (stats.ValueKind != JsonValueKind.Object || stats.EnumerateObject().Any(p =>
            string.IsNullOrWhiteSpace(p.Name) || p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt64(out var n) || n < 0))
            throw new PublicationException("E_RESULT_CONTRACT");
        foreach (var name in new[] { "profiles", "templates", "errors", "warnings" })
            if (r.GetProperty(name).ValueKind != JsonValueKind.Array) throw new PublicationException("E_RESULT_CONTRACT");
        if (r.GetProperty("errors").GetArrayLength() != 0 || r.GetProperty("profiles").GetArrayLength() < 1 ||
            r.GetProperty("templates").GetArrayLength() < 1 || r.GetProperty("warnings").GetArrayLength() > 1000)
            throw new PublicationException("E_RESULT_CONTRACT");
        foreach (var warning in r.GetProperty("warnings").EnumerateArray()) ValidateWarning(warning);
        foreach (var profile in r.GetProperty("profiles").EnumerateArray())
        {
            Exact(profile, "id", "revision", "profileVersion", "sha256");
            if (!SafeName(Text(profile, "id")) || !SafeName(Text(profile, "revision")) ||
                profile.GetProperty("profileVersion").GetInt32() < 1 || !IsHash(Text(profile, "sha256"))) throw new PublicationException("E_RESULT_CONTRACT");
        }
        foreach (var template in r.GetProperty("templates").EnumerateArray())
        {
            Exact(template, "id", "sha256");
            if (!SafeName(Text(template, "id")) || !IsHash(Text(template, "sha256"))) throw new PublicationException("E_RESULT_CONTRACT");
        }
        var outputs = ReadDescriptors(r.GetProperty("outputs"));
        MatchExpected(outputs, artifacts.Select(a => new ExpectedArtifact(a.Id, a.Path)).ToArray());
        if (outputs.Any(o => !artifacts.Contains(o))) throw new PublicationException("E_RESULT_OUTPUTS");
    }
    private static void ValidateWarning(JsonElement warning)
    {
        string[] allowed = ["code", "stage", "message", "key", "field", "ruleId", "severity"];
        if (warning.ValueKind != JsonValueKind.Object || warning.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new PublicationException("E_RESULT_CONTRACT");
        foreach (var name in new[] { "code", "stage", "message" })
            if (!warning.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new PublicationException("E_RESULT_CONTRACT");
        foreach (var name in new[] { "key", "field", "ruleId" })
            if (warning.TryGetProperty(name, out var value) && (value.ValueKind != JsonValueKind.String || name != "field" && !SafeName(value.GetString()!)))
                throw new PublicationException("E_RESULT_CONTRACT");
        if (!SafeName(Text(warning, "code")) || !new[] { "syntax", "profile", "data", "binding", "packet", "render", "publication", "internal" }.Contains(Text(warning, "stage"), StringComparer.Ordinal) ||
            !warning.TryGetProperty("severity", out var severity) || severity.ValueKind != JsonValueKind.String || severity.GetString() != "warning")
            throw new PublicationException("E_RESULT_CONTRACT");
    }

    internal static ArtifactDescriptor BytesDescriptor(string id, string path, byte[] bytes) =>
        new(id, path, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    private static List<ArtifactDescriptor> ReadDescriptors(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw new PublicationException("E_COMPLETION_SET");
        return value.EnumerateArray().Select(e => Descriptor(e, true)).ToList();
    }
    private static ArtifactDescriptor Descriptor(JsonElement value, bool artifact)
    {
        if (artifact) Exact(value, "id", "path", "bytes", "sha256"); else Exact(value, "path", "bytes", "sha256");
        var id = artifact ? Text(value, "id") : "result";
        var path = Text(value, "path"); var hash = Text(value, "sha256");
        var bytes = value.GetProperty("bytes").GetInt64();
        if (!PublicationPaths.IsOpaqueId(id) || !PublicationPaths.IsSafeRelativeOutput(path) || bytes < 0 || !IsHash(hash))
            throw new PublicationException("E_COMPLETION_INVALID");
        return new(id, path, bytes, hash);
    }
    private static bool SafeName(string value) => value.Length is >= 1 and <= 128 && char.IsAsciiLetterOrDigit(value[0]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    private static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new PublicationException("E_RESULT_CONTRACT");
    private static void MatchExpected(IReadOnlyList<ArtifactDescriptor> actual, IReadOnlyList<ExpectedArtifact> expected)
    {
        if (actual.Count != expected.Count || actual.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != actual.Count ||
            actual.Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != actual.Count ||
            actual.Any(a => !expected.Contains(new(a.Id, a.Path)))) throw new PublicationException("E_COMPLETION_SET");
    }
    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length ||
            value.EnumerateObject().Any(p => !names.Contains(p.Name, StringComparer.Ordinal))) throw new PublicationException("E_RESULT_CONTRACT");
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        // JSON output is strict UTF-8 without BOM; duplicates are rejected at every depth.
        try { _ = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new PublicationException("E_JSON_UTF8"); }
        ValidateEscapedSurrogates(bytes);
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        try { RejectDuplicates(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private static void ValidateEscapedSurrogates(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 32 });
        while (reader.Read())
        {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName)) continue;
            var raw = reader.ValueSpan;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != (byte)'\\') continue;
                if (raw[++i] != (byte)'u') continue;
                int value = Hex4(raw.Slice(i + 1, 4)); i += 4;
                if (value is >= 0xd800 and <= 0xdbff)
                {
                    if (i + 6 >= raw.Length || raw[i + 1] != (byte)'\\' || raw[i + 2] != (byte)'u' ||
                        Hex4(raw.Slice(i + 3, 4)) is not (>= 0xdc00 and <= 0xdfff))
                        throw new PublicationException("E_JSON_UNICODE");
                    i += 6;
                }
                else if (value is >= 0xdc00 and <= 0xdfff) throw new PublicationException("E_JSON_UNICODE");
            }
        }
    }
    private static int Hex4(ReadOnlySpan<byte> bytes)
    {
        int result = 0;
        foreach (byte b in bytes) result = (result << 4) | (b <= '9' ? b - '0' : (b | 32) - 'a' + 10);
        return result;
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new PublicationException("E_JSON_DUPLICATE");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
        else if (element.ValueKind == JsonValueKind.String) _ = element.GetString();
    }
    private static byte[] ReadBounded(string path, int cap)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > cap) throw new PublicationException("E_MANIFEST_LIMIT");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    private static HashSet<string> EnumerateSafeFiles(string root, int maxFiles)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var directories = new Queue<(string Path, int Depth)>(); directories.Enqueue((root, 0));
        int directoryCount = 0;
        while (directories.TryDequeue(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                var identity = NativeFileSystem.Identity(path);
                if (identity.Directory)
                {
                    if (++directoryCount > maxFiles * 8 || directory.Depth >= 8) throw new PublicationException("E_COMPLETION_SET");
                    directories.Enqueue((path, directory.Depth + 1));
                }
                else
                {
                    if (identity.Links != 1 || !files.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')) || files.Count > maxFiles)
                        throw new PublicationException("E_COMPLETION_SET");
                }
            }
        }
        // No extra empty directories: the package's exact tree must follow its expected file set.
        if (directoryCount != files.SelectMany(p => Ancestors(p)).Distinct(StringComparer.Ordinal).Count()) throw new PublicationException("E_COMPLETION_SET");
        return files;
    }
    private static IEnumerable<string> Ancestors(string path)
    {
        while (path.Contains('/')) { path = path[..path.LastIndexOf('/')]; yield return path; }
    }
}
