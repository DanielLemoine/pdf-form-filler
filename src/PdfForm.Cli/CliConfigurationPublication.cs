using PdfForm.Core;
using PdfForm.Pdf;
using PdfForm.Publication;

namespace PdfForm.Cli;

/// <summary>CLI-to-configuration-publication adapter only. Draft construction, semantic admission,
/// candidate verification and receipt serialization remain with their owning Core/PDF contracts.
/// This never substitutes a run/render receipt or performs its own filesystem publication.</summary>
public static class CliConfigurationPublication
{
    /// <summary>Pure request assembly. The caller supplies admitted bytes and independent reopened
    /// candidate verifiers. No paths are opened, no receipt is synthesized and no output is reserved.</summary>
    public static ConfigurationPackageRequest Prepare(CliOnboardingRequest command,
        string requestId, string toolBuild, IReadOnlyList<string> inputPaths,
        ReadOnlyMemory<byte> catalogBytes, ReadOnlyMemory<byte> profileBytes,
        Func<Stream, bool> verifyCatalog, Func<Stream, bool> verifyProfile,
        Func<IReadOnlyList<ArtifactDescriptor>, byte[]> createResult,
        ReadOnlyMemory<byte>? templateBytes = null, Func<Stream, bool>? verifyTemplate = null)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentNullException.ThrowIfNull(verifyCatalog); ArgumentNullException.ThrowIfNull(verifyProfile);
        ArgumentNullException.ThrowIfNull(createResult);
        string operation = command.Operation switch
        {
            CliOnboardingOperation.Configure => "configure",
            CliOnboardingOperation.SaveConfig => "save-config",
            _ => throw new CliConfigurationInputException("E_CONFIGURATION_OPERATION")
        };
        if (string.IsNullOrWhiteSpace(command.Output)) throw new CliConfigurationInputException("E_CONFIGURATION_DESTINATION");
        if (command.Operation == CliOnboardingOperation.SaveConfig && !command.Reviewed)
            throw new CliConfigurationInputException("E_CONFIGURATION_REVIEW_REQUIRED");
        if (catalogBytes.Length is <= 0 or > Limits.MaxJsonBytes || profileBytes.Length is <= 0 or > Limits.MaxJsonBytes)
            throw new CliConfigurationInputException("E_CONFIGURATION_SIZE");
        bool save = command.Operation == CliOnboardingOperation.SaveConfig;
        if (save != templateBytes.HasValue || save != (verifyTemplate is not null))
            throw new CliConfigurationInputException("E_CONFIGURATION_TEMPLATE_REQUIRED");
        if (templateBytes is { } template && template.Length is <= 0 or > TemplateSnapshot.MaximumBytes)
            throw new CliConfigurationInputException("E_CONFIGURATION_SIZE");

        // Keep owned byte/path snapshots across the later publication call. The new publisher owns
        // fixed filenames, result/completion validation, create-new commit and uncertain outcomes.
        var paths = Array.AsReadOnly(inputPaths.ToArray());
        byte[] catalog = catalogBytes.ToArray(), profile = profileBytes.ToArray();
        return templateBytes is { } bytes
            ? new(command.Output, requestId, toolBuild, operation, paths, catalog, profile,
                verifyCatalog, verifyProfile, createResult, bytes.ToArray(), verifyTemplate!)
            : new(command.Output, requestId, toolBuild, operation, paths, catalog, profile,
                verifyCatalog, verifyProfile, createResult, null, null);
    }

    public static PublicationOutcome Publish(CliOnboardingRequest command,
        string requestId, string toolBuild, IReadOnlyList<string> inputPaths,
        ReadOnlyMemory<byte> catalogBytes, ReadOnlyMemory<byte> profileBytes,
        Func<Stream, bool> verifyCatalog, Func<Stream, bool> verifyProfile,
        Func<IReadOnlyList<ArtifactDescriptor>, byte[]> createResult,
        ReadOnlyMemory<byte>? templateBytes = null, Func<Stream, bool>? verifyTemplate = null,
        PublicationLimits? limits = null)
    {
        var request = Prepare(command, requestId, toolBuild, inputPaths, catalogBytes, profileBytes,
            verifyCatalog, verifyProfile, createResult, templateBytes, verifyTemplate);
        // Return the owner's outcome verbatim, including unknown/4 and its empty output set.
        // No fallback to SafePublisher.PublishPackage or run/render completion is permitted here.
        return Publish(request, limits);
    }

    public static PublicationOutcome Publish(ConfigurationPackageRequest request, PublicationLimits? limits = null) =>
        new ConfigurationPublisher(limits ?? new()).Publish(request);
}
