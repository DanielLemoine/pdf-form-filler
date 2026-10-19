using System.Text.Json.Serialization;
using PdfForm.Core;

namespace PdfForm.Pdf;

public sealed record PdfBooleanInspection(string State, bool? Value);
public sealed record FieldFlags(bool ReadOnly, bool Required, bool NoExport, bool Multiline, bool Password,
    bool Comb, bool RichText);
public sealed record FontInspection(string? Name, double? Size, string? Subtype, bool ResourcePresent,
    bool Embedded, string AttributeSource);
public sealed record EffectiveAttributes(FieldFlags Flags, int? MaxLength, string Alignment, FontInspection Font);
public sealed record WidgetInspection(string Id, int? Page, IReadOnlyList<double> Rectangle,
    bool GeometryValid, EffectiveAttributes Effective, int AnnotationFlags, int Rotation,
    bool HasNormalAppearance, bool HasNormalOffAppearance, IReadOnlyList<string> OnStates,
    int OnStateCount, string Linkage);
public sealed record FieldInspection(string Id, string? Name, string Type, EffectiveAttributes Effective,
    IReadOnlyList<WidgetInspection> Widgets, IReadOnlyList<string> AllowedOnStates,
    int OnStateCount, string DescriptionSource);
public sealed record XfaInspection(bool Present, string Kind, int? ScriptCount, bool XmlParsed,
    bool DynamicRenderForbidden, bool KnownScriptRemovalException);
public sealed record SignatureInspection(int SigFlags, int ApprovalSignatureCount, bool CertificationPresent,
    int UsageRightsSignatureCount, bool ApprovedUsageRightsException,
    IReadOnlyList<string> RemovableUsageRightsKeys, bool UnknownPermissionEntry);
public sealed record CatalogActionIdentity(string Name, string ActionType, string Sha256);
public sealed record CatalogJavaScriptInspection(int Count, bool ApprovedRemovalRequired, IReadOnlyList<CatalogActionIdentity> ApprovedActions);
public sealed record CapabilityInspection(string Capability, string Status, int Count, string ReasonCode);

/// <summary>No values, default values, tooltip text, arbitrary metadata, raw scripts, datasets or embedded payloads.</summary>
public sealed class TemplateInspection
{
    public string Schema => "pdfform.inspect/2";
    public required string Sha256 { get; init; }
    public required int Bytes { get; init; }
    public int Pages { get; init; }
    public bool Encrypted { get; init; }
    public bool HasAcroForm { get; init; }
    public int CatalogJavaScriptCount => CatalogJavaScript.Count;
    public CatalogJavaScriptInspection CatalogJavaScript { get; init; } = new(0, false, []);
    public bool ExactApprovedTemplate { get; init; }
    public string? ApprovedTemplateId { get; init; }
    public string? ApprovedTitle { get; init; }
    public string? StructureFingerprintV1 { get; init; }
    public PdfBooleanInspection NeedAppearances { get; init; } = new("missing", null);
    public PdfBooleanInspection NeedsRendering { get; init; } = new("missing", null);
    public XfaInspection Xfa { get; init; } = new(false, "none", 0, false, false, false);
    public SignatureInspection Signatures { get; init; } = new(0, 0, false, 0, false, [], false);
    public IReadOnlyList<FieldInspection> Fields { get; init; } = [];
    public IReadOnlyList<WidgetInspection> OrphanWidgets { get; init; } = [];
    public int TerminalFieldCount => Fields.Count;
    public int WidgetCount => Fields.Sum(f => f.Widgets.Count) + OrphanWidgets.Count;
    public IReadOnlyList<CapabilityInspection> Capabilities { get; init; } = [];
    public IReadOnlyList<Finding> Findings { get; init; } = [];
    public bool InitialStructureSupported { get; init; }
    [JsonIgnore] internal string? ActualTitle { get; init; }
    [JsonIgnore] internal IReadOnlyDictionary<string, FieldInspection> NamedFields { get; init; }
        = new Dictionary<string, FieldInspection>();
}

public sealed record BindingResult(string Schema, string TemplateSha256, bool Bound,
    IReadOnlyList<string> CompletedChecks, IReadOnlyList<string> NotRunChecks,
    IReadOnlyList<Finding> Findings, TemplateInspection Inspection);
