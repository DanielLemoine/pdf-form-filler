namespace PdfForm.AppearanceSpike;

internal sealed record FixtureManifest(string Schema, string Scope, TemplateFixture[] Templates, DateGeometry DateGeometry);
internal sealed record TemplateFixture(string Id, string File, string Sha256, string ExpectedFont, int Pages,
    string NameTarget, string? DateTarget, TargetFixture[] Targets);
internal sealed record TargetFixture(string Name, string Type, int Page, float[] Rect, int Flags, int MaxLength,
    string DefaultAppearance, string SyntheticValue, string? OnState, string? OnAppearanceSha256);
internal sealed record DateGeometry(string TemplateSha256, string Field, string Source, float[] FieldRectangle,
    float[][] SlashRectangles, float[][] Cells, float PaddingPt, float SlashClearancePt);
internal sealed record CheckResult(string Id, string Status, string Code, string? Template = null, string? Field = null);
internal sealed record FontEvidence(string Template, string Name, bool Embedded, bool DigitGlyphs, bool LatinGlyphs,
    bool UnsupportedEmojiRejected, float FixedSizePt, string PortabilityStatus, string VerticalBoundsSource, float DigitInkHeightPt);
internal sealed record ArtifactEvidence(string Template, string Scenario, string Editable, string Flattened,
    string EditableSha256, string FlattenedSha256, int VerifiedFields, int VerifiedWidgets, int CheckboxTransitions,
    string AssignmentManifest);
internal sealed record SpikeReport(string Schema, string Status, string Decision, string Runtime, string OperatingSystem, string Architecture, string Engine,
    CheckResult[] Checks, FontEvidence[] Fonts, ArtifactEvidence[] Artifacts, string[] RemainingGates);
internal sealed class SpikeFailure(string code) : Exception(code)
{
    public string Code { get; } = code;
}
