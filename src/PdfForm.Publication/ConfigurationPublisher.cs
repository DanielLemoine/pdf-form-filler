using System.Text.Json;

namespace PdfForm.Publication;

/// <summary>
/// Configuration-only adapter over the existing package transaction. It adds no replacement,
/// staging, cleanup or recovery algorithm. Inputs and candidate verifiers remain caller supplied.
/// </summary>
public sealed class ConfigurationPublisher
{
    private readonly PublicationLimits limits;
    private readonly SafePublisher publisher;

    public ConfigurationPublisher(PublicationLimits? limits = null)
    { this.limits = limits ?? new(); publisher = new(this.limits); }

    internal ConfigurationPublisher(PublicationLimits limits, Action<PublicationPoint> fault,
        Action<StageCreationPoint>? afterCreate = null, Action? beforePathAccess = null)
    { this.limits = limits; publisher = new(limits, fault, afterCreate, beforePathAccess); }

    public PublicationOutcome Publish(ConfigurationPackageRequest request)
    {
        PackageRequest package;
        try
        {
            limits.Validate();
            if (request is null || request.InputPaths is null || request.VerifyCatalog is null ||
                request.VerifyProfile is null || request.CreateResult is null)
                throw new PublicationException("E_CONFIGURATION_ARGUMENT", 2);
            if (request.Operation is not ("configure" or "save-config"))
                throw new PublicationException("E_CONFIGURATION_OPERATION", 2);
            var expected = ExpectedArtifacts(request.Operation);
            bool save = request.Operation == "save-config";
            if (save != request.TemplatePdf.HasValue || save != (request.VerifyTemplate is not null))
                throw new PublicationException("E_CONFIGURATION_TEMPLATE_ARGUMENT", 2);
            var candidates = new List<CandidateArtifact>
            {
                new("catalog", "catalog.json", request.CatalogJson, request.VerifyCatalog),
                new("profile", "profile.json", request.ProfileJson, request.VerifyProfile)
            };
            if (save) candidates.Add(new("template", "template.pdf", request.TemplatePdf!.Value, request.VerifyTemplate!));
            CompletionProtocol.ValidateExpected(expected, limits);
            if (candidates.Any(a => a.Bytes.Length == 0 || a.Bytes.Length > limits.MaxArtifactBytes) ||
                checked(candidates.Sum(a => (long)a.Bytes.Length) + 2L * limits.MaxManifestBytes) > limits.MaxTotalBytes)
                throw new PublicationException("E_OUTPUT_LIMIT", 2);

            package = new(request.Destination, request.RequestId, request.ToolBuild,
                Array.AsReadOnly(request.InputPaths.ToArray()), candidates.AsReadOnly(), outputs =>
                {
                    var provided = request.CreateResult(outputs);
                    if (provided is null)
                        throw new PublicationException("E_CONFIGURATION_RESULT_CONTRACT");
                    if (provided.Length > limits.MaxManifestBytes)
                        throw new PublicationException("E_MANIFEST_LIMIT", 2);
                    var bytes = provided.ToArray();
                    // Use the existing strict UTF-8/duplicate/surrogate parser first. The separate
                    // operation check prevents a valid legacy run receipt from entering this API.
                    CompletionProtocol.ValidateResult(bytes, request.RequestId, request.ToolBuild, outputs);
                    using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                    if (json.RootElement.GetProperty("schema").GetString() != "pdfform.configuration-result/1" ||
                        json.RootElement.GetProperty("operation").GetString() != request.Operation)
                        throw new PublicationException("E_CONFIGURATION_RESULT_CONTRACT");
                    return bytes;
                });
        }
        catch (PublicationException e) { return new(false, e.ExitCode, "none", e.Code, []); }
        catch { return new(false, 4, "none", "E_CONFIGURATION_INTERNAL", []); }

        // Preserve the engine's exact partial-failure state, verified outputs and cleanup flag,
        // including published + failure after a successful namespace commit.
        return publisher.PublishPackage(package);
    }

    public static IReadOnlyList<ExpectedArtifact> ExpectedArtifacts(string operation) => operation switch
    {
        "configure" => Array.AsReadOnly(new[] { new ExpectedArtifact("catalog", "catalog.json"), new ExpectedArtifact("profile", "profile.json") }),
        "save-config" => Array.AsReadOnly(new[] { new ExpectedArtifact("catalog", "catalog.json"), new ExpectedArtifact("profile", "profile.json"), new ExpectedArtifact("template", "template.pdf") }),
        _ => throw new ArgumentException("E_CONFIGURATION_OPERATION", nameof(operation))
    };

    /// <summary>Only for an actually missing/unusable publisher outcome, never a known failure.</summary>
    public static PublicationOutcome UnconfirmedOutcome() =>
        new(false, 4, "unknown", "E_CONFIGURATION_PUBLICATION_UNCONFIRMED", [], true);

    /// <summary>Terminal status only. This does not create or alter the committed success receipt.</summary>
    public static string TerminalStatus(PublicationOutcome outcome) =>
        outcome.Success && outcome.ExitCode == 0 && outcome.ArtifactState == "published" ? "ok" :
        outcome.ExitCode switch { 1 => "validation_failed", 2 => "input_error", 3 => "unsupported", _ => "internal_error" };
}
