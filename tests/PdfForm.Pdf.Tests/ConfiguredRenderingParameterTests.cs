using System.Text;
using System.Text.Json;
using PdfForm.Core;
using PdfForm.Pdf;

// Prospective source-only integration helper for the frozen Core/PDF consumer
// APIs. Root owns harness wiring, coordinated source closure and execution.
internal static class ConfiguredRenderingParameterTests
{
    internal static void Run(Action<bool, string> check, string root)
    {
        var template = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", "f941.pdf"));
        byte[] profileBytes = File.ReadAllBytes(Path.Combine(root, "profiles", "irs-941-2026-03.json"));
        byte[] dataBytes = File.ReadAllBytes(Path.Combine(root, "samples", "941-2026q3-monthly-final-refund.json"));
        var admitted = FormCatalog.BindDefault(profileBytes, template.Sha256, template.ByteCount);
        check(admitted.IsValid, "default sealed configuration admission");
        var configuration = admitted.Value!;
        check(template.MatchesConfiguration(configuration), "configuration matches exact immutable blank bytes");
        var profile = configuration.ParseProfile();
        var policy = configuration.ParsePolicy();
        check(configuration.IsBuiltIn && policy.Id == FormCatalog.Form941Policy
            && policy.TextFields.Length == 0 && policy.CheckboxFields.Length == 0,
            "built-in configuration retains reviewed template policy rather than invented per-field facts");
        profile.Keys[0] = profile.Keys[0] with { Key = "PRIVATE_MUTATED_KEY" };
        profile.Template.Sha256Allowlist[0] = new string('0', 64);
        check(configuration.ParsePolicy().Id == FormCatalog.Form941Policy
            && configuration.ParseProfile().Keys.All(key => key.Key != "PRIVATE_MUTATED_KEY")
            && configuration.ParseProfile().Template.Sha256Allowlist.Contains(template.Sha256),
            "fresh defensive trees cannot mutate sealed profile or placement facts");

        var malformed = VerifiedFillPipeline.Prepare(template, configuration, Encoding.UTF8.GetBytes("{}"), flatten: false);
        check(malformed.ExitCode == 2 && !malformed.ReadyToPublish && malformed.ValidationLevels["syntax"] == "failed",
            "configured prepare retains syntax exit 2 and no candidate");
        byte[] different = File.ReadAllBytes(Path.Combine(root, "fixtures", "appearance", "forms", "f941.pdf"));
        different[^1] ^= 1;
        var wrongTemplate = VerifiedFillPipeline.Prepare(TemplateSnapshot.FromBytes(different), configuration, dataBytes, flatten: false);
        check(wrongTemplate.ExitCode == 3 && !wrongTemplate.ReadyToPublish && wrongTemplate.ValidationLevels["binding"] == "failed"
            && wrongTemplate.Findings.Any(finding => finding.Code == "E_TEMPLATE_PROFILE"),
            "exact template-byte mismatch fails before parse/render");

        const string privateKey = "PRIVATE_LOGICAL_KEY_SENTINEL";
        const string privateValue = "PRIVATE_PAYLOAD_SENTINEL";
        var unknown = VerifiedFillPipeline.CheckValueFit(template, configuration, privateKey, JsonSerializer.SerializeToElement(privateValue));
        check(unknown.Any(finding => finding.Code == "E_FIT_KEY"), "unknown logical fit key rejected without echo");
        string unknownJson = JsonSerializer.Serialize(unknown);
        check(!unknownJson.Contains(privateKey, StringComparison.Ordinal) && !unknownJson.Contains(privateValue, StringComparison.Ordinal),
            "per-value fit findings never echo unknown keys or values");
        var oversized = VerifiedFillPipeline.CheckValueFit(template, configuration, "name", JsonSerializer.SerializeToElement(new string('X', 513)));
        check(oversized.Count == 1 && oversized[0].Code == "E_TEXT_LIMIT"
            && oversized[0].Key == VerifiedFillPipeline.DiagnosticFieldId("name"),
            "per-value input bound uses an opaque logical-key identifier");
        var invalidDate = VerifiedFillPipeline.CheckValueFit(template, configuration, "line17.finalWageDate", JsonSerializer.SerializeToElement(privateValue));
        check(invalidDate.Any(finding => finding.Severity == "error")
            && invalidDate.All(finding => finding.Key is null || finding.Key.StartsWith("field_", StringComparison.Ordinal))
            && !JsonSerializer.Serialize(invalidDate).Contains(privateValue, StringComparison.Ordinal),
            "normalization refusal keeps private data out of fit diagnostics");

        // No unrelated required inputs or IRS filing context are needed for an
        // advisory check of one already normalized key's physical targets.
        var nameFit = VerifiedFillPipeline.CheckValueFit(template, configuration, "name", JsonSerializer.SerializeToElement("EXAMPLE WATER DISTRICT"));
        check(nameFit.All(finding => finding.Severity != "error"), "per-value fit does not fabricate or require unrelated context");
        var prepared = VerifiedFillPipeline.Prepare(template, configuration, dataBytes, flatten: false);
        check(prepared.ReadyToPublish && prepared.ExitCode == 0 && prepared.ValidationLevels["render"] == "passed",
            "configured full sample requires actual verified normal appearances");
        var sampleFit = VerifiedFillPipeline.CheckSampleFit(template, configuration, dataBytes);
        check(sampleFit.All(finding => finding.Severity != "error"), "whole-sample fit shares full configured preparation");
        check(prepared.Findings.All(finding => finding.Key is null || finding.Key.StartsWith("field_", StringComparison.Ordinal))
            && prepared.Findings.All(finding => finding.Field is null || finding.Field.StartsWith("field_", StringComparison.Ordinal)),
            "configured render diagnostics use only opaque key/field identities");
    }
}
