using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using PdfForm.Core;
using PdfForm.Pdf;

namespace PdfForm.Cli;

/// <summary>Immutable inputs for the fixed three-file configuration package. Loading is not catalog,
/// blankness, compatibility, review or rendering admission; the consumer must call the owning bind APIs.</summary>
public sealed class CliCatalogPackage
{
    private readonly byte[] catalog, profile;
    private readonly string[] inputPaths;
    private CliCatalogPackage(byte[] catalog, byte[] profile, TemplateSnapshot template, string[] inputPaths)
    {
        this.catalog = catalog; this.profile = profile; Template = template; this.inputPaths = inputPaths;
        CatalogSha256 = Convert.ToHexStringLower(SHA256.HashData(catalog));
        ProfileSha256 = Convert.ToHexStringLower(SHA256.HashData(profile));
    }
    public TemplateSnapshot Template { get; }
    public string CatalogSha256 { get; }
    public string ProfileSha256 { get; }
    [JsonIgnore] public IReadOnlyList<string> InputPaths => Array.AsReadOnly(inputPaths.ToArray());
    public byte[] CopyCatalog() => catalog.ToArray();
    public byte[] CopyProfile() => profile.ToArray();
    public Stream OpenCatalog() => new MemoryStream(catalog, 0, catalog.Length, writable: false, publiclyVisible: false);
    public Stream OpenProfile() => new MemoryStream(profile, 0, profile.Length, writable: false, publiclyVisible: false);

    public static CliCatalogPackage Load(string catalogPath, string? observedPdfPath = null)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
            string fullCatalog = Path.GetFullPath(catalogPath);
            if (!string.Equals(Path.GetFileName(fullCatalog), "catalog.json", StringComparison.Ordinal))
                throw new CliConfigurationInputException("E_CONFIGURATION_LAYOUT");
            string directory = Path.GetDirectoryName(fullCatalog) ?? throw new CliConfigurationInputException("E_CONFIGURATION_LAYOUT");
            string fullProfile = Path.Combine(directory, "profile.json");
            string packageTemplate = Path.Combine(directory, "template.pdf");
            string fullTemplate = observedPdfPath is null ? packageTemplate : Path.GetFullPath(observedPdfPath);
            // Every input becomes an owned bounded snapshot before binding or any publication.
            byte[] catalog = ReadJson(fullCatalog), profile = ReadJson(fullProfile);
            // Configure drafts intentionally omit template.pdf. An explicit PDF can be assessed
            // against that draft, while reviewed packages retain the exact three-file contract.
            bool draftWithoutCopy = observedPdfPath is not null && CliCompatibilityProjection.CatalogState(catalog) == "draft" && !File.Exists(packageTemplate);
            var packaged = draftWithoutCopy ? null : TemplateSnapshot.Load(packageTemplate);
            var template = string.Equals(packageTemplate, fullTemplate, StringComparison.Ordinal) && packaged is not null ? packaged : TemplateSnapshot.Load(fullTemplate);
            if (packaged is not null && (template.Sha256 != packaged.Sha256 || template.ByteCount != packaged.ByteCount))
                throw new CliConfigurationInputException("E_CONFIGURATION_TEMPLATE_MISMATCH");
            if (draftWithoutCopy)
            {
                var parsed = StrictJson.Parse<JsonObject>(catalog, "catalog");
                var identity = parsed.Value?["template"];
                if (identity?["path"]?.GetValue<string>() != "template.pdf" ||
                    identity?["sha256"]?.GetValue<string>() != template.Sha256 || identity?["bytes"]?.GetValue<long>() != template.ByteCount)
                    throw new CliConfigurationInputException("E_CONFIGURATION_TEMPLATE_MISMATCH");
            }
            return new(catalog, profile, template, new[] { packageTemplate, fullTemplate, fullProfile, fullCatalog }.Distinct(StringComparer.Ordinal).ToArray());
        }
        catch (CliConfigurationInputException) { throw; }
        catch (TemplateInputException) { throw new CliConfigurationInputException("E_CONFIGURATION_TEMPLATE"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or FormatException or OverflowException or System.Text.Json.JsonException)
        { throw new CliConfigurationInputException("E_CONFIGURATION_INPUT"); }
    }
    private static byte[] ReadJson(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > Limits.MaxJsonBytes) throw new CliConfigurationInputException("E_CONFIGURATION_SIZE");
        using var copy = new MemoryStream((int)file.Length);
        var buffer = new byte[65_536]; int count;
        while ((count = file.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (copy.Length + count > Limits.MaxJsonBytes) throw new CliConfigurationInputException("E_CONFIGURATION_SIZE");
            copy.Write(buffer, 0, count);
        }
        return copy.ToArray();
    }
}

public sealed class CliConfigurationInputException(string code) : Exception("Configuration input could not be read within its fixed package contract.")
{
    public string Code { get; } = code;
}
