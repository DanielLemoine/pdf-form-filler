using System.Text;
using PdfForm.Core;
using PdfForm.Pdf;
using PdfForm.Publication;

namespace PdfForm.Cli;

public static partial class CliApplication
{
    private static CliExecution ExecuteConfigure(CliOnboardingRequest request)
    {
        const string operation = "configure";
        var levels = OnboardingLevels(); var findings = new List<Finding>();
        TemplateSnapshot? template = null; byte[]? candidateProfile = null;
        PublicationOutcome? published = null; bool publishing = false;
        try
        {
            template = TemplateSnapshot.Load(request.Pdf!); levels["syntax"] = "passed";
            var facts = new PdfTemplateInspector().DiscoverConfiguration(template);
            findings.AddRange(facts.Compatibility.Issues);
            if (facts.Compatibility.ExitCode is not (0 or 1))
                return ConfigurationResult(operation, facts.Compatibility.ExitCode, findings, levels, template);
            var candidate = CliConfigurationDraft.Create(facts);
            findings.AddRange(candidate.Findings);
            if (!candidate.IsValid)
            {
                ConfigurationRejected(levels, candidate.Findings);
                return ConfigurationResult(operation, candidate.Findings.Any(f => f.Code is "E_CONFIGURATION_ON_STATE" or "E_CONFIGURATION_FONT") ? 3 : 2,
                    findings, levels, template);
            }
            byte[] catalog = candidate.Value!.CopyCatalog(), profile = candidate.Value.CopyProfile();
            candidateProfile = profile; levels["profile"] = "passed";
            var prepared = CliConfigurationPublication.Prepare(request, Guid.NewGuid().ToString("N"), Tool.Build,
                [request.Pdf!], catalog, profile,
                source => ExactCandidate(source, catalog, bytes => FormCatalog.ParseCatalog(bytes).IsValid),
                source => ExactCandidate(source, profile, bytes => ProfileParser.ParseProfile(bytes).IsValid),
                outputs => CliConfigurationReceipt.Published(operation, template, profile, outputs, findings, structurallyBound: false));
            publishing = true;
            published = CliConfigurationPublication.Publish(prepared);
            AddConfigurationPublicationFindings(published, findings);
            return ConfigurationResult(operation, published.ExitCode, findings, levels, template, profile, published);
        }
        catch (CliConfigurationInputException ex)
        { levels["syntax"] = "failed"; return ConfigurationResult(operation, 2, [new(ex.Code, "syntax", ex.Code)], levels, template, candidateProfile, published); }
        catch (TemplateInputException ex)
        { levels["syntax"] = "failed"; return ConfigurationResult(operation, 2, [new(ex.Code, "syntax", ex.Code)], levels, template, candidateProfile, published); }
        catch
        {
            if (publishing && published is null) published = ConfigurationPublisher.UnconfirmedOutcome();
            return ConfigurationResult(operation, 4, [new("E_CONFIGURATION_INTERNAL", "internal", "E_CONFIGURATION_INTERNAL")],
                levels, template, candidateProfile, published);
        }
    }

    private static CliExecution ConfigurationResult(string operation, int exitCode, IEnumerable<Finding> findings,
        Dictionary<string, string>? levels = null, TemplateSnapshot? template = null, byte[]? profile = null,
        PublicationOutcome? publication = null, bool configurationNeeded = false)
    {
        ReadOnlyMemory<byte>? identity = profile is null ? null : new ReadOnlyMemory<byte>(profile);
        string state = publication?.ArtifactState ?? "none";
        byte[] bytes = CliConfigurationReceipt.Terminal(operation, exitCode, state, template, identity,
            publication?.Outputs ?? [], findings, levels ?? OnboardingLevels(), configurationNeeded);
        return new(exitCode, Encoding.UTF8.GetString(bytes), state);
    }

