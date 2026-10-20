using System.Security.Cryptography;

namespace PdfForm.Core;

public sealed record ReleaseProfileCandidate(string Id, string Revision, int ProfileVersion,
    string ProfileSha256, string TemplateSha256, string ReviewStatus);
public sealed record ProductionProfileCheck(bool ExactCandidateMatch, bool ProductionTrusted,
    ReleaseProfileCandidate? Candidate, IReadOnlyList<Finding> Findings);

/// <summary>
/// Release-owned profile bytes. Generic profile authoring validation is deliberately separate.
/// Exact digests admitted for bounded private renderer development are not release or filing approval.
/// No runtime enrollment, edited allowlist, or override flag is supported.
/// </summary>
public static class ProductionProfileRegistry
{
    public const string AdoptionScope = "bounded_private_development";
    public const string BaseContractReviewCut = "68f4e167afe4eff423f7cb844bf18b5717a4cbe5b73e5c025ec58185053829b0";
    // Separately reviewed successor: only version and exact measured date-cell geometry changed.
    public const string ReviewedDateLayoutProfileSha256 = "9283800c7e179e2f29cf2015021faaf859f4bf87820feaaf0c3299de0b01dd54";
    private const string AcceptedStatus = "accepted_private_development";
    private static readonly ReleaseProfileCandidate[] Entries =
    [
        new("irs-941", "2026-03", 2,
            ReviewedDateLayoutProfileSha256,
            "38a3d8cf7a455101d52543c8c48e66202bc25c189ead878627e35c797c88e2ad", AcceptedStatus),
        new("irs-941sb", "2024-03", 1,
            "e5503b46b8c54313875b5a97d4299962d6fe108e5d7f8a767c858c8606c2bcc7",
            "95a62159c8367fc50e6b5445f7d80f9a00ac7cdf56d224240fc2776cb545ab63", AcceptedStatus)
    ];
    public static IReadOnlyList<ReleaseProfileCandidate> Candidates => Array.AsReadOnly(Entries);

    public static ProductionProfileCheck Check(ReadOnlySpan<byte> profileBytes, string templateSha256)
    {
        if (profileBytes.Length is < 1 or > Limits.MaxJsonBytes)
            return new(false, false, null, [new("E_PROFILE_SIZE", "profile", "Profile exceeds the production input limit.")]);
        string digest = Convert.ToHexStringLower(SHA256.HashData(profileBytes));
        var candidate = Entries.SingleOrDefault(x => x.ProfileSha256 == digest && x.TemplateSha256 == templateSha256);
        if (candidate is null)
            return new(false, false, null, [new("E_PROFILE_NOT_RELEASED", "profile", "Exact profile and template bytes are not a registered release pair.")]);
        bool trusted = candidate.ReviewStatus == AcceptedStatus;
        return new(true, trusted, candidate, trusted ? Array.Empty<Finding>() : [new("E_PROFILE_REVIEW_PENDING", "profile", "Profile candidate has not completed independent implementation review.")]);
    }

    /// <summary>Default whole-binding adapter; the existing exact-pair admission remains authoritative.</summary>
    public static ParseResult<FormConfigurationSnapshot> BindConfiguration(ReadOnlySpan<byte> profileBytes,
        string templateSha256, long templateBytes) => FormCatalog.BindDefault(profileBytes, templateSha256, templateBytes);

    public static ParseResult<ProductionProfileSnapshot> Bind(ReadOnlySpan<byte> profileBytes, string templateSha256)
    {
        // Copy first: the hash, admission decision and later parses always use the same owned snapshot.
        if (profileBytes.Length is < 1 or > Limits.MaxJsonBytes)
            return new(null, [new("E_PROFILE_SIZE", "profile", "Profile exceeds the production input limit.")]);
        byte[] bytes = profileBytes.ToArray();
        var check = Check(bytes, templateSha256);
        return check.ProductionTrusted ? new(new ProductionProfileSnapshot(bytes, check.Candidate!), Array.Empty<Finding>()) : new(null, check.Findings);
    }
}

/// <summary>Immutable approved profile-byte snapshot; callers cannot mutate the bytes used for admission.</summary>
public sealed class ProductionProfileSnapshot
{
    private readonly byte[] bytes;
    internal ProductionProfileSnapshot(byte[] ownedBytes, ReleaseProfileCandidate identity) { bytes = ownedBytes; Identity = identity; }
    public ReleaseProfileCandidate Identity { get; }
    public Stream OpenRead() => new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false);
    public ProfileDefinition ParseDefinition() => ProfileParser.ParseProfile(bytes).Value ?? throw new InvalidOperationException("E_RELEASE_PROFILE_INVALID");
}
