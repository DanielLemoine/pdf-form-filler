using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iText.Kernel.Pdf;
using PdfForm.Core;
using PdfForm.Pdf;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRoot();
var inspector = new PdfTemplateInspector();
int assertions = 0;
void Check(bool condition, string name)
{
    if (!condition) { Console.Error.WriteLine("FAIL: " + name); Environment.Exit(1); }
    assertions++;
}
TemplateInspection Inspect(byte[] bytes) => inspector.Inspect(TemplateSnapshot.FromBytes(bytes));
bool Has(TemplateInspection report, string code) => report.Findings.Any(f => f.Code == code);
var original = Fixture.Make();
var snapshot = TemplateSnapshot.FromBytes(original);
string snapshotHash = snapshot.Sha256;
original[0] ^= 1;
Check(snapshot.Sha256 == snapshotHash && snapshot.OpenRead().ReadByte() == '%', "snapshot owns independent bytes");
using (var stream = snapshot.OpenRead())
{
    Check(!stream.CanWrite, "snapshot stream is read-only");
    Check(stream is not MemoryStream memory || !memory.TryGetBuffer(out _), "snapshot stream does not expose backing array");
}
var basic = inspector.Inspect(snapshot);
Check(basic.TerminalFieldCount == 2 && basic.WidgetCount == 2 && basic.OrphanWidgets.Count == 0, "canonical two-terminal inventory");
Check(basic.Fields.All(f => f.Effective.Font.Size == 10 && f.Effective.Alignment == "right"), "ancestor default appearance and alignment");
Check(basic.Fields.All(f => f.Widgets.Single().Page == 1), "all page widgets linked");
Check(basic.NeedAppearances.State == "missing" && basic.NeedAppearances.Value is null, "missing Boolean distinct");
var falseBooleans = Inspect(Fixture.Make(booleans: "/NeedAppearances false", catalog: "/NeedsRendering false"));
Check(falseBooleans.NeedAppearances.State == "false" && falseBooleans.NeedAppearances.Value == false, "false NeedAppearances is false");
Check(falseBooleans.NeedsRendering.State == "false" && falseBooleans.NeedsRendering.Value == false, "false NeedsRendering is false");
Check(Inspect(Fixture.Make(booleans: "/NeedAppearances true")).NeedAppearances.Value == true, "true Boolean distinguished");
Check(Has(Inspect(Fixture.Make(booleans: "/NeedAppearances (false)")), "E_PDF_BOOLEAN"), "wrong Boolean type rejected");
var moved = Inspect(Fixture.Make(swap: true));
Check(basic.StructureFingerprintV1 == moved.StructureFingerprintV1, "wrong-box rectangle swap survives legacy fingerprint");
Check(basic.Sha256 != moved.Sha256, "wrong-box rectangle swap changes byte hash");
Check(basic.Fields[0].Widgets[0].Rectangle[0] != moved.Fields[0].Widgets[0].Rectangle[0], "inspector exposes changed geometry");
var sensitive = Inspect(Fixture.Make(sensitive: true));
string json = JsonSerializer.Serialize(sensitive);
Check(!json.Contains(Fixture.Sentinel, StringComparison.Ordinal), "privacy excludes values defaults names tooltip metadata script states");
Check(!json.Contains("CurrentValue", StringComparison.Ordinal) && !json.Contains("DefaultValue", StringComparison.Ordinal), "no value-bearing report properties");
Check(sensitive.Fields.All(f => f.Name is null), "unknown raw field names suppressed");
var statePrivacy = Inspect(Fixture.Make(sensitive: true, fieldType: "Btn", stateNames: true));
Check(statePrivacy.Fields[0].OnStateCount == 1 && !JsonSerializer.Serialize(statePrivacy).Contains(Fixture.Sentinel, StringComparison.Ordinal), "arbitrary on-state names suppressed");
var scriptPrivacy = Inspect(Fixture.Make(xfa: "<template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\"><script>" + Fixture.Sentinel + "</script></template>"));
Check(!JsonSerializer.Serialize(scriptPrivacy).Contains(Fixture.Sentinel, StringComparison.Ordinal), "script bodies never exported");
var adjacentScripts = Inspect(Fixture.Make(xfa: "<template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\"><script>one</script><script>two</script></template>"));
Check(adjacentScripts.Xfa.ScriptCount == 2, "adjacent XML scripts both counted");
Check(Has(Inspect(Fixture.Make(orphan: true)), "E_ORPHAN_WIDGET"), "orphan widget rejected");
Check(Has(Inspect(Fixture.Make(noPageWidget: true)), "E_WIDGET_NOT_ON_PAGE"), "canonical widget missing from page rejected");
Check(Has(Inspect(Fixture.Make(repeated: true)), "E_FIELD_CAPABILITY"), "repeated widgets rejected");
Check(Inspect(Fixture.Make(repeated: true)).Fields[0].Widgets.Count == 2, "repeated widgets fully enumerated");
foreach (var (type, flags, expected) in new[] { ("Btn", 32768, "radio"), ("Btn", 65536, "pushbutton"), ("Ch", 131072, "combo"), ("Ch", 0, "list"), ("Sig", 0, "signature") })
{
    var report = Inspect(Fixture.Make(fieldType: type, flags: flags));
    Check(report.Fields[0].Type == expected && Has(report, "E_FIELD_CAPABILITY"), "unsupported field classified " + expected);
}
Check(Has(Inspect(Fixture.Make(flags: 1)), "E_FIELD_POLICY"), "effective read-only flag");
Check(Has(Inspect(Fixture.Make(flags: 4)), "E_FIELD_POLICY"), "effective no-export flag");
Check(Has(Inspect(Fixture.Make(flags: 33554432)), "E_FIELD_CAPABILITY"), "rich text rejected");
Check(Has(Inspect(Fixture.Make(catalog: "/OpenAction 10 0 R", extra: "<< /S /JavaScript /JS (sentinel-no-execution) >>")), "E_ACTIVE_CONTENT"), "catalog action detected without execution");
Check(Has(Inspect(Fixture.Make(booleans: "/AA << /K 10 0 R >>", extra: "<< /S /JavaScript /JS (sentinel-no-execution) >>")), "E_ACTIVE_CONTENT"), "AcroForm action detected");
var sigFlags = Inspect(Fixture.Make(booleans: "/SigFlags 2"));
Check(sigFlags.Signatures.SigFlags == 2 && sigFlags.Signatures.ApprovalSignatureCount == 0, "SigFlags alone is not signature proof");
var signature = "<< /Type /Sig /SubFilter /adbe.pkcs7.detached /Contents <0102> /ByteRange [0 10 20 30] >>";
Check(Has(Inspect(Fixture.Make(extra: signature)), "E_APPROVAL_SIGNATURE"), "approval signature detected");
Check(Has(Inspect(Fixture.Make(catalog: "/Perms << /DocMDP 10 0 R >>", extra: signature)), "E_CERTIFICATION"), "certification detected");
var rights = "<< /Type /Sig /SubFilter /adbe.pkcs7.detached /Contents <0102> /ByteRange [0 10 20 30] /Reference [<< /TransformMethod /UR3 /TransformParams << /Type /TransformParams /V /2.2 >> >>] >>";
var unapprovedRights = Inspect(Fixture.Make(catalog: "/Perms << /UR3 10 0 R >>", extra: rights));
Check(unapprovedRights.Signatures.UsageRightsSignatureCount == 1 && !unapprovedRights.Signatures.ApprovedUsageRightsException, "Reader rights recognized separately from approval");
Check(unapprovedRights.Signatures.RemovableUsageRightsKeys.Count == 0 && Has(unapprovedRights, "E_USAGE_RIGHTS_UNAPPROVED"), "unknown byte hash grants no rights removal exception");
string staticXml = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\"><subform/></template>";
var staticUnknown = Inspect(Fixture.Make(xfa: staticXml));
Check(staticUnknown.Xfa.Kind == "unknown" && staticUnknown.Xfa.XmlParsed, "unknown XFA not guessed static");
Check(Inspect(Fixture.Make(xfa: staticXml, catalog: "/NeedsRendering true")).Xfa.Kind == "dynamic", "dynamic rendering flag honored");
var hostileXml = "<!DOCTYPE template [<!ENTITY xxe SYSTEM \"file:///not-a-real-permitted-source\">]><template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\">&xxe;</template>";
Check(Has(Inspect(Fixture.Make(xfa: hostileXml)), "E_XFA_XML"), "DTD and external entities prohibited");
Check(Has(Inspect(Fixture.Make(xfa: "<template>")), "E_XFA_XML"), "malformed XML rejected");
Check(Has(Inspect(Encoding.ASCII.GetBytes("not a pdf")), "E_PDF_PARSE"), "malformed PDF safe result");
var cleanNoForm = TemplateSnapshot.FromBytes(Fixture.Make(noForm: true));
Check(ApprovedTemplateNormalizer.HasNoActiveContent(cleanNoForm), "clean flattened no-form shape passes output active-content check");
Check(!inspector.Inspect(cleanNoForm).InitialStructureSupported, "clean no-form remains unsupported as input form");
foreach (var (candidate, reason) in new[]
{
    (Fixture.Make(noForm: true, catalog: "/OpenAction 10 0 R", extra: "<< /S /JavaScript /JS (synthetic-no-execution) >>"), "catalog action"),
    (Fixture.Make(noForm: true, pageAction: true, extra: "<< /S /JavaScript /JS (synthetic-no-execution) >>"), "page action"),
    (Fixture.Make(noForm: true, catalog: "/Names << /JavaScript << /Names [(synthetic) 10 0 R] >> >>", extra: "<< /S /JavaScript /JS (synthetic-no-execution) >>"), "catalog script"),
    (Fixture.Make(noForm: true, catalog: "/Perms << /UR3 10 0 R >>", extra: rights), "Reader rights remnant"),
    (Fixture.Make(noForm: true, catalog: "/Perms << /DocMDP 10 0 R >>", extra: signature), "certification remnant"),
    (Fixture.Make(noForm: true, extra: signature), "approval signature remnant"),
    (Fixture.Make(noForm: true, noFormWidgets: true), "orphan widgets"),
    (Fixture.Make(noForm: true, catalog: "/Outlines << /First << /Title (synthetic) /A 10 0 R >> >>", extra: "<< /S /JavaScript /JS (synthetic-no-execution) >>"), "outline JavaScript"),
    (Fixture.Make(noForm: true, annotations: "10 0 R", extra: "<< /Subtype /RichMedia /Rect [0 0 10 10] /RichMediaContent << >> >>"), "RichMedia annotation"),
    (Fixture.Make(noForm: true, annotations: "10 0 R", extra: "<< /Subtype /Screen /Rect [0 0 10 10] >>"), "Screen annotation")
}) Check(!ApprovedTemplateNormalizer.HasNoActiveContent(TemplateSnapshot.FromBytes(candidate)), "no-form output rejects " + reason);
Check(!ApprovedTemplateNormalizer.HasNoActiveContent(TemplateSnapshot.FromBytes(Encoding.ASCII.GetBytes("not a pdf"))), "output verification rejects parser failure");
TraversalBudgetTests.Run(Check);
var actualReports = new List<object>();
foreach (var (filename, id, count, pages, scripts, hash, fingerprint, font) in new[]
{
    ("f941.pdf", "irs-941-2026-03", 116, 3, 2, ApprovedTemplates.Form941Sha256, "sha256:62fd1361e37118c6bcf7b25b6bc3fc38d7390fa4a15822551a88210d73609f72", "TimesNewRomanPSMT"),
    ("f941sb.pdf", "irs-941sb-2024-03", 212, 1, 0, ApprovedTemplates.ScheduleBSha256, "sha256:627aa3ebd89a32cf9ff5a9631dcacf91f2291181b452be4f4b3e206fc1a9cc69", "TimesLTStd-Roman")
})
{
    var input = TemplateSnapshot.Load(Path.Combine(root, "fixtures", "appearance", "forms", filename));
    var report = inspector.Inspect(input);
    Check(input.Sha256 == hash, "exact approved bytes " + filename);
    Check(report.TerminalFieldCount == count && report.WidgetCount == count && report.Pages == pages, "complete approved inventory " + filename);
    Check(report.StructureFingerprintV1 == fingerprint, "preserved legacy fingerprint " + filename);
    Check(report.Fields.Where(f => f.Type == "text").All(f => f.Effective.Font.Name == font && f.Effective.Font.Size == 10), "effective native font " + filename);
    Check(report.Fields.Where(f => f.Type == "checkbox").All(f => f.Widgets.All(w => !w.HasNormalOffAppearance)), "normal Off deficiency classified " + filename);
    Check(report.Xfa.Kind == "staticHybridApproved" && report.Xfa.ScriptCount == scripts, "approved static XFA and script inventory " + filename);
    Check(report.Signatures.ApprovalSignatureCount == 0 && report.Signatures.ApprovedUsageRightsException && report.Signatures.RemovableUsageRightsKeys.SequenceEqual(new[] { "UR3" }), "narrow usage-rights exception " + filename);
    Check(report.InitialStructureSupported && report.CatalogJavaScriptCount == 3 && report.CatalogJavaScript.ApprovedRemovalRequired, "exact catalog scripts have removal-only exception " + filename);
    Check(report.CatalogJavaScript.ApprovedActions.Count == 3, "all catalog action identities checked " + filename);
    var parsed = ProfileParser.ParseProfile(File.ReadAllBytes(Path.Combine(root, "profiles", id + ".json")));
    Check(parsed.IsValid, "migrated profile parses " + filename);
    var bound = new TemplateBinder().Bind(input, parsed.Value!);
    Check(bound.Bound, "all reviewed targets statically bound " + filename);
    Check(bound.NotRunChecks.Contains("textFit") && bound.NotRunChecks.Contains("viewerAcceptance"), "binding never claims rendering acceptance");
    var wrong = parsed.Value! with { Template = parsed.Value!.Template with { Sha256Allowlist = [] } };
    Check(!new TemplateBinder().Bind(input, wrong).Bound, "empty allowlist cannot bind");
    var modifiedBytes = File.ReadAllBytes(Path.Combine(root, "fixtures", "appearance", "forms", filename)).Concat(Encoding.ASCII.GetBytes("\n% benign resave variation\n")).ToArray();
    var modified = TemplateSnapshot.FromBytes(modifiedBytes);
    Check(!new TemplateBinder().Bind(modified, parsed.Value!).Bound, "byte changed same structure cannot bind");
    using (var normalizedBytes = new MemoryStream())
    {
        using (var writable = ApprovedTemplateNormalizer.OpenForNormalization(input, normalizedBytes)) { }
        var normalized = TemplateSnapshot.FromBytes(normalizedBytes.ToArray());
        var normalizedReport = inspector.Inspect(normalized);
        Check(ApprovedTemplateNormalizer.HasNoActiveContent(normalized), "independently verify removed active content " + filename);
        Check(!normalizedReport.Xfa.Present && normalizedReport.CatalogJavaScriptCount == 0 && normalizedReport.Signatures.UsageRightsSignatureCount == 0, "XFA catalog JS and UR3 removed " + filename);
        Check(normalizedReport.TerminalFieldCount == count && normalizedReport.WidgetCount == count && normalizedReport.Pages == pages, "normalization preserves canonical inventory " + filename);
        Check(!new TemplateBinder().Bind(normalized, parsed.Value!).Bound, "normalized output cannot become approved input " + filename);
    }
    foreach (bool extra in new[] { false, true })
    {
        var changed = TemplateSnapshot.FromBytes(MutateCatalog(input, extra));
        var changedReport = inspector.Inspect(changed);
        Check(!changedReport.CatalogJavaScript.ApprovedRemovalRequired && Has(changedReport, "E_ACTIVE_CONTENT"), (extra ? "additional" : "changed") + " catalog action fails closed " + filename);
        Check(changedReport.CatalogJavaScriptCount == (extra ? 4 : 3), "changed catalog action inventory " + filename);
        bool rejected = false;
        try { using var sink = new MemoryStream(); using var rejectedPdf = ApprovedTemplateNormalizer.OpenForNormalization(changed, sink); }
        catch (TemplateInputException e) { rejected = e.Code == "E_NORMALIZATION_UNAPPROVED"; }
        Check(rejected, "normalization rejects altered original bytes " + filename);
    }
    Check(input.Sha256 == hash, "normalization leaves source snapshot immutable " + filename);
    actualReports.Add(new { filename, report.Sha256, report.TerminalFieldCount, report.WidgetCount, report.StructureFingerprintV1, report.Xfa, report.Signatures, report.CatalogJavaScriptCount, bound.Bound });
}
Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions, actualTemplates = actualReports }, new JsonSerializerOptions { WriteIndented = true }));
return;