    private static CliExecution ExecuteSaveConfiguration(CliOnboardingRequest request)
    {
        const string operation = "save-config";
        var levels = OnboardingLevels(); var findings = new List<Finding>();
        TemplateSnapshot? template = null; byte[]? admittedProfile = null;
        PublicationOutcome? published = null; bool publishing = false;
        try
        {
            if (!request.Reviewed) return ConfigurationResult(operation, 1,
                [new("E_CONFIGURATION_REVIEW_REQUIRED", "profile", "E_CONFIGURATION_REVIEW_REQUIRED")], configurationNeeded: true);
            string directory = Path.GetFullPath(request.Draft!);
            string catalogPath = Path.Combine(directory, "catalog.json"), profilePath = Path.Combine(directory, "profile.json");
            template = TemplateSnapshot.Load(request.Pdf!);
            byte[] catalog = ReadJson(catalogPath, "catalog"), profile = ReadJson(profilePath, "profile");
            levels["syntax"] = "passed";
            var reviewed = CliConfigurationDraft.Review(catalog, profile, template);
            if (!reviewed.IsValid)
            {
                ConfigurationRejected(levels, reviewed.Findings);
                bool needsReview = reviewed.Findings.Any(f => f.Code is "E_CONFIGURATION_UNRESOLVED" or "E_CONFIGURATION_REVIEW_REQUIRED");
                int reviewExit = needsReview ? 1 : FormCatalog.AssessmentExitCode(reviewed.Findings);
                return ConfigurationResult(operation, reviewExit,
                    reviewed.Findings, levels, template, configurationNeeded: reviewExit == 1);
            }
            catalog = reviewed.Value!.CopyCatalog(); profile = reviewed.Value.CopyProfile();
            var bound = FormCatalog.Bind(catalog, profile, template.Sha256, template.ByteCount);
            if (!bound.IsValid)
            {
                ConfigurationRejected(levels, bound.Findings);
                return ConfigurationResult(operation, FormCatalog.AssessmentExitCode(bound.Findings), bound.Findings, levels, template,
                    configurationNeeded: FormCatalog.AssessmentExitCode(bound.Findings) == 1);
            }
            admittedProfile = profile; levels["profile"] = "passed";
            var compatibility = new PdfTemplateInspector().CheckCompatibility(template, bound.Value!);
            findings.AddRange(compatibility.Issues);
            levels["binding"] = levels["bindingStructure"] = compatibility.ExitCode == 0 ? "passed" : "failed";
            if (compatibility.ExitCode != 0) return ConfigurationResult(operation, compatibility.ExitCode, findings, levels,
                template, profile, configurationNeeded: compatibility.ExitCode == 1);

            using var original = template.OpenRead(); using var copy = new MemoryStream(); original.CopyTo(copy);
            byte[] templateBytes = copy.ToArray();
            var prepared = CliConfigurationPublication.Prepare(request, Guid.NewGuid().ToString("N"), Tool.Build,
                [request.Pdf!, catalogPath, profilePath], catalog, profile,
                source => ExactCandidate(source, catalog, bytes => FormCatalog.Bind(bytes, profile, template.Sha256, template.ByteCount).IsValid),
                source => ExactCandidate(source, profile, bytes => ProfileParser.ParseProfile(bytes).IsValid),
                outputs => CliConfigurationReceipt.Published(operation, template, profile, outputs, findings, structurallyBound: true),
                templateBytes, source => ExactCandidate(source, templateBytes, bytes =>
                    new PdfTemplateInspector().CheckCompatibility(TemplateSnapshot.FromBytes(bytes), bound.Value!).ExitCode == 0));
            publishing = true;
            published = CliConfigurationPublication.Publish(prepared);
            AddConfigurationPublicationFindings(published, findings);
            return ConfigurationResult(operation, published.ExitCode, findings, levels, template, profile, published);
        }
        catch (CliConfigurationInputException ex)
        {
            levels["syntax"] = "failed";
            bool review = ex.Code == "E_CONFIGURATION_REVIEW_REQUIRED";
            return ConfigurationResult(operation, review ? 1 : 2, [new(ex.Code, "syntax", ex.Code)], levels,
                template, admittedProfile, published, review);
        }
        catch (InputException ex)
        { levels["syntax"] = "failed"; return ConfigurationResult(operation, 2, [new(ex.Code, "syntax", ex.Code)], levels, template, admittedProfile, published); }
        catch (TemplateInputException ex)
        { levels["syntax"] = "failed"; return ConfigurationResult(operation, 2, [new(ex.Code, "syntax", ex.Code)], levels, template, admittedProfile, published); }
        catch
        {
            if (publishing && published is null) published = ConfigurationPublisher.UnconfirmedOutcome();
            return ConfigurationResult(operation, 4, [new("E_CONFIGURATION_INTERNAL", "internal", "E_CONFIGURATION_INTERNAL")],
                levels, template, admittedProfile, published);
        }
    }

    private static void AddConfigurationPublicationFindings(PublicationOutcome outcome, List<Finding> findings)
    {
        if (!outcome.Success) findings.Add(new(outcome.ErrorCode ?? "E_CONFIGURATION_PUBLICATION", "internal", "Configuration publication did not complete."));
        if (outcome.CleanupIncomplete) findings.Add(new("W_CONFIGURATION_CLEANUP_INCOMPLETE", "internal", "W_CONFIGURATION_CLEANUP_INCOMPLETE", Severity: "warning"));
    }

    private static bool ExactCandidate(Stream source, ReadOnlyMemory<byte> expected, Func<byte[], bool> verify)
    {
        using var observed = new MemoryStream(expected.Length); var buffer = new byte[65_536]; int count;
        while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (observed.Length + count > expected.Length) return false;
            observed.Write(buffer, 0, count);
        }
        byte[] bytes = observed.ToArray();
        return bytes.AsSpan().SequenceEqual(expected.Span) && verify(bytes);
    }
}
