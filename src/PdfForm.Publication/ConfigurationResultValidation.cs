using System.Text.Json;

namespace PdfForm.Publication;

/// <summary>Wire validation for configuration receipts only; no run-result meanings are changed.</summary>
internal static class ConfigurationResultValidation
{
    private static readonly string[] Required = ["schema", "operation", "status", "exitCode", "artifactState",
        "validationLevels", "template", "profile", "outputs", "errors", "warnings", "filingReadiness"];
    private static readonly string[] Stages = ["syntax", "profile", "binding", "bindingStructure", "data", "packet", "render"];
    private static readonly string[] FindingStages = ["syntax", "profile", "data", "binding", "packet", "render", "publication", "internal"];
    private const long MaxTemplateIdentityBytes = 50L * 1024 * 1024;
    private const long MaxJsonArtifactBytes = 10L * 1024 * 1024;

    internal static void ValidatePublished(JsonElement root, IReadOnlyList<ArtifactDescriptor> artifacts)
    {
        // This branch validates completion-eligible success only. Identities can be absent in
        // pre-observation terminal failures, never in a published configuration package.
        ExactOptional(root, Required, []);
        if (Text(root, "schema") != "pdfform.configuration-result/1" ||
            Text(root, "status") != "ok" || !Integer(root.GetProperty("exitCode"), out long exit) || exit != 0 ||
            Text(root, "artifactState") != "published" || Text(root, "filingReadiness") != "not_assessed")
            Fail();
        string operation = Text(root, "operation");
        if (operation is not ("configure" or "save-config")) Fail();
        var expected = ConfigurationPublisher.ExpectedArtifacts(operation);
        if (artifacts.Count != expected.Count || artifacts.Any(a => a.Bytes <= 0 ||
                a.Bytes > (a.Id == "template" ? MaxTemplateIdentityBytes : MaxJsonArtifactBytes) ||
                !expected.Contains(new ExpectedArtifact(a.Id, a.Path))) ||
            artifacts.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != expected.Count ||
            artifacts.Select(a => a.Path).Distinct(StringComparer.Ordinal).Count() != expected.Count)
            Fail();

        var levels = root.GetProperty("validationLevels");
        ExactOptional(levels, Stages, []);
        foreach (var stage in Stages)
        {
            string state = Text(levels, stage);
            if (stage is "data" or "packet" or "render") { if (state != "not_run") Fail(); }
            else if ((stage is "syntax" or "profile") || operation == "save-config") { if (state != "passed") Fail(); }
            else if (state is not ("passed" or "not_run")) Fail();
        }

        var outputs = root.GetProperty("outputs");
        if (outputs.ValueKind != JsonValueKind.Array || outputs.GetArrayLength() != artifacts.Count) Fail();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var output in outputs.EnumerateArray())
        {
            ExactOptional(output, ["id", "path", "bytes", "sha256"], []);
            string id = Text(output, "id"), path = Text(output, "path"), hash = Text(output, "sha256");
            if (!Integer(output.GetProperty("bytes"), out long bytes) || bytes <= 0 || !Hash(hash) ||
                !seen.Add(id) || !artifacts.Contains(new ArtifactDescriptor(id, path, bytes, hash))) Fail();
        }
        var template = root.GetProperty("template");
        {
            ExactOptional(template, ["path", "bytes", "sha256"], []);
            bool validBytes = Integer(template.GetProperty("bytes"), out long bytes);
            if (Text(template, "path") != "template.pdf" || !Hash(Text(template, "sha256")) ||
                !validBytes || bytes <= 0 || bytes > MaxTemplateIdentityBytes) Fail();
            if (operation == "save-config")
            {
                var published = artifacts.Single(a => a.Id == "template");
                if (published.Path != Text(template, "path") || published.Sha256 != Text(template, "sha256") || published.Bytes != bytes) Fail();
            }
            // Configure observes but does not copy the template. Its identity is supplied by
            // the caller's Core snapshot under the fixed logical path, never an absolute I/O path.
        }
        var profile = root.GetProperty("profile");
        {
            ExactOptional(profile, ["path", "bytes", "sha256", "id", "revision", "profileVersion"], []);
            bool validBytes = Integer(profile.GetProperty("bytes"), out long bytes);
            var published = artifacts.Single(a => a.Id == "profile");
            if (!Name(Text(profile, "id")) || !Name(Text(profile, "revision")) ||
                !Integer(profile.GetProperty("profileVersion"), out long version) || version < 1 || version > int.MaxValue ||
                Text(profile, "path") != "profile.json" || !validBytes || bytes <= 0 || bytes > MaxJsonArtifactBytes ||
                published.Path != Text(profile, "path") || published.Bytes != bytes ||
                !Hash(Text(profile, "sha256")) || published.Sha256 != Text(profile, "sha256")) Fail();
        }
        if (root.GetProperty("errors").ValueKind != JsonValueKind.Array || root.GetProperty("errors").GetArrayLength() != 0) Fail();
        var warnings = root.GetProperty("warnings");
        if (warnings.ValueKind != JsonValueKind.Array || warnings.GetArrayLength() > 1000) Fail();
        foreach (var warning in warnings.EnumerateArray()) ValidateWarning(warning);
    }

    private static void ValidateWarning(JsonElement warning)
    {
        ExactOptional(warning, ["code", "stage", "message", "severity"], ["key", "field", "ruleId"]);
        string code = Text(warning, "code"), message = Text(warning, "message");
        if (!Name(code) || !code.StartsWith("W_", StringComparison.Ordinal) ||
            !FindingStages.Contains(Text(warning, "stage"), StringComparer.Ordinal) ||
            Text(warning, "severity") != "warning" ||
            (message != code && message != "Configuration assessment warning.")) Fail();
        foreach (var name in new[] { "key", "field", "ruleId" })
            if (warning.TryGetProperty(name, out var value) &&
                (value.ValueKind != JsonValueKind.String || !FieldId(value.GetString()!))) Fail();
    }

    private static bool FieldId(string value) => value.Length == 70 && value.StartsWith("field_", StringComparison.Ordinal) && Hash(value[6..]);
    private static bool Hash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Name(string value) => value.Length is >= 1 and <= 128 && char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    private static bool Integer(JsonElement value, out long result)
    { result = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result); }
    private static string Text(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new PublicationException("E_CONFIGURATION_RESULT_CONTRACT");
        return value.GetString()!;
    }
    private static void ExactOptional(JsonElement value, string[] required, string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object || required.Any(name => !value.TryGetProperty(name, out _)) ||
            value.EnumerateObject().Any(p => !required.Contains(p.Name, StringComparer.Ordinal) && !optional.Contains(p.Name, StringComparer.Ordinal))) Fail();
    }
    private static void Fail() => throw new PublicationException("E_CONFIGURATION_RESULT_CONTRACT");
}
