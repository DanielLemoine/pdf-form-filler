using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfForm.Core;
using PdfForm.Pdf;

namespace PdfForm.Cli;

/// <summary>Owned candidate bytes for a private, explicitly reviewed configuration workflow.</summary>
public sealed class CliConfigurationDraft
{
    private readonly byte[] catalog, profile;
    private CliConfigurationDraft(byte[] catalog, byte[] profile)
    { this.catalog = catalog.ToArray(); this.profile = profile.ToArray(); }
    public byte[] CopyCatalog() => catalog.ToArray();
    public byte[] CopyProfile() => profile.ToArray();

    /// <summary>Neutral structural candidates only. Field names never become business labels,
    /// dates, money, identifiers or inferred rules. The review state is supplied from Core's contract.</summary>
    public static ParseResult<CliConfigurationDraft> Create(StructuralConfigurationDraft facts, string draftReviewState = "draft")
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (draftReviewState != "draft")
            return Fail("E_CONFIGURATION_REVIEW_STATE");
        if (facts.Compatibility.Status is not ("configuration_needed" or "supported") || facts.Compatibility.Prefilled ||
            facts.Targets.Count is <= 0 or > Limits.MaxKeysOrTargets || facts.StructureFingerprintV1 is null)
            return Fail("E_CONFIGURATION_DISCOVERY");
        try
        {
            var fields = facts.Targets.OrderBy(t => t.Target, StringComparer.Ordinal).ToArray();
            _ = CliDraftKeys.Build(fields.Select(t => t.Target));
            if (fields.Any(t => t.Key != CliDraftKeys.FromCanonicalField(t.Target) || t.Type is not ("text" or "checkbox") ||
                    t.Page is not { } page || page < 1 || page > facts.Pages || t.Rectangle.Count != 4 ||
                    t.Rectangle.Any(v => !double.IsFinite(v)) || t.Rectangle[0] >= t.Rectangle[2] || t.Rectangle[1] >= t.Rectangle[3]))
                return Fail("E_CONFIGURATION_DISCOVERY");
            if (fields.Any(t => t.Type == "checkbox" && t.OnStates.Count != 1))
                return Fail("E_CONFIGURATION_ON_STATE");
            if (fields.Any(t => t.Type == "text" && (t.Font is null || t.Font.FontSize is not (>= 1 and <= 72) ||
                    t.Font.Encoding is not ("absent" or "WinAnsiEncoding"))))
                return Fail("E_CONFIGURATION_FONT");
            var definition = new ProfileDefinition
            {
                Schema = "pdfform.profile/3", Id = "form-" + facts.TemplateSha256, Revision = "1", ProfileVersion = 1,
                Title = "Configured form", Template = new()
                {
                    Sha256Allowlist = [facts.TemplateSha256], Title = facts.TemplateTitle,
                    PageCount = facts.Pages, StructureFingerprintV1 = facts.StructureFingerprintV1!
                },
                Capabilities = fields.Select(t => t.Type + ".singleWidget").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                FilingYears = [], Options = new() { ZeroAmounts = "blank" },
                Keys = fields.Select(t => new KeyDefinition
                {
                    Key = t.Key, Kind = t.Type, Required = t.Required, Sensitive = true,
                    MaxLength = t.Type == "text" ? Math.Min(t.MaxLength is > 0 ? t.MaxLength.Value : Limits.MaxTextScalars, Limits.MaxTextScalars) : null,
                    Targets = t.Type == "text" ? JsonSerializer.SerializeToElement(new[] { t.Target }) : default,
                    Target = t.Type == "checkbox" ? t.Target : null,
                    On = t.Type == "checkbox" ? t.OnStates.Single() : null
                }).ToArray(), Rules = [], Unmapped = [],
                Source = JsonSerializer.SerializeToElement(new { }), Extensions = JsonSerializer.SerializeToElement(new { })
            };
            byte[] profileBytes = ProfileParser.SerializeProfile(definition);
            var profileCheck = ProfileParser.ParseProfile(profileBytes);
            if (!profileCheck.IsValid) return new(null, profileCheck.Findings);
            var catalogNode = new JsonObject
            {
                ["schema"] = "pdfform.catalog/1",
                ["template"] = JsonSerializer.SerializeToNode(new { path = "template.pdf", bytes = facts.TemplateBytes, sha256 = facts.TemplateSha256 }, StrictJson.Options),
                ["profile"] = ProfileIdentity(definition, profileBytes),
                ["review"] = new JsonObject { ["state"] = draftReviewState, ["unresolved"] = new JsonArray() },
                ["policy"] = new JsonObject
                {
                    ["id"] = "configured-acroform-v1", ["normalization"] = "none-v1",
                    ["textFields"] = new JsonArray(fields.Where(t => t.Type == "text").Select(TextPolicy).Cast<JsonNode?>().ToArray()),
                    ["checkboxFields"] = new JsonArray(fields.Where(t => t.Type == "checkbox").Select(CheckboxPolicy).Cast<JsonNode?>().ToArray())
                }
            };
            byte[] catalogBytes = JsonSerializer.SerializeToUtf8Bytes(catalogNode, StrictJson.Options);
            if (catalogBytes.Length > Limits.MaxJsonBytes || profileBytes.Length > Limits.MaxJsonBytes) return Fail("E_CONFIGURATION_SIZE");
            var catalogCheck = FormCatalog.ParseCatalog(catalogBytes);
            if (!catalogCheck.IsValid) return new(null, catalogCheck.Findings);
            return new(new(catalogBytes, profileBytes),
                [new("W_CONFIGURATION_DRAFT_REVIEW", "profile", "W_CONFIGURATION_DRAFT_REVIEW", Severity: "warning")]);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        { return Fail("E_CONFIGURATION_DRAFT"); }
    }