static byte[] MutateCatalog(TemplateSnapshot source, bool add)
{
    using var output = new MemoryStream();
    using (var pdf = new PdfDocument(new PdfReader(source.OpenRead()), new PdfWriter(output)))
    {
        pdf.SetFlushUnusedObjects(false);
        var entries = pdf.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Names).GetAsDictionary(PdfName.JavaScript).GetAsArray(PdfName.Names);
        if (add)
        {
            entries.Add(new PdfString("synthetic-extra"));
            var action = new PdfDictionary(); action.Put(PdfName.S, PdfName.JavaScript); action.Put(PdfName.JS, new PdfString("synthetic-extra-no-execution")); entries.Add(action);
        }
        else entries.GetAsDictionary(1).Put(PdfName.JS, new PdfString("synthetic-changed-no-execution"));
    }
    return output.ToArray();
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "PdfForm.slnx"))) return dir.FullName;
    throw new InvalidOperationException("Project root is required.");
}

internal static class Fixture
{
    internal const string Sentinel = "SYNTHETIC_PRIVATE_SENTINEL_827491";
    internal static byte[] Make(bool swap = false, bool sensitive = false, string booleans = "", string catalog = "",
        bool orphan = false, bool noPageWidget = false, bool repeated = false, string fieldType = "Tx", int flags = 0,
        string extra = "<< >>", string? xfa = null, bool stateNames = false, bool noForm = false, bool noFormWidgets = false, bool pageAction = false, string? annotations = null)
    {
        string firstRect = swap ? "[150 10 250 30]" : "[10 10 110 30]";
        string secondRect = swap ? "[10 10 110 30]" : "[150 10 250 30]";
        string privateContent = sensitive ? $"/V ({Sentinel}) /DV ({Sentinel}) /TU ({Sentinel})" : "";
        string fieldName = sensitive ? Sentinel : "amountA";
        string normal = "<< /Length 0 >>\nstream\n\nendstream";
        string appearance = stateNames ? $"<< /N << /{Sentinel} 7 0 R >> >>" : "<< /N 7 0 R >>";
        string second = repeated ? $"<< /Subtype /Widget /Parent 6 0 R /Rect {secondRect} /AP << /N 7 0 R >> >>"
            : $"<< /Subtype /Widget /FT /Tx /T (amountB) /Parent 8 0 R /Rect {secondRect} /AP << /N 7 0 R >> >>";
        string first = repeated ? $"<< /FT /{fieldType} /T ({fieldName}) /Parent 8 0 R /Kids [11 0 R 9 0 R] {privateContent} >>"
            : $"<< /Subtype /Widget /T ({fieldName}) /Parent 8 0 R /Rect {firstRect} /AP {appearance} {privateContent} >>";
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R {(noForm ? "" : "/AcroForm 4 0 R")} {catalog} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [{(annotations ?? (noForm && !noFormWidgets ? "" : (noPageWidget ? "" : repeated ? "11 0 R" : "6 0 R") + " 9 0 R"))}] {(pageAction ? "/AA << /O 10 0 R >>" : "")} >>",
            $"<< /Fields [8 0 R] /DR << /Font << /Helvetica 5 0 R >> >> {booleans} {(xfa is null ? "" : "/XFA [(template) 12 0 R]")} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            first, normal,
            $"<< /T (group) /FT /{fieldType} /Ff {flags} /DA (/Helvetica 10 Tf) /Q 2 /MaxLen 10 /Kids [6 0 R {(orphan || repeated ? "" : "9 0 R")}] >>",
            second, extra,
            $"<< /Subtype /Widget /Parent 6 0 R /Rect {firstRect} /AP << /N 7 0 R >> >>",
            xfa is null ? "<< >>" : $"<< /Length {Encoding.UTF8.GetByteCount(xfa)} >>\nstream\n{xfa}\nendstream",
            sensitive ? $"<< /Title ({Sentinel}) /Author ({Sentinel}) >>" : "<< >>"
        };
        using var output = new MemoryStream();
        void Write(string text) { var data = Encoding.UTF8.GetBytes(text); output.Write(data); }
        Write("%PDF-1.7\n");
        var positions = new List<long> { 0 };
        for (int i = 0; i < objects.Count; ++i) { positions.Add(output.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        long xref = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long position in positions.Skip(1)) Write($"{position:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 13 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
}
