using PdfForm.Core;

namespace PdfForm.Pdf;

public sealed partial class PdfTemplateInspector
{
    private static void ValidateObservedConfiguration(ConfigurationInspectionSession session, TemplateSnapshot snapshot,
        int pages, string fingerprint, List<Finding> findings)
    {
        if (session.Configuration is not { } configuration) return;
        var policy = configuration.ParsePolicy();
        var profile = configuration.ParseProfile();
        void Error(string code, string message) => findings.Add(new(code, "binding", message));
        if (policy.Id != "configured-acroform-v1" || policy.Normalization != "none-v1")
            Error("E_CONFIGURATION_POLICY", "Configured AcroForms require the exact supported policy and no-normalization mode.");
        if (!snapshot.MatchesConfiguration(configuration) || profile.Template.Sha256Allowlist.Length != 1
            || profile.Template.Sha256Allowlist[0] != snapshot.Sha256)
            Error("E_CONFIGURATION_TEMPLATE", "The configured exact template identity differs from the immutable observed bytes.");
        if (profile.Template.Title != session.Title || profile.Template.PageCount != pages
            || profile.Template.StructureFingerprintV1 != fingerprint)
            Error("E_CONFIGURATION_TEMPLATE", "The configured template metadata differs from the observed title, pages or fingerprint.");

        var targets = new Dictionary<string, ConfigurationTargetFacts>(StringComparer.Ordinal);
        foreach (var observed in session.Targets)
            if (!targets.TryAdd(observed.Target, observed)) Error("E_CONFIGURATION_POLICY", "Canonical configuration targets must be unique.");
        var textTargets = targets.Values.Where(t => t.Type == "text").Select(t => t.Target).ToHashSet(StringComparer.Ordinal);
        var checkboxTargets = targets.Values.Where(t => t.Type == "checkbox").Select(t => t.Target).ToHashSet(StringComparer.Ordinal);
        if (policy.TextFields.Select(p => p.Target).Distinct(StringComparer.Ordinal).Count() != policy.TextFields.Length
            || !textTargets.SetEquals(policy.TextFields.Select(p => p.Target))
            || policy.CheckboxFields.Select(p => p.Target).Distinct(StringComparer.Ordinal).Count() != policy.CheckboxFields.Length
            || !checkboxTargets.SetEquals(policy.CheckboxFields.Select(p => p.Target)))
            Error("E_CONFIGURATION_POLICY", "Configured policies must cover every observed text and checkbox target exactly once.");
        foreach (var text in policy.TextFields)
            if (targets.TryGetValue(text.Target, out var observed))
                ValidateObservedTextPolicy(text, observed, profile, snapshot.Sha256, findings);
        foreach (var checkbox in policy.CheckboxFields)
        {
            if (!targets.TryGetValue(checkbox.Target, out var observed)) continue;
            if (observed.Type != "checkbox" || checkbox.Page != observed.Page || !SameRectangle(checkbox.Rectangle, observed.Rectangle)
                || !observed.OnStates.Contains(checkbox.OnState, StringComparer.Ordinal))
                findings.Add(new("E_CONFIG_CHECKBOX_TARGET", "binding", "The checkbox target, page, rectangle or normal export state differs from observation.", Key: observed.Key));
            var assignments = profile.Keys.SelectMany(key => key.Kind == "checkbox" && key.Target == checkbox.Target
                ? new string?[] { key.On }
                : key.Kind == "oneOf" && key.Options is not null
                    ? key.Options.Values.SelectMany(options => options).Where(a => a.Target == checkbox.Target).Select(a => a.On)
                    : Enumerable.Empty<string?>());
            if (assignments.Any(on => on is null ? observed.OnStates.Count != 1 : on != checkbox.OnState))
                findings.Add(new("E_CONFIG_CHECKBOX_TARGET", "binding", "The profile checkbox assignment differs from its configured normal export state.", Key: observed.Key));
            ValidateObservedCheckboxMark(checkbox, observed, findings);
        }
    }

