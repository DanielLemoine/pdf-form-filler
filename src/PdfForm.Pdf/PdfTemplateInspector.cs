using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using iText.Kernel.Pdf;
using PdfForm.Core;

namespace PdfForm.Pdf;

/// <summary>Read-only structural parsing. No form filling, JavaScript, external resources or XFA dataset export.</summary>
/// <remarks>
/// Traversal is limited to 30,000 combined AcroForm Fields, field Kids and page Annots entries per document.
/// Repeated references and malformed entries each consume budget; canonical/page aliases count in both traversals.
/// JavaScript name trees have separate 30,000-node-visit and 30,000-name-entry budgets, including empty nodes.
/// These are traversal limits, not a universal PDF-object ceiling; outline and engine limits remain separate.
/// </remarks>
public sealed class PdfTemplateInspector
{
    private const int MaximumNodes = 30_000;
    private readonly int maximumTraversalEntries;

    public PdfTemplateInspector() : this(MaximumNodes) { }

    // A reduced, never enlarged limit keeps synthetic boundary fixtures small; no caller-facing override exists.
    internal PdfTemplateInspector(int maximumTraversalEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTraversalEntries, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTraversalEntries, MaximumNodes);
        this.maximumTraversalEntries = maximumTraversalEntries;
    }

    private sealed class TraversalBudget(int maximum)
    {
        private int remaining = maximum;
        public void Consume(int count)
        {
            // Subtraction only after the comparison avoids cumulative-count overflow.
            if (count < 0 || count > remaining) throw new InspectionLimitException();
            remaining -= count;
        }
    }

    private static PdfName N(string value) => new(value);
    private static readonly Regex FontPattern = new(@"/([^\s/<>\[\]()]+)\s+([-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))\s+Tf\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> SafeFontNames = new(StringComparer.Ordinal)
        { "TimesNewRomanPSMT", "TimesLTStd-Roman", "Helvetica", "Times-Roman", "ZapfDingbats" };
    private sealed class FieldNode(PdfDictionary dictionary, string name, string id)
    {
        public PdfDictionary Dictionary { get; } = dictionary;
        public string Name { get; } = name;
        public string Id { get; } = id;
        public List<PdfDictionary> Widgets { get; } = [];
    }

    public TemplateInspection Inspect(TemplateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            using var input = snapshot.OpenRead();
            var memory = new BoundedMemoryHandler();
            memory.SetMaxSizeOfSingleDecompressedPdfStream(10 * 1024 * 1024);
            memory.SetMaxSizeOfDecompressedPdfStreamsSum(50 * 1024 * 1024);
            memory.SetMaxNumberOfElementsInXrefStructure(100_000);
            using var reader = new PdfReader(input, new ReaderProperties().SetMemoryLimitsAwareHandler(memory));
            using var pdf = new PdfDocument(reader);
            if (reader.IsEncrypted()) return Failure(snapshot, "E_PDF_ENCRYPTED", true);
            return InspectDocument(snapshot, pdf, maximumTraversalEntries);
        }
        catch (iText.Kernel.Exceptions.BadPasswordException) { return Failure(snapshot, "E_PDF_ENCRYPTED", true); }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // PDF parser exceptions can contain arbitrary strings from the PDF. Never echo them.
            return Failure(snapshot, e is InspectionLimitException or iText.Kernel.Exceptions.MemoryLimitsAwareException ? "E_PDF_LIMIT" : "E_PDF_PARSE");
        }
    }
    private sealed class BoundedMemoryHandler : MemoryLimitsAwareHandler
    {
        [Obsolete("Compatibility override for the pinned engine; all decoded streams require accounting.")]
        public override bool IsMemoryLimitsAwarenessRequiredOnDecompression(PdfArray filters) => true;
    }
    private sealed class InspectionLimitException : Exception { }
    private static TemplateInspection Failure(TemplateSnapshot s, string code, bool encrypted = false) => new()
    {
        Sha256 = s.Sha256, Bytes = s.ByteCount, Encrypted = encrypted,
        Findings = [new(code, "binding", "PDF cannot be inspected within the supported boundary.")]
    };

    private static TemplateInspection InspectDocument(TemplateSnapshot snapshot, PdfDocument pdf, int maximumTraversalEntries)
    {
        var findings = new List<Finding>();
        void Error(string code, string message) => findings.Add(new(code, "binding", message));
        var approved = ApprovedTemplates.Find(snapshot.Sha256);
        bool trustedNames = approved is not null;
        int pageCount = pdf.GetNumberOfPages();
        if (pageCount > Limits.MaxTemplatePages) throw new InspectionLimitException();
        var catalog = pdf.GetCatalog().GetPdfObject();
        var af = catalog.GetAsDictionary(N("AcroForm"));
        var needAppearances = Boolean(af, "NeedAppearances");
        var needsRendering = Boolean(catalog, "NeedsRendering");
        if (needAppearances.State == "invalid" || needsRendering.State == "invalid")
            Error("E_PDF_BOOLEAN", "A structural PDF Boolean has an invalid type.");
        var fields = new List<FieldNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var canonicalWidgets = new Dictionary<string, FieldNode>(StringComparer.Ordinal);
        var pageWidgets = new Dictionary<string, (PdfDictionary Widget, int Page)>(StringComparer.Ordinal);
        var fieldAndAnnotationBudget = new TraversalBudget(maximumTraversalEntries);
        int actions = HasAction(catalog) + HasAction(af);
        actions += CountOutlineActions(catalog.GetAsDictionary(N("Outlines"))?.GetAsDictionary(N("First")), new HashSet<string>(), 0);
        if (catalog.ContainsKey(N("OpenAction"))) actions++;
        var names = catalog.GetAsDictionary(N("Names"));
        var catalogJavaScript = ClassifyCatalogJavaScript(names, approved, maximumTraversalEntries);
        int catalogScripts = catalogJavaScript.Count;
        if (!catalogJavaScript.ApprovedRemovalRequired) actions += catalogScripts;
        else findings.Add(new("W_CATALOG_JAVASCRIPT_REMOVAL", "binding", "Reviewed viewer-version catalog scripts must be removed without execution before output.", Severity: "warning"));
        bool embedded = names?.ContainsKey(N("EmbeddedFiles")) == true || catalog.ContainsKey(N("AF"));
        if (af is null) Error("E_NO_ACROFORM", "Document has no canonical AcroForm.");
        var roots = af?.GetAsArray(N("Fields"));
        if (roots is not null)
        {
            fieldAndAnnotationBudget.Consume(roots.Size());
            for (int i = 0; i < roots.Size(); ++i)
                Walk(roots.GetAsDictionary(i), "", 0, null);
        }

        void Walk(PdfDictionary? node, string prefix, int depth, PdfDictionary? expectedParent)
        {
            if (node is null) { Error("E_FIELD_TREE", "Canonical field tree contains a non-dictionary entry."); return; }
            if (depth > 64) throw new InspectionLimitException();
            if (!seen.Add(ObjectId(node))) { Error("E_FIELD_CYCLE", "Canonical field tree repeats or cycles an object."); return; }
            if (expectedParent is not null && (node.GetAsDictionary(N("Parent")) is not { } parent || ObjectId(parent) != ObjectId(expectedParent)))
                Error("E_FIELD_PARENT", "A child field does not link to its canonical parent.");
            actions += HasAction(node);
            var part = node.GetAsString(N("T"))?.ToUnicodeString();
            if (part?.Length > 1024) throw new InspectionLimitException();
            string name = part is null ? prefix : prefix.Length == 0 ? part : prefix + "." + part;
            var kids = node.GetAsArray(N("Kids"));
            var childFields = new List<PdfDictionary>();
            var widgets = new List<PdfDictionary>();
            if (kids is not null)
            {
                // Reserve every child before resolving dictionaries or materializing either child list.
                fieldAndAnnotationBudget.Consume(kids.Size());
                for (int i = 0; i < kids.Size(); ++i)
                {
                    var child = kids.GetAsDictionary(i);
                    if (child is null) { Error("E_FIELD_TREE", "Field child is not a dictionary."); continue; }
                    if (IsWidget(child) && !child.ContainsKey(N("T"))) widgets.Add(child);
                    else childFields.Add(child);
                }
            }
            if (childFields.Count > 0)
            {
                if (widgets.Count > 0 || IsWidget(node)) Error("E_FIELD_TREE", "A container mixes terminal widgets and child fields.");
                foreach (var child in childFields) Walk(child, name, depth + 1, node);
                return;
            }
            if (Inherited(node, "FT", af) is null)
            { Error("E_FIELD_TYPE", "A terminal field has no effective field type."); }
            if (fields.Count >= Limits.MaxKeysOrTargets) throw new InspectionLimitException();
            var field = new FieldNode(node, name, $"field-{fields.Count + 1:D4}");
            if (IsWidget(node)) field.Widgets.Add(node);
            field.Widgets.AddRange(widgets);
            foreach (var widget in field.Widgets)
            {
                if (!canonicalWidgets.TryAdd(ObjectId(widget), field)) Error("E_WIDGET_DUPLICATE", "A widget belongs to multiple canonical fields.");
                if (!ReferenceEquals(node, widget))
                {
                    actions += HasAction(widget);
                    if (widget.GetAsDictionary(N("Parent")) is not { } widgetParent || ObjectId(widgetParent) != ObjectId(node))
                        Error("E_WIDGET_PARENT", "A widget does not link to its owning canonical terminal field.");
                }
            }
            fields.Add(field);
        }
        for (int p = 1; p <= pageCount; ++p)
        {
            var page = pdf.GetPage(p).GetPdfObject(); actions += HasAction(page);
            var annots = page.GetAsArray(N("Annots"));
            if (annots is null) continue;
            fieldAndAnnotationBudget.Consume(annots.Size());
            for (int i = 0; i < annots.Size(); ++i)
            {
                var annot = annots.GetAsDictionary(i);
                if (annot is null) continue;
                if (IsWidget(annot))
                {
                    if (!pageWidgets.TryAdd(ObjectId(annot), (annot, p))) Error("E_WIDGET_DUPLICATE", "A widget appears more than once in page annotations.");
                    if (!canonicalWidgets.ContainsKey(ObjectId(annot))) actions += HasAction(annot);
                }
                else
                {
                    actions += HasAction(annot);
                    string? subtype = Name(annot, "Subtype");
                    if (subtype == "FileAttachment") embedded = true;
                    if (subtype is "RichMedia" or "Screen" or "Movie" or "Sound" or "3D") actions++;
                }
            }
        }
        if (fields.Count == 0) Error("E_NO_FIELDS", "Document has no canonical terminal form fields.");
        var reports = new List<FieldInspection>();
        var named = new Dictionary<string, FieldInspection>(StringComparer.Ordinal);
        var fingerprintRows = new List<(string Name, string Type, int? Page, int? MaxLen, bool Comb, string[] On)>();
        int orphanId = 0;
        foreach (var field in fields)
        {
            ValidateAttributes(field.Dictionary);
            var attributes = Attributes(field.Dictionary, af, trustedNames);
            string type = FieldType(NameOf(Inherited(field.Dictionary, "FT", af)), Int(Inherited(field.Dictionary, "Ff", af)) ?? 0);
            var widgets = new List<WidgetInspection>();
            var allOn = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var widget in field.Widgets)
            {
                ValidateAttributes(widget);
                var widgetId = ObjectId(widget);
                var page = pageWidgets.TryGetValue(widgetId, out var pw) ? (int?)pw.Page : null;
                var on = OnStates(widget); allOn.UnionWith(on);
                widgets.Add(Widget(widget, field.Id + $"-widget-{widgets.Count + 1:D2}", page, af, trustedNames,
                    page.HasValue ? "canonicalAndPage" : "canonicalWithoutPage"));
                if (!page.HasValue) Error("E_WIDGET_NOT_ON_PAGE", "A canonical widget is not attached to any page.");
            }
            if (widgets.Count == 0) Error("E_FIELD_NO_WIDGET", "A canonical field has no widget.");
            if (widgets.Any(w => !w.GeometryValid)) Error("E_WIDGET_GEOMETRY", "A widget has invalid geometry.");
            string description = Inherited(field.Dictionary, "TU", af) is not null ? "tooltip" : "none";
            // XFA descriptions are suppressed, but known reviewed templates carry XFA accessibility descriptions.
            if (description == "none" && approved is not null) description = "xfa";
            var report = new FieldInspection(field.Id, trustedNames ? field.Name : null, type, attributes,
                widgets.AsReadOnly(), trustedNames ? allOn.ToArray() : [], allOn.Count, description);
            reports.Add(report);
            if (field.Name.Length == 0 || !named.TryAdd(field.Name, report)) Error("E_FIELD_NAME", "Canonical field names are missing or duplicated.");
            fingerprintRows.Add((field.Name, type, widgets.FirstOrDefault()?.Page, attributes.MaxLength, attributes.Flags.Comb, allOn.ToArray()));
        }
        void ValidateAttributes(PdfDictionary node)
        {
            foreach (string key in new[] { "Ff", "Q", "MaxLen" })
            {
                var value = Inherited(node, key, af);
                if (value is not null && (Int(value) is not { } integer || integer < 0 || (key == "Q" && integer > 2) || (key == "MaxLen" && integer == 0)))
                    Error("E_FIELD_ATTRIBUTE", "An effective numeric field attribute has an invalid type or range.");
            }
            if (Inherited(node, "DA", af) is { } da && da is not PdfString)
                Error("E_FIELD_ATTRIBUTE", "An effective default appearance has an invalid type.");
        }
        var orphans = new List<WidgetInspection>();
        foreach (var (id, pw) in pageWidgets)
            if (!canonicalWidgets.ContainsKey(id)) orphans.Add(Widget(pw.Widget, $"orphan-{++orphanId:D4}", pw.Page, af, false, "pageWithoutCanonicalField"));
        if (orphans.Count > 0) Error("E_ORPHAN_WIDGET", "Page widgets are absent from the canonical terminal-field tree.");
        var xfa = InspectXfa(af, approved, needsRendering, findings);
        var signatures = InspectSignatures(pdf, af, catalog, fields, approved);
        if (signatures.ApprovalSignatureCount > 0) Error("E_APPROVAL_SIGNATURE", "Existing approval signatures are unsupported.");
        if (signatures.CertificationPresent) Error("E_CERTIFICATION", "Certification or DocMDP permissions are unsupported.");
        if (signatures.UnknownPermissionEntry) Error("E_PERMISSION_UNKNOWN", "Unclassified document permission entries are unsupported.");
        if (signatures.UsageRightsSignatureCount > 0 && !signatures.ApprovedUsageRightsException)
            Error("E_USAGE_RIGHTS_UNAPPROVED", "Reader usage-rights exceptions require an exact reviewed template.");
        if (actions > 0) Error("E_ACTIVE_CONTENT", "Catalog, page, AcroForm or annotation actions are unsupported.");
        if (embedded) Error("E_EMBEDDED_CONTENT", "Embedded-file content is outside initial fill support.");
        if (xfa.Present && xfa.Kind != "staticHybridApproved") Error("E_XFA_UNSUPPORTED", "XFA is dynamic, unknown or outside the exact approved static templates.");
        if (reports.Any(f => f.Type is not ("text" or "checkbox") || f.Widgets.Count != 1 || f.Effective.Flags.RichText || f.Effective.Flags.Password))
            Error("E_FIELD_CAPABILITY", "Initial fill supports only single-widget plain text and checkbox fields.");
        if (reports.Any(f => f.Effective.Flags.ReadOnly || f.Effective.Flags.NoExport))
            Error("E_FIELD_POLICY", "Read-only or no-export fields require an explicit supported profile policy.");
        if (reports.Any(f => f.Type == "text" && !f.Effective.Font.ResourcePresent))
            Error("E_FONT_RESOURCE", "An effective default font resource is missing.");
        if (reports.Any(f => f.Type == "text" && !f.Effective.Font.Embedded))
            findings.Add(new("W_FONT_NOT_EMBEDDED", "binding", "An effective font lacks an embedded program; portable appearance qualification remains required.", Severity: "warning"));
        if (reports.Any(f => f.Type == "checkbox" && f.Widgets.Any(w => !w.HasNormalOffAppearance)))
            findings.Add(new("W_NORMAL_OFF_MISSING", "binding", "Checkboxes require reviewed normal Off appearance generation before filling.", Severity: "warning"));
        var capabilities = Capabilities(reports, xfa, signatures, actions, embedded).ToList();
        capabilities.Add(new("active.catalogJavaScript", catalogScripts == 0 ? "absent" : catalogJavaScript.ApprovedRemovalRequired ? "approved_removal_only" : "unsupported", catalogScripts, "EXACT_TEMPLATE_SCRIPT_EXCEPTION_ONLY"));
        return new TemplateInspection
        {
            Sha256 = snapshot.Sha256, Bytes = snapshot.ByteCount, Pages = pageCount, HasAcroForm = af is not null, CatalogJavaScript = catalogJavaScript,
            ExactApprovedTemplate = approved is not null, ApprovedTemplateId = approved?.Id, ApprovedTitle = approved?.Title,
            ActualTitle = pdf.GetDocumentInfo().GetTitle(), StructureFingerprintV1 = Fingerprint(fingerprintRows),
            NeedAppearances = needAppearances, NeedsRendering = needsRendering, Xfa = xfa, Signatures = signatures,
            Fields = reports.AsReadOnly(), OrphanWidgets = orphans.AsReadOnly(), NamedFields = named,
            Capabilities = capabilities, Findings = findings.AsReadOnly(), InitialStructureSupported = findings.All(f => f.Severity != "error")
        };
    }

    private static CatalogJavaScriptInspection ClassifyCatalogJavaScript(PdfDictionary? names, ApprovedTemplate? approved, int maximumTraversalEntries)
    {
        if (names?.ContainsKey(N("JavaScript")) != true) return new(0, false, []);
        var nodeBudget = new TraversalBudget(maximumTraversalEntries);
        var entryBudget = new TraversalBudget(maximumTraversalEntries);
        nodeBudget.Consume(1);
        var tree = names.GetAsDictionary(N("JavaScript"));
        int count = Math.Max(1, CountNameEntries(tree, new HashSet<string>(), 0, nodeBudget, entryBudget));
        if (approved is null || tree is null || tree.Size() != 1 || tree.GetAsArray(N("Names")) is not { } entries || entries.Size() != 6)
            return new(count, false, []);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["!ADBE::0100_VersChkStrings"] = "812f8446cf1b6187844f5e7116cf921ee6da6407961ebe4e7ee52c843d7af754",
            ["!ADBE::0100_VersChkVars"] = "e985b5df65c8c3cf732a9074b575fbc594c1c7f0bccc0994182ec7e5c0f7308a",
            ["!ADBE::0200_VersChkCode_XFACheck"] = "529357503ec67b623d2a12816cdeea62bd639f2b4ff4e568b01c96cc3f5bfc6f"
        };
        var identities = new List<CatalogActionIdentity>();
        for (int i = 0; i < entries.Size(); i += 2)
        {
            string? name = entries.GetAsString(i)?.ToUnicodeString();
            var action = entries.GetAsDictionary(i + 1);
            if (name is null || !expected.Remove(name, out var expectedHash) || action is null || action.Size() != 2
                || Name(action, "S") != "JavaScript" || action.GetAsStream(N("JS")) is not { } script)
                return new(count, false, []);
            byte[] bytes = script.GetBytes();
            if (bytes.Length > 16 * 1024) return new(count, false, []);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (hash != expectedHash) return new(count, false, []);
            identities.Add(new(name, "JavaScript", hash));
        }
        return new(count, expected.Count == 0, identities.AsReadOnly());
    }
    private static int CountOutlineActions(PdfDictionary? node, HashSet<string> visited, int depth)
    {
        if (depth > 64) throw new InspectionLimitException();
        int count = 0;
        while (node is not null)
        {
            if (visited.Count >= MaximumNodes || !visited.Add(ObjectId(node))) throw new InspectionLimitException();
            count += HasAction(node);
            count += CountOutlineActions(node.GetAsDictionary(N("First")), visited, depth + 1);
            node = node.GetAsDictionary(N("Next"));
        }
        return count;
    }
    private static int CountNameEntries(PdfDictionary? node, HashSet<string> visited, int depth,
        TraversalBudget nodeBudget, TraversalBudget entryBudget)
    {
        if (depth > 64) throw new InspectionLimitException();
        if (node is null)
        {
            entryBudget.Consume(1);
            return 1; // Malformed action tree is not silently treated as empty.
        }
        if (!visited.Add(ObjectId(node))) throw new InspectionLimitException();
        var names = node.GetAsArray(N("Names"));
        int count = names is null ? 0 : names.Size() / 2 + names.Size() % 2;
        entryBudget.Consume(count);
        var kids = node.GetAsArray(N("Kids"));
        if (kids is not null)
        {
            // Charge empty, malformed and repeated child visits before resolving or traversing them.
            nodeBudget.Consume(kids.Size());
            for (int i = 0; i < kids.Size(); ++i)
                count += CountNameEntries(kids.GetAsDictionary(i), visited, depth + 1, nodeBudget, entryBudget);
        }
        return count;
    }
    private static int HasAction(PdfDictionary? dictionary) => dictionary is null ? 0 :
        (dictionary.ContainsKey(N("A")) ? 1 : 0) + (dictionary.ContainsKey(N("AA")) ? 1 : 0);
    private static string ObjectId(PdfDictionary d)
    {
        var r = d.GetIndirectReference();
        return r is null ? "direct:" + RuntimeHelpers.GetHashCode(d) : r.GetObjNumber() + ":" + r.GetGenNumber();
    }
    private static bool IsWidget(PdfDictionary d) => Name(d, "Subtype") == "Widget";
    private static string? Name(PdfDictionary d, string key) => d.GetAsName(N(key))?.GetValue();
    private static string? NameOf(PdfObject? value) => (value as PdfName)?.GetValue();
    private static int? Int(PdfObject? value) => value is PdfNumber n && double.IsFinite(n.DoubleValue()) && n.DoubleValue() == n.IntValue() ? n.IntValue() : null;
    private static PdfObject? Inherited(PdfDictionary? node, string key, PdfDictionary? af)
    {
        var seen = new HashSet<string>();
        for (int depth = 0; node is not null; ++depth)
        {
            if (depth > 64 || !seen.Add(ObjectId(node))) throw new InspectionLimitException();
            if (node.ContainsKey(N(key))) return node.Get(N(key), true);
            node = node.GetAsDictionary(N("Parent"));
        }
        return key is "DA" or "Q" or "DR" ? af?.Get(N(key), true) : null;
    }
    private static PdfBooleanInspection Boolean(PdfDictionary? d, string key)
    {
        if (d?.ContainsKey(N(key)) != true) return new("missing", null);
        return d.GetAsBoolean(N(key)) is { } b ? new(b.GetValue() ? "true" : "false", b.GetValue()) : new("invalid", null);
    }
    private static string FieldType(string? ft, int flags) => ft switch
    {
        "Tx" => "text", "Btn" when (flags & (1 << 16)) != 0 => "pushbutton",
        "Btn" when (flags & (1 << 15)) != 0 => "radio", "Btn" => "checkbox",
        "Ch" when (flags & (1 << 17)) != 0 => "combo", "Ch" => "list", "Sig" => "signature", _ => "unknown"
    };
    private static EffectiveAttributes Attributes(PdfDictionary node, PdfDictionary? af, bool trusted)
    {
        int ff = Int(Inherited(node, "Ff", af)) ?? 0;
        bool text = NameOf(Inherited(node, "FT", af)) == "Tx";
        var flags = new FieldFlags((ff & 1) != 0, (ff & 2) != 0, (ff & 4) != 0,
            text && (ff & (1 << 12)) != 0, text && (ff & (1 << 13)) != 0, text && (ff & (1 << 24)) != 0,
            text && ((ff & (1 << 25)) != 0 || Inherited(node, "RV", af) is not null));
        var da = (Inherited(node, "DA", af) as PdfString)?.ToUnicodeString();
        string? fontName = null; double? size = null;
        if (da is { Length: > 8192 }) throw new InspectionLimitException();
        var match = FontPattern.Match(da ?? "");
        if (match.Success && double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fs) && double.IsFinite(fs))
        { fontName = match.Groups[1].Value; size = fs; }
        var resources = Inherited(node, "DR", af) as PdfDictionary;
        var font = fontName is null ? null : resources?.GetAsDictionary(N("Font"))?.GetAsDictionary(N(fontName));
        var desc = font?.GetAsDictionary(N("FontDescriptor"));
        bool embedded = desc is not null && (desc.ContainsKey(N("FontFile")) || desc.ContainsKey(N("FontFile2")) || desc.ContainsKey(N("FontFile3")));
        string? subtype = font is null ? null : Name(font, "Subtype");
        if (subtype is not ("Type1" or "TrueType" or "Type0" or "Type3" or "MMType1")) subtype = font is null ? null : "unknown";
        string source = node.ContainsKey(N("DA")) ? "fieldOrWidget" : af?.ContainsKey(N("DA")) == true && !HasParentAttribute(node, "DA") ? "acroForm" : "ancestor";
        string? safeFont = fontName is not null && (trusted || SafeFontNames.Contains(fontName)) ? fontName : null;
        return new(flags, Int(Inherited(node, "MaxLen", af)), (Int(Inherited(node, "Q", af)) ?? 0) switch { 0 => "left", 1 => "center", 2 => "right", _ => "invalid" },
            new(safeFont, size, subtype, font is not null, embedded, source));
    }
    private static bool HasParentAttribute(PdfDictionary node, string key) => Inherited(node.GetAsDictionary(N("Parent")), key, null) is not null;
    private static string[] OnStates(PdfDictionary widget)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var ap = widget.GetAsDictionary(N("AP"));
        foreach (string kind in new[] { "N", "D" })
        {
            var states = ap?.Get(N(kind), true);
            if (states is PdfDictionary dict and not PdfStream)
                foreach (var name in dict.KeySet()) if (name.GetValue() != "Off") result.Add(name.GetValue());
        }
        return result.ToArray();
    }
    private static WidgetInspection Widget(PdfDictionary widget, string id, int? page, PdfDictionary? af, bool trusted, string linkage)
    {
        var rect = widget.GetAsArray(N("Rect"));
        var coordinates = new List<double>();
        if (rect?.Size() == 4)
            for (int i = 0; i < 4; ++i) if (rect.GetAsNumber(i) is { } number && double.IsFinite(number.DoubleValue())) coordinates.Add(number.DoubleValue());
        bool valid = coordinates.Count == 4 && coordinates[2] > coordinates[0] && coordinates[3] > coordinates[1];
        var normal = widget.GetAsDictionary(N("AP"))?.Get(N("N"), true);
        bool off = normal is PdfDictionary dict and not PdfStream && dict.GetAsStream(N("Off")) is not null;
        var states = OnStates(widget);
        return new(id, page, coordinates.AsReadOnly(), valid, Attributes(widget, af, trusted), Int(widget.Get(N("F"), true)) ?? 0,
            Int(widget.GetAsDictionary(N("MK"))?.Get(N("R"), true)) ?? 0, normal is not null, off, trusted ? states : [], states.Length, linkage);
    }
    private static string Fingerprint(IEnumerable<(string Name, string Type, int? Page, int? MaxLen, bool Comb, string[] On)> fields)
    {
        // Exact Rev.1 algorithm: deliberately excludes rectangles, static labels, metadata and byte identity.
        string lines = string.Join("\n", fields.OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => string.Join("\t", f.Name, f.Type,
            f.Page?.ToString(CultureInfo.InvariantCulture) ?? "None", f.MaxLen is > 0 ? f.MaxLen.Value.ToString(CultureInfo.InvariantCulture) : "", f.Comb ? "1" : "0", string.Join(",", f.On))));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lines))).ToLowerInvariant();
    }
    private static XfaInspection InspectXfa(PdfDictionary? af, ApprovedTemplate? approved,
        PdfBooleanInspection needsRendering, List<Finding> findings)
    {
        var xfa = af?.Get(N("XFA"), true);
        if (xfa is null) return new(false, "none", 0, false, false, false);
        const int maxXml = 10 * 1024 * 1024;
        int scripts = 0; bool forbidden = false, dynamic = needsRendering.Value == true;
        bool validTemplateNamespace = false, knownScriptBodies = true;
        string? templateHash = null;
        try
        {
            if (xfa is PdfArray array)
            {
                if (array.Size() > 256 || array.Size() % 2 != 0) throw new XmlException();
                bool templateSeen = false, configSeen = false;
                for (int i = 0; i < array.Size(); i += 2)
                {
                    string? packet = array.GetAsString(i)?.ToUnicodeString();
                    // Datasets, metadata, embedded payloads and other packets are never decoded or exported.
                    if (packet is not ("config" or "template")) continue;
                    if ((packet == "template" && templateSeen) || (packet == "config" && configSeen)) throw new XmlException();
                    if (packet == "template") templateSeen = true; else configSeen = true;
                    var stream = array.GetAsStream(i + 1) ?? throw new XmlException();
                    var bytes = stream.GetBytes(); if (bytes.Length > maxXml) throw new InspectionLimitException();
                    if (packet == "template") templateHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    ReadXml(bytes, packet == "template");
                }
                if (!templateSeen) throw new XmlException();
            }
            else if (xfa is PdfStream stream)
            {
                var bytes = stream.GetBytes(); if (bytes.Length > maxXml) throw new InspectionLimitException();
                ReadXml(bytes, true); // XDP stream remains unknown unless admitted through a future reviewed registry record.
            }
            else throw new XmlException();
            bool known = approved is not null && validTemplateNamespace && forbidden && !dynamic && knownScriptBodies
                && templateHash == approved.XfaTemplateSha256 && scripts == approved.Scripts;
            return new(true, known ? "staticHybridApproved" : dynamic ? "dynamic" : "unknown", scripts, true, forbidden, known && scripts > 0);
        }
        catch (Exception e) when (e is XmlException or InspectionLimitException or InvalidOperationException)
        {
            findings.Add(new("E_XFA_XML", "binding", "XFA XML is malformed or exceeds the safe parser boundary."));
            return new(true, "unknown", null, false, false, false);
        }
        void ReadXml(byte[] bytes, bool template)
        {
            using var input = new MemoryStream(bytes, false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = maxXml, MaxCharactersFromEntities = 0,
                IgnoreComments = true, IgnoreProcessingInstructions = true
            });
            bool advance = true;
            while (true)
            {
                if (advance && !reader.Read()) break;
                advance = true;
                if (reader.Depth > 64) throw new InspectionLimitException();
                if (reader.NodeType != XmlNodeType.Element) continue;
                string local = reader.LocalName, ns = reader.NamespaceURI;
                if (local == "template" && ns.StartsWith("http://www.xfa.org/schema/xfa-template/", StringComparison.Ordinal)) validTemplateNamespace = true;
                if (local == "dynamicRender" && ns == "http://www.xfa.org/schema/xci/3.0/")
                {
                    string value = reader.ReadElementContentAsString();
                    advance = false;
                    if (value.Trim() == "forbidden") forbidden = true;
                    else dynamic = true;
                }
                else if (template && local == "script" && ns.StartsWith("http://www.xfa.org/schema/xfa-template/", StringComparison.Ordinal))
                {
                    ++scripts;
                    string body = reader.ReadElementContentAsString();
                    advance = false;
                    string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
                    knownScriptBodies &= digest == "6236f4151b6583e75e1fec2099803924c85f4149958cffd010759dfa61b7f459";
                }
            }
        }
    }
    private static SignatureInspection InspectSignatures(PdfDocument pdf, PdfDictionary? af, PdfDictionary catalog,
        IReadOnlyList<FieldNode> fields, ApprovedTemplate? approved)
    {
        var perms = catalog.GetAsDictionary(N("Perms"));
        bool certification = perms?.ContainsKey(N("DocMDP")) == true;
        bool unknown = catalog.ContainsKey(N("Perms")) && perms is null;
        var rights = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>();
        if (perms is not null)
            foreach (var key in perms.KeySet())
            {
                string name = key.GetValue();
                if (name == "DocMDP") continue;
                var signature = perms.GetAsDictionary(key);
                bool recognized = name is "UR" or "UR3" && signature is not null
                    && Name(signature, "Type") == "Sig" && Name(signature, "SubFilter") == "adbe.pkcs7.detached"
                    && signature.ContainsKey(N("ByteRange")) && signature.ContainsKey(N("Contents"));
                var references = signature?.GetAsArray(N("Reference"));
                recognized &= references is not null && references.Size() > 0 && references.Size() <= 16;
                if (references is not null)
                    for (int i = 0; i < references.Size(); ++i)
                    {
                        var reference = references.GetAsDictionary(i);
                        var parameters = reference?.GetAsDictionary(N("TransformParams"));
                        recognized &= reference is not null && Name(reference, "TransformMethod") is "UR" or "UR3"
                            && parameters is not null && Name(parameters, "Type") == "TransformParams";
                        if (reference is not null && Name(reference, "TransformMethod") == "DocMDP") certification = true;
                    }
                if (recognized) { rights.Add(ObjectId(signature!)); keys.Add(name); }
                else unknown = true;
            }
        var approval = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
            if (NameOf(Inherited(field.Dictionary, "FT", af)) == "Sig" && Inherited(field.Dictionary, "V", af) is PdfDictionary sig
                && sig.ContainsKey(N("Contents")) && sig.ContainsKey(N("ByteRange"))
                && !rights.Contains(ObjectId(sig))) approval.Add(ObjectId(sig));
        int objectCount = pdf.GetNumberOfPdfObjects();
        if (objectCount > 100_000) throw new InspectionLimitException();
        for (int i = 1; i < objectCount; ++i)
            if (pdf.GetPdfObject(i) is PdfDictionary d && Name(d, "Type") == "Sig" && d.ContainsKey(N("Contents")) && d.ContainsKey(N("ByteRange")))
            {
                if (!rights.Contains(ObjectId(d))) approval.Add(ObjectId(d));
                var references = d.GetAsArray(N("Reference"));
                if (references is not null)
                    for (int j = 0; j < references.Size(); ++j)
                        if (references.GetAsDictionary(j) is { } r && Name(r, "TransformMethod") == "DocMDP") certification = true;
            }
        bool exception = approved is not null && rights.Count > 0 && !certification && !unknown && approval.Count == 0;
        return new(Int(af?.Get(N("SigFlags"), true)) ?? 0, approval.Count, certification, rights.Count,
            exception, exception ? keys.AsReadOnly() : [], unknown);
    }
    private static IReadOnlyList<CapabilityInspection> Capabilities(IReadOnlyList<FieldInspection> fields,
        XfaInspection xfa, SignatureInspection signatures, int actions, bool embedded)
    {
        var result = new List<CapabilityInspection>();
        void Add(string name, int count, string status, string reason) => result.Add(new(name, count == 0 ? "absent" : status, count, reason));
        Add("text.singleWidget", fields.Count(f => f.Type == "text" && f.Widgets.Count == 1 && !f.Effective.Flags.RichText), "structurally_supported", "APPEARANCE_QUALIFICATION_REQUIRED");
        Add("checkbox.singleWidget", fields.Count(f => f.Type == "checkbox" && f.Widgets.Count == 1), "structurally_supported", "OFF_APPEARANCE_QUALIFICATION_REQUIRED");
        Add("field.repeatedWidget", fields.Count(f => f.Widgets.Count > 1), "unsupported", "E_FIELD_CAPABILITY");
        Add("field.radio", fields.Count(f => f.Type == "radio"), "unsupported", "E_FIELD_CAPABILITY");
        Add("field.choice", fields.Count(f => f.Type is "combo" or "list"), "unsupported", "E_FIELD_CAPABILITY");
        Add("field.pushbutton", fields.Count(f => f.Type == "pushbutton"), "unsupported", "E_FIELD_CAPABILITY");
        Add("field.signature", fields.Count(f => f.Type == "signature"), "unsupported", "E_FIELD_CAPABILITY");
        Add("field.richText", fields.Count(f => f.Effective.Flags.RichText), "unsupported", "E_FIELD_CAPABILITY");
        Add("xfa.staticHybrid", xfa.Present ? 1 : 0, xfa.Kind == "staticHybridApproved" ? "approved_removal_only" : "unsupported", "E_XFA_UNSUPPORTED");
        Add("xfa.script", xfa.ScriptCount ?? 0, xfa.KnownScriptRemovalException ? "approved_removal_only" : "unsupported", "NEVER_EXECUTED");
        Add("signature.readerRights", signatures.UsageRightsSignatureCount, signatures.ApprovedUsageRightsException ? "approved_removal_only" : "unsupported", "EXACT_TEMPLATE_EXCEPTION_ONLY");
        Add("signature.approval", signatures.ApprovalSignatureCount, "unsupported", "E_APPROVAL_SIGNATURE");
        Add("signature.certification", signatures.CertificationPresent ? 1 : 0, "unsupported", "E_CERTIFICATION");
        Add("active.action", actions, "unsupported", "E_ACTIVE_CONTENT");
        Add("embedded.file", embedded ? 1 : 0, "unsupported", "E_EMBEDDED_CONTENT");
        return result.AsReadOnly();
    }
}
