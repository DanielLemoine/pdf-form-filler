using System.Security.Cryptography;
using System.Text.Json;

namespace PdfForm.Core;

/// <summary>Identity and declared-policy binding only. Observed PDF safety and rendering are separate checks.</summary>
public static class FormCatalog
{
    public const string ConfiguredPolicy = "configured-acroform-v1";
    public const string Form941Policy = "builtin-irs-941-v1";
    public const string ScheduleBPolicy = "builtin-irs-941sb-embedded-v1";

    public static ParseResult<FormCatalogDefinition> ParseCatalog(ReadOnlySpan<byte> catalogJson)
    {
        var parsed = StrictJson.Parse<FormCatalogDefinition>(catalogJson, "catalog");
        if (!parsed.IsValid) return parsed;
        var catalog = parsed.Value!; var findings = new List<Finding>();
        void Error(string code, string message) => findings.Add(new(code, "binding", message));
        bool Hash(string value) => ProfileParser.Match(value, "[0-9a-f]{64}");
        bool Text(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 1024 && !value.Any(char.IsControl);
        bool Rectangle(double[] value) => value.Length == 4 && value.All(double.IsFinite) && value[0] < value[2] && value[1] < value[3];
        if (catalog.Schema != "pdfform.catalog/1") Error("E_CATALOG_SCHEMA", "Catalog schema must be pdfform.catalog/1.");
        if (catalog.Template.Path != "template.pdf" || catalog.Template.Bytes is < 1 or > Limits.MaxTemplateBytes || !Hash(catalog.Template.Sha256))
            Error("E_CATALOG_TEMPLATE", "Catalog template identity or fixed package path is invalid.");
        if (catalog.Profile.Path != "profile.json" || catalog.Profile.Bytes is < 1 or > Limits.MaxJsonBytes || !Hash(catalog.Profile.Sha256)
            || !ProfileParser.SafeName(catalog.Profile.Id) || !ProfileParser.SafeName(catalog.Profile.Revision) || catalog.Profile.ProfileVersion < 1)
            Error("E_CATALOG_PROFILE", "Catalog profile identity or fixed package path is invalid.");
        if (catalog.Review.State is not ("draft" or "reviewed") || catalog.Review.State == "reviewed" && catalog.Review.Unresolved.Length != 0)
            Error("E_CATALOG_REVIEW", "Catalog review state is inconsistent with unresolved configuration.");
        if (catalog.Review.Unresolved.Length > Limits.MaxRules || catalog.Review.Unresolved.Any(i => !ProfileParser.SafeName(i.Code)
            || !ProfileParser.SafeName(i.Capability) || i.FieldId is not null && !ProfileParser.Match(i.FieldId, "field_[0-9a-f]{64}")))
            Error("E_CATALOG_ISSUES", "Configuration issue structure exceeds its bounded contract.");
        var policy = catalog.Policy;
        bool builtin = policy.Id is Form941Policy or ScheduleBPolicy;
        if (policy.Id is not (ConfiguredPolicy or Form941Policy or ScheduleBPolicy))
            Error("E_CATALOG_POLICY_UNSUPPORTED", "The requested form policy is not implemented.");
        if (builtin ? policy.Normalization != "reviewed-irs-static-hybrid-v1" : policy.Normalization != "none-v1")
            Error("E_CATALOG_POLICY_UNSUPPORTED", "The requested normalization strategy is not implemented for this policy.");
        if ((long)policy.TextFields.Length + policy.CheckboxFields.Length > Limits.MaxKeysOrTargets
            || builtin && (policy.TextFields.Length != 0 || policy.CheckboxFields.Length != 0))
            Error("E_CATALOG_TARGETS", "Catalog target policy count is invalid for its closed strategy.");
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in policy.TextFields)
        {
            if (!Text(field.Target) || !targets.Add(field.Target) || field.Page is < 1 or > Limits.MaxTemplatePages || !Rectangle(field.Rectangle))
                Error("E_CATALOG_TARGETS", "Text target identity, uniqueness or geometry is invalid.");
            bool preserved = field.Strategy == "preserve-embedded-simple-v1";
            bool bundled = field.Strategy == "bundled-liberation-serif-ascii-v1";
            if ((!preserved && !bundled) || field.Layout is not ("single-line-v1" or "eight-date-cells-v1")
                || !double.IsFinite(field.FontSize) || field.FontSize is < 1 or > 72
                || preserved && (field.Subtype != "TrueType" || field.Encoding != "WinAnsiEncoding" || field.Repertoire != "encoded-font-v1" || field.ProgramSha256 is null)
                || bundled && (field.Subtype is not ("Type1" or "TrueType") || field.Encoding is not ("WinAnsiEncoding" or "absent") || field.Repertoire != "ascii-plus-eacute-v1"))
                Error("E_CATALOG_POLICY_UNSUPPORTED", "The requested text font or layout strategy is not implemented.");
            if (!Text(field.ResourceName) || !Text(field.BaseFont) || field.ProgramSha256 is not null && !Hash(field.ProgramSha256) || !Hash(field.MetricsSha256))
                Error("E_CATALOG_FONT", "The exact source font resource identities are invalid.");
        }
        foreach (var field in policy.CheckboxFields)
        {
            if (!Text(field.Target) || !targets.Add(field.Target) || field.Page is < 1 or > Limits.MaxTemplatePages
                || !Rectangle(field.Rectangle) || !Text(field.OnState) || field.OnState == "Off")
                Error("E_CATALOG_TARGETS", "Checkbox target identity, export state or geometry is invalid.");
            double side = field.Mark == "cross-10-v1" ? 10 : field.Mark == "circle-12-v1" ? 12 : 0;
            if (side == 0) Error("E_CATALOG_POLICY_UNSUPPORTED", "The requested checkbox mark strategy is not implemented.");
            else if (field.Rectangle.Length == 4 && (Math.Abs(field.Rectangle[2] - field.Rectangle[0] - side) >= .002
                || Math.Abs(field.Rectangle[3] - field.Rectangle[1] - side) >= .002))
                Error("E_CATALOG_MARK_GEOMETRY", "Checkbox geometry does not match its fixed mark strategy.");
        }
        return new(findings.Count == 0 ? catalog : null, findings.AsReadOnly());
    }