    /// <summary>Review never clears unresolved items. Exact template identity must already agree;
    /// only the edited profile's actual byte identity and explicit review state are updated.</summary>
    public static ParseResult<CliConfigurationDraft> Review(ReadOnlySpan<byte> catalogBytes,
        ReadOnlySpan<byte> profileBytes, TemplateSnapshot template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var originalCatalog = FormCatalog.ParseCatalog(catalogBytes);
        if (!originalCatalog.IsValid) return new(null, originalCatalog.Findings);
        var parsedProfile = ProfileParser.ParseProfile(profileBytes);
        if (!parsedProfile.IsValid) return new(null, parsedProfile.Findings);
        var parsed = StrictJson.Parse<JsonObject>(catalogBytes, "catalog");
        if (!parsed.IsValid) return new(null, parsed.Findings);
        try
        {
            var node = parsed.Value!;
            if (node["template"] is not JsonObject identity || identity["path"]?.GetValue<string>() != "template.pdf" ||
                identity["sha256"]?.GetValue<string>() != template.Sha256 || identity["bytes"]?.GetValue<long>() != template.ByteCount)
                return Fail("E_CONFIGURATION_TEMPLATE_MISMATCH");
            if (node["review"] is not JsonObject review || review["state"] is not JsonValue ||
                review["unresolved"] is not JsonArray unresolved) return Fail("E_CONFIGURATION_REVIEW_STATE");
            if (unresolved.Count != 0) return Fail("E_CONFIGURATION_UNRESOLVED");
            review["state"] = "reviewed";
            node["profile"] = ProfileIdentity(parsedProfile.Value!, profileBytes);
            byte[] reviewed = JsonSerializer.SerializeToUtf8Bytes(node, StrictJson.Options);
            if (reviewed.Length > Limits.MaxJsonBytes) return Fail("E_CONFIGURATION_SIZE");
            // ParseCatalog checked the original review shape/state before the explicit promotion.
            // Final binding checks edited profile identity, policy and exact target coverage.
            var bound = FormCatalog.Bind(reviewed, profileBytes, template.Sha256, template.ByteCount);
            return bound.IsValid ? new(new(reviewed, profileBytes.ToArray()), bound.Findings) : new(null, bound.Findings);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or ArgumentException)
        { return Fail("E_CONFIGURATION_DRAFT"); }
    }

    private static JsonNode ProfileIdentity(ProfileDefinition definition, ReadOnlySpan<byte> bytes) =>
        JsonSerializer.SerializeToNode(new
        {
            path = "profile.json", bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            id = definition.Id, revision = definition.Revision, profileVersion = definition.ProfileVersion
        }, StrictJson.Options)!;

    private static JsonNode TextPolicy(ConfigurationTargetFacts target)
    {
        var font = target.Font!;
        bool preserve = font.Embedded && font.ProgramUsable && font.ProgramKind == "FontFile2" &&
            font.Subtype == "TrueType" && font.Encoding == "WinAnsiEncoding" && !font.HasUnicodeMapping;
        return JsonSerializer.SerializeToNode(new
        {
            target = target.Target, page = target.Page!.Value, rectangle = target.Rectangle,
            strategy = preserve ? "preserve-embedded-simple-v1" : "bundled-liberation-serif-ascii-v1",
            layout = "single-line-v1", resourceName = font.ResourceName, baseFont = font.BaseFont,
            subtype = font.Subtype, encoding = font.Encoding, fontSize = font.FontSize!.Value,
            programSha256 = font.Embedded ? font.ProgramSha256 : null, metricsSha256 = font.MetricsSha256,
            repertoire = preserve ? "encoded-font-v1" : "ascii-plus-eacute-v1"
        }, StrictJson.Options)!;
    }

    private static JsonNode CheckboxPolicy(ConfigurationTargetFacts target)
    {
        double width = target.Rectangle[2] - target.Rectangle[0], height = target.Rectangle[3] - target.Rectangle[1];
        string mark = Math.Abs(width - 10) < .002 && Math.Abs(height - 10) < .002 ? "cross-10-v1"
            : Math.Abs(width - 12) < .002 && Math.Abs(height - 12) < .002 ? "circle-12-v1"
            : throw new ArgumentException("E_CONFIGURATION_CHECKBOX_GEOMETRY");
        return JsonSerializer.SerializeToNode(new
        {
            target = target.Target, page = target.Page!.Value, rectangle = target.Rectangle,
            onState = target.OnStates.Single(), mark
        }, StrictJson.Options)!;
    }

    private static ParseResult<CliConfigurationDraft> Fail(string code) =>
        new(null, [new(code, "profile", "Configuration candidate does not satisfy its declared structural contract.")]);
}