    private static void ValidateObservedTextPolicy(TextFieldPolicy policy, ConfigurationTargetFacts observed,
        ProfileDefinition profile, string templateSha256, List<Finding> findings)
    {
        void Error(string code, string message) => findings.Add(new(code, "binding", message, Key: observed.Key));
        var font = observed.Font;
        if (observed.Type != "text" || font is null || policy.Page != observed.Page
            || !SameRectangle(policy.Rectangle, observed.Rectangle))
        {
            Error("E_CONFIG_TEXT_TARGET", "The configured text target, page or rectangle differs from the observed canonical widget.");
            return;
        }
        if (policy.ResourceName != font.ResourceName || policy.BaseFont != font.BaseFont || policy.Subtype != font.Subtype
            || policy.Encoding != font.Encoding || policy.FontSize != font.FontSize || policy.MetricsSha256 != font.MetricsSha256
            || (font.Embedded ? policy.ProgramSha256 != font.ProgramSha256 : policy.ProgramSha256 is not null))
            Error("E_CONFIG_FONT_IDENTITY", "The configured source font identity differs from the observed effective resource.");
        if (font.FontSize is not (>= 1 and <= 72))
            Error("E_CONFIG_FONT_SIZE", "Configured rendering requires the observed fixed font size from 1 through 72 points.");
        if (font.Embedded && !font.ProgramUsable)
            Error("E_CONFIG_FONT_PROGRAM", "The observed embedded source font program cannot be parsed within the supported boundary.");
        switch (policy.Strategy)
        {
            case "preserve-embedded-simple-v1":
                if (font.Subtype != "TrueType" || font.Encoding != "WinAnsiEncoding" || font.ProgramKind != "FontFile2"
                    || !font.Embedded || !font.ProgramUsable || font.HasUnicodeMapping || policy.Repertoire != "encoded-font-v1")
                    Error("E_CONFIG_FONT_STRATEGY", "The observed font does not satisfy the configured embedded simple-font preservation strategy.");
                break;
            case "bundled-liberation-serif-ascii-v1":
                if (font.Subtype is not ("Type1" or "TrueType") || font.Encoding is not ("absent" or "WinAnsiEncoding")
                    || policy.Repertoire != "ascii-plus-eacute-v1")
                    Error("E_CONFIG_FONT_STRATEGY", "The observed source font does not satisfy the explicitly selected bundled-font strategy.");
                break;
            default:
                Error("E_CONFIG_FONT_STRATEGY", "The configured font strategy is unsupported.");
                break;
        }
        var owner = profile.Keys.SingleOrDefault(k => k.EnumerateTargets().Contains(observed.Target, StringComparer.Ordinal));
        var layouts = (profile.Options.DateCellLayouts ?? []).Where(l => l.Target == observed.Target).ToArray();
        if (policy.Layout == "single-line-v1")
        {
            if (layouts.Length != 0) Error("E_CONFIG_DATE_LAYOUT", "Single-line rendering cannot silently replace a declared eight-cell date layout.");
        }
        else if (policy.Layout == "eight-date-cells-v1")
        {
            if (owner is not { Kind: "date", Format: "MMddyyyy" } || layouts.Length != 1
                || layouts[0].TemplateSha256 != templateSha256 || layouts[0].Cells.Length != 8
                || layouts[0].Cells.Any(c => c.Length != 4 || c.Any(v => !double.IsFinite(v))
                    || c[0] >= c[2] || c[1] >= c[3] || c[0] < observed.Rectangle[0] || c[1] < observed.Rectangle[1]
                    || c[2] > observed.Rectangle[2] || c[3] > observed.Rectangle[3]))
                Error("E_CONFIG_DATE_LAYOUT", "Eight-cell date rendering requires its exact template-bound eight rectangles and MMddyyyy owner.");
        }
        else Error("E_CONFIG_TEXT_LAYOUT", "The configured text layout is unsupported.");
    }

    private static void ValidateObservedCheckboxMark(CheckboxFieldPolicy policy, ConfigurationTargetFacts observed, List<Finding> findings)
    {
        double size = policy.Mark switch { "cross-10-v1" => 10, "circle-12-v1" => 12, _ => 0 };
        if (size == 0 || observed.Rectangle.Count != 4
            || Math.Abs(observed.Rectangle[2] - observed.Rectangle[0] - size) >= .002
            || Math.Abs(observed.Rectangle[3] - observed.Rectangle[1] - size) >= .002)
            findings.Add(new("E_CONFIG_CHECKBOX_GEOMETRY", "binding", "The checkbox mark or observed widget dimensions are outside the supported fixed geometry.", Key: observed.Key));
    }

    private static bool SameRectangle(IEnumerable<double> expected, IReadOnlyList<double> actual)
    {
        var array = expected.Take(5).ToArray();
        return array.Length == 4 && actual.Count == 4
            && array.All(double.IsFinite) && actual.All(double.IsFinite)
            && array.Zip(actual).All(p => Math.Abs(p.First - p.Second) < .000001);
    }
}