    public static ParseResult<FormConfigurationSnapshot> Bind(ReadOnlySpan<byte> catalogJson, ReadOnlySpan<byte> profileJson,
        string observedTemplateSha256, long observedTemplateBytes)
    {
        if (catalogJson.Length > Limits.MaxJsonBytes || profileJson.Length > Limits.MaxJsonBytes)
            return new(null, [new("E_JSON_SIZE", "syntax", "Configuration input exceeds the JSON byte limit.")]);
        // Own both buffers before any parse or digest; later consumers receive only new DTOs/read-only streams.
        byte[] catalogBytes = catalogJson.ToArray(), profileBytes = profileJson.ToArray();
        var parsed = ParseCatalog(catalogBytes);
        if (!parsed.IsValid) return new(null, parsed.Findings);
        var catalog = parsed.Value!; var findings = new List<Finding>();
        void Error(string code, string message) => findings.Add(new(code, "binding", message));
        if (catalog.Template.Sha256 != observedTemplateSha256 || catalog.Template.Bytes != observedTemplateBytes)
            Error("E_CATALOG_TEMPLATE_HASH", "Observed template bytes do not match the complete configuration binding.");
        if (catalog.Profile.Bytes != profileBytes.Length || catalog.Profile.Sha256 != Hash(profileBytes))
            Error("E_CATALOG_PROFILE_HASH", "Profile bytes do not match the complete configuration binding.");
        var profileResult = ProfileParser.ParseProfile(profileBytes);
        if (!profileResult.IsValid) return new(null, findings.Concat(profileResult.Findings).ToArray());
        var profile = profileResult.Value!;
        if (profile.Id != catalog.Profile.Id || profile.Revision != catalog.Profile.Revision || profile.ProfileVersion != catalog.Profile.ProfileVersion
            || profile.Template.Sha256Allowlist.Length != 1 || profile.Template.Sha256Allowlist[0] != catalog.Template.Sha256)
            Error("E_CATALOG_PROFILE_IDENTITY", "Profile identity or exact single-template binding disagrees with the catalog.");
        if (catalog.Review.State != "reviewed" || catalog.Review.Unresolved.Length != 0)
            Error("E_CATALOG_REVIEW_REQUIRED", "Configuration review or unresolved work remains; fill is not admitted.");
        if (catalog.Policy.Id is Form941Policy or ScheduleBPolicy)
        {
            var original = ProductionProfileRegistry.Check(profileBytes, observedTemplateSha256);
            string required = original.Candidate?.Id == "irs-941" ? Form941Policy : ScheduleBPolicy;
            long length = required == Form941Policy ? 841648 : 97028;
            if (!original.ProductionTrusted || catalog.Policy.Id != required || observedTemplateBytes != length)
                Error("E_CATALOG_BUILTIN_IDENTITY", "A built-in strategy requires its unchanged registered profile and template pair.");
        }
        else if (catalog.Policy.Id == ConfiguredPolicy)
        {
            if (profile.Schema != "pdfform.profile/3" || profile.Capabilities.Any(c => c is not ("text.singleWidget" or "checkbox.singleWidget")))
                Error("E_CATALOG_POLICY_UNSUPPORTED", "Configured clean forms require the context-free single-widget profile policy.");
            var text = catalog.Policy.TextFields.ToDictionary(f => f.Target, StringComparer.Ordinal);
            var checks = catalog.Policy.CheckboxFields.ToDictionary(f => f.Target, StringComparer.Ordinal);
            var expected = profile.EnumerateTargets().ToHashSet(StringComparer.Ordinal);
            var declared = text.Keys.Concat(checks.Keys).ToHashSet(StringComparer.Ordinal);
            bool missingTargets = expected.Except(declared).Any();
            if (declared.Except(expected).Any() || missingTargets && catalog.Review.State != "draft")
                Error("E_CATALOG_TARGET_COVERAGE", "Catalog policies must cover every profile target exactly once with no extras.");
            if (missingTargets && catalog.Review.State == "draft")
                Error("E_CATALOG_TARGET_REQUIRED", "Draft configuration still needs a policy for one or more mapped targets.");
            foreach (var key in profile.Keys)
            {
                bool checkbox = key.Kind is "checkbox" or "oneOf";
                foreach (var target in key.EnumerateTargets())
                {
                    // A missing draft policy is unresolved configuration, not invented type/page evidence.
                    if (catalog.Review.State == "draft" && !checks.ContainsKey(target) && !text.ContainsKey(target)) continue;
                    if (checkbox ? !checks.ContainsKey(target) : !text.ContainsKey(target))
                        Error("E_CATALOG_TARGET_TYPE", "A target policy has the wrong field family.");
                    int page = checkbox ? checks.GetValueOrDefault(target)?.Page ?? 0 : text.GetValueOrDefault(target)?.Page ?? 0;
                    if (page < 1 || page > profile.Template.PageCount) Error("E_CATALOG_TARGET_PAGE", "A declared target page is outside the profile page count.");
                }
                if (key.Kind == "checkbox" && key.On is not null && checks.TryGetValue(key.Target!, out var targetPolicy) && key.On != targetPolicy.OnState)
                    Error("E_CATALOG_ON_STATE", "A checkbox profile export state disagrees with its exact policy.");
                if (key.Options is not null)
                    foreach (var assignment in key.Options.Values.SelectMany(x => x))
                        if (assignment.On is not null && checks.TryGetValue(assignment.Target, out var optionPolicy) && assignment.On != optionPolicy.OnState)
                            Error("E_CATALOG_ON_STATE", "An option export state disagrees with its exact policy.");
                foreach (var target in key.EnumerateTargets())
                {
                    if (!text.TryGetValue(target, out var fieldPolicy)) continue;
                    var layouts = (profile.Options.DateCellLayouts ?? []).Where(l => l.Target == target && l.TemplateSha256 == catalog.Template.Sha256).ToArray();
                    if (fieldPolicy.Layout == "eight-date-cells-v1")
                    {
                        if (key.Kind != "date" || key.Format != "MMddyyyy")
                            Error("E_CATALOG_POLICY_UNSUPPORTED", "Eight-cell placement requires an eight-digit date key.");
                        else if (layouts.Length != 1)
                            Error("E_CATALOG_DATE_LAYOUT_REQUIRED", "An eight-cell date target still needs its reviewed geometry.");
                    }
                    else if (layouts.Length != 0)
                        Error("E_CATALOG_DATE_LAYOUT_CONFLICT", "Profile date-cell geometry conflicts with the selected single-line layout.");
                }
            }
            foreach (var layout in profile.Options.DateCellLayouts ?? [])
                if (text.TryGetValue(layout.Target, out var field) && layout.Cells.Any(c => c[0] < field.Rectangle[0] || c[1] < field.Rectangle[1]
                    || c[2] > field.Rectangle[2] || c[3] > field.Rectangle[3]))
                    Error("E_CATALOG_DATE_GEOMETRY", "A date cell exceeds its configured target rectangle.");
        }
        return findings.Count == 0 ? new(new FormConfigurationSnapshot(catalogBytes, profileBytes, catalog), []) : new(null, findings.AsReadOnly());
    }

