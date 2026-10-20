using System.Text;
using PdfForm.Cli;

namespace PdfForm.Cli.Tests;

// Pure request-assembly cases. No publisher, PDF parser, native API or filesystem is invoked.
// Registration remains owned by the common test Program owner.
public static class ConfigurationPublicationCases
{
    public static void Register(Action<string, Action> test)
    {
        test("configuration publication adapter requires reviewed save and template verifier", () =>
        {
            var unreviewed = new CliOnboardingRequest(CliOnboardingOperation.SaveConfig,
                "PRIVATE_SENTINEL.pdf", null, "draft", null, "new-package");
            Refused(unreviewed, "E_CONFIGURATION_REVIEW_REQUIRED");
            var reviewed = unreviewed with { Reviewed = true };
            Refused(reviewed, "E_CONFIGURATION_TEMPLATE_REQUIRED");
            Refused(reviewed, "E_CONFIGURATION_TEMPLATE_REQUIRED", Encoding.UTF8.GetBytes("synthetic-pdf"));
            Refused(reviewed, "E_CONFIGURATION_TEMPLATE_REQUIRED", verifyTemplate: _ => true);
            Refused(reviewed with { Operation = CliOnboardingOperation.Configure }, "E_CONFIGURATION_TEMPLATE_REQUIRED",
                Encoding.UTF8.GetBytes("synthetic-pdf"), _ => true);
        });
        test("configuration publication adapter never accepts render or validation operations", () =>
        {
            foreach (var operation in new[] { CliOnboardingOperation.Check, CliOnboardingOperation.CatalogFill,
                CliOnboardingOperation.CatalogValidate, CliOnboardingOperation.SampleInput })
                Refused(new(operation, "PRIVATE_SENTINEL.pdf", null, null, null, "new-package"), "E_CONFIGURATION_OPERATION");
        });
        test("configuration publication request assembly is independent of destination filesystem", () =>
        {
            byte[] json = Encoding.UTF8.GetBytes("{}");
            var draft = new CliOnboardingRequest(CliOnboardingOperation.Configure,
                "PRIVATE_SENTINEL.pdf", null, null, null, "PRIVATE_SENTINEL/new-draft");
            // The owner publisher will validate these intentionally synthetic catalog/profile bytes.
            // Constructing its request must not open or create any of these nonexistent paths.
            var request = CliConfigurationPublication.Prepare(draft, "synthetic-request", "synthetic-build",
                ["PRIVATE_SENTINEL.pdf"], json, json, _ => true, _ => true, _ => []);
            Require(request is not null);
            var saved = CliConfigurationPublication.Prepare(draft with { Operation = CliOnboardingOperation.SaveConfig, Reviewed = true },
                "synthetic-request", "synthetic-build", ["PRIVATE_SENTINEL.pdf"], json, json,
                _ => true, _ => true, _ => [], Encoding.UTF8.GetBytes("synthetic-pdf"), _ => true);
            Require(saved is not null);
        });
    }

    private static void Refused(CliOnboardingRequest command, string code,
        ReadOnlyMemory<byte>? templateBytes = null, Func<Stream, bool>? verifyTemplate = null)
    {
        byte[] json = Encoding.UTF8.GetBytes("{}"); bool refused = false;
        try
        {
            _ = CliConfigurationPublication.Prepare(command, "synthetic-request", "synthetic-build",
                ["PRIVATE_SENTINEL.pdf"], json, json, _ => true, _ => true, _ => [], templateBytes, verifyTemplate);
        }
        catch (CliConfigurationInputException ex)
        {
            refused = ex.Code == code && !ex.ToString().Contains("PRIVATE_SENTINEL", StringComparison.Ordinal);
        }
        Require(refused);
    }
    private static void Require(bool condition)
    { if (!condition) throw new InvalidOperationException("Configuration publication adapter source regression."); }
}
