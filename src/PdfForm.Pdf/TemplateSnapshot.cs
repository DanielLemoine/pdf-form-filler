using System.Security.Cryptography;
using PdfForm.Core;

namespace PdfForm.Pdf;

/// <summary>A bounded, immutable copy. Hashing and every parser use these exact bytes, never reopen the input path.</summary>
public sealed class TemplateSnapshot
{
    public const int MaximumBytes = 50 * 1024 * 1024;
    private readonly byte[] bytes;
    private TemplateSnapshot(byte[] ownedBytes)
    {
        bytes = ownedBytes;
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    public string Sha256 { get; }
    public int ByteCount => bytes.Length;
    /// <summary>Compare the exact immutable byte identity already observed by the caller; no path is reopened.</summary>
    public bool MatchesConfiguration(FormConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.TemplateSha256 == Sha256 && configuration.TemplateBytes == ByteCount;
    }
    public Stream OpenRead() => new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false);
    public static TemplateSnapshot FromBytes(ReadOnlySpan<byte> input)
    {
        if (input.Length == 0 || input.Length > MaximumBytes) throw new TemplateInputException("E_PDF_SIZE");
        return new(input.ToArray());
    }
    public static TemplateSnapshot Load(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length <= 0 || file.Length > MaximumBytes) throw new TemplateInputException("E_PDF_SIZE");
            // An in-memory snapshot avoids hash-check/reopen substitution even on platforms where file sharing is advisory.
            using var copy = new MemoryStream((int)file.Length);
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = file.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (copy.Length + count > MaximumBytes) throw new TemplateInputException("E_PDF_SIZE");
                copy.Write(buffer, 0, count);
            }
            if (copy.Length == 0) throw new TemplateInputException("E_PDF_SIZE");
            return new(copy.ToArray());
        }
        catch (TemplateInputException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw new TemplateInputException("E_PDF_INPUT"); }
    }
}

public sealed class TemplateInputException(string code) : Exception("Template input could not be read safely.")
{
    public string Code { get; } = code;
}

/// <summary>Release-owned template registry; a caller-edited profile cannot grant a known-XFA/Reader-rights exception.</summary>
public static class ApprovedTemplates
{
    public const string Form941Sha256 = "38a3d8cf7a455101d52543c8c48e66202bc25c189ead878627e35c797c88e2ad";
    public const string ScheduleBSha256 = "95a62159c8367fc50e6b5445f7d80f9a00ac7cdf56d224240fc2776cb545ab63";
    internal static ApprovedTemplate? Find(string hash) => hash switch
    {
        Form941Sha256 => new("irs-941", "2026-03", "Form 941 (Rev. March 2026)", 3, 116,
            "sha256:62fd1361e37118c6bcf7b25b6bc3fc38d7390fa4a15822551a88210d73609f72", 2,
            "03630fb4ecc464c44386ec692494053cdbdf03eebdd18337d2c899cf58b15b5d"),
        ScheduleBSha256 => new("irs-941sb", "2024-03", "Schedule B (Form 941) (Rev. March 2024)", 1, 212,
            "sha256:627aa3ebd89a32cf9ff5a9631dcacf91f2291181b452be4f4b3e206fc1a9cc69", 0,
            "ea03476631031025616f57817b5a0e5baa20bb91393d088caa63631bd53df373"),
        _ => null
    };
}
internal sealed record ApprovedTemplate(string Id, string Revision, string Title, int Pages, int Fields,
    string Fingerprint, int Scripts, string XfaTemplateSha256);