    public static ParseResult<FormConfigurationSnapshot> BindDefault(ReadOnlySpan<byte> profileJson, string observedTemplateSha256, long observedTemplateBytes)
    {
        var original = ProductionProfileRegistry.Bind(profileJson, observedTemplateSha256);
        if (!original.IsValid) return new(null, original.Findings);
        using var stream = original.Value!.OpenRead(); using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        byte[] profile = buffer.ToArray(); var identity = original.Value.Identity;
        var catalog = new FormCatalogDefinition
        {
            Schema = "pdfform.catalog/1", Template = new("template.pdf", identity.Id == "irs-941" ? 841648 : 97028, identity.TemplateSha256),
            Profile = new("profile.json", profile.Length, identity.ProfileSha256, identity.Id, identity.Revision, identity.ProfileVersion),
            Review = new("reviewed", []), Policy = new()
            {
                Id = identity.Id == "irs-941" ? Form941Policy : ScheduleBPolicy,
                Normalization = "reviewed-irs-static-hybrid-v1", TextFields = [], CheckboxFields = []
            }
        };
        return Bind(JsonSerializer.SerializeToUtf8Bytes(catalog, StrictJson.Options), profile, observedTemplateSha256, observedTemplateBytes);
    }

    /// <summary>Only compatibility/check classification; fill still must refuse any failed binding.</summary>
    public static int AssessmentExitCode(IReadOnlyList<Finding> findings) =>
        findings.Any(f => f.Severity == "error" && f.Code is not ("E_CATALOG_POLICY_UNSUPPORTED" or "E_CATALOG_REVIEW_REQUIRED" or "E_CATALOG_DATE_LAYOUT_REQUIRED" or "E_CATALOG_TARGET_REQUIRED")) ? 2
        : findings.Any(f => f.Severity == "error" && f.Code == "E_CATALOG_POLICY_UNSUPPORTED") ? 3
        : findings.Any(f => f.Severity == "error") ? 1 : 0;
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>Owned whole configuration identity. Parse methods return fresh trees; no mutable registry or policy escape.</summary>
public sealed class FormConfigurationSnapshot
{
    private readonly byte[] catalogBytes, profileBytes;
    internal FormConfigurationSnapshot(byte[] ownedCatalog, byte[] ownedProfile, FormCatalogDefinition validated)
    {
        catalogBytes = ownedCatalog; profileBytes = ownedProfile; CatalogSha256 = FormCatalog.Hash(catalogBytes);
        TemplateSha256 = validated.Template.Sha256; TemplateBytes = validated.Template.Bytes;
        ProfileIdentity = new(validated.Profile.Id, validated.Profile.Revision, validated.Profile.ProfileVersion, validated.Profile.Sha256);
        IsBuiltIn = validated.Policy.Id is FormCatalog.Form941Policy or FormCatalog.ScheduleBPolicy;
    }
    public string CatalogSha256 { get; }
    public string TemplateSha256 { get; }
    public long TemplateBytes { get; }
    public ProfileIdentity ProfileIdentity { get; }
    public bool IsBuiltIn { get; }
    public FormCatalogDefinition ParseCatalog() => FormCatalog.ParseCatalog(catalogBytes).Value ?? throw new InvalidOperationException("E_CATALOG_SNAPSHOT");
    public ProfileDefinition ParseProfile() => ProfileParser.ParseProfile(profileBytes).Value ?? throw new InvalidOperationException("E_PROFILE_SNAPSHOT");
    public FormPolicyDefinition ParsePolicy() => ParseCatalog().Policy;
    public Stream OpenProfileRead() => new MemoryStream(profileBytes, 0, profileBytes.Length, writable: false, publiclyVisible: false);
    public Stream OpenCatalogRead() => new MemoryStream(catalogBytes, 0, catalogBytes.Length, writable: false, publiclyVisible: false);
}
