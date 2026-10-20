using System.Text;
using System.Text.Json;
using iText.Forms;
using iText.Kernel.Pdf;
using PdfForm.Pdf;

/// <summary>Focused authored fixtures; register Run(Check) in the existing PDF test entry point.</summary>
internal static class ConfigurationInspectionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var inspector = new PdfTemplateInspector();
        StructuralConfigurationDraft Discover(byte[] bytes) => inspector.DiscoverConfiguration(TemplateSnapshot.FromBytes(bytes));
        const string sentinel = "PRIVATE_CURRENT_DEFAULT_DATASET_SENTINEL_71492";

        var first = Discover(Fixture.Make());
        var moved = Discover(Fixture.Make(swap: true));
        check(first.Compatibility.Status == "configuration_needed" && first.Compatibility.ExitCode == 1,
            "blank structural discovery requires a configuration rather than claiming approval");
        check(first.Targets.Count == 2 && first.Targets.All(t => t.Type == "text" && t.Target.StartsWith("group.", StringComparison.Ordinal)),
            "private draft maps actual canonical text fields without guessing money meaning");
        check(first.Targets.Select(t => t.Key).SequenceEqual(moved.Targets.Select(t => t.Key)),
            "generic field keys survive widget geometry changes");
        check(first.TemplateSha256 != moved.TemplateSha256 && first.Targets[0].Rectangle[0] != moved.Targets[0].Rectangle[0],
            "stable keys never hide changed template identity or geometry");
        check(first.Targets.All(t => t.Key == PdfTemplateInspector.ConfigurationFieldKey(t.Target)),
            "draft keys bind exact full canonical field names");
        check(first.Compatibility.Fields.All(f => f.Id.StartsWith("field_", StringComparison.Ordinal))
            && !JsonSerializer.Serialize(first.Compatibility).Contains("amountA", StringComparison.Ordinal),
            "public compatibility uses generic field IDs rather than raw canonical names");

        foreach (var key in new[] { PdfName.V, PdfName.DV })
        {
            var draft = Discover(Change(pdf => Field(pdf).Put(key, new PdfString(sentinel))));
            check(draft.Compatibility.Prefilled && draft.Compatibility.Status == "unsupported" && draft.Targets.Count == 0,
                "prefilled current/default value refuses a configuration draft: " + key.GetValue());
            check(!JsonSerializer.Serialize(draft).Contains(sentinel, StringComparison.Ordinal),
                "prefilled value never enters draft or compatibility JSON: " + key.GetValue());
        }
        var selected = Discover(Change(pdf =>
        {
            var field = Field(pdf); field.Put(PdfName.FT, PdfName.Btn); field.Put(PdfName.AS, new PdfName(sentinel));
        }));
        check(selected.Compatibility.Prefilled && selected.Targets.Count == 0
            && !JsonSerializer.Serialize(selected).Contains(sentinel, StringComparison.Ordinal),
            "selected control is rejected without returning its selected state");

        var radio = Discover(Fixture.Make(fieldType: "Btn", flags: 1 << 15));
        check(radio.Compatibility.Status == "unsupported" && radio.Compatibility.MissingEngineFeatures.Contains("field.radio"),
            "radio is an explicit missing feature rather than guessed checkbox configuration");
        var multi = Discover(Fixture.Make(repeated: true));
        check(multi.Compatibility.Status == "unsupported" && multi.Compatibility.MissingEngineFeatures.Contains("field.singleWidget"),
            "multiple widgets are an explicit unsupported capability");
        var multiline = Discover(Fixture.Make(flags: 1 << 12));
        check(multiline.Compatibility.Status == "unsupported" && multiline.Compatibility.MissingEngineFeatures.Contains("text.multiline"),
            "multiline is not silently configured as a supported single line");
        var encoded = Discover(Change(pdf => Font(pdf).Put(PdfName.Encoding, new PdfDictionary())));
        check(encoded.Compatibility.Status == "unsupported" && encoded.Compatibility.MissingEngineFeatures.Contains("font.supportedSourceEncoding")
            && encoded.Targets.Count == 0, "an encoding dictionary cannot masquerade as observed absent source encoding");
        var composite = Discover(Change(pdf => Font(pdf).Put(PdfName.Subtype, PdfName.Type0)));
        check(composite.Compatibility.Status == "unsupported" && composite.Compatibility.MissingEngineFeatures.Contains("font.simpleType1OrTrueType"),
            "composite source fonts need engine support rather than configuration metadata");
        foreach (var malformedFont in MalformedFontStructures())
        {
            var rejected = Discover(malformedFont.Bytes);
            check(rejected.Compatibility.Status == "invalid" && rejected.Compatibility.ExitCode == 2 && rejected.Targets.Count == 0
                && rejected.Compatibility.Issues.Any(f => f.Code == "E_CONFIG_FONT_STRUCTURE"),
                "malformed descriptor/program structure cannot be observed as source-program absence: " + malformedFont.Name);
            check(!JsonSerializer.Serialize(rejected).Contains(FontSentinel, StringComparison.Ordinal),
                "malformed font payload never enters discovery diagnostics: " + malformedFont.Name);
        }
        var scripts = Discover(Fixture.Make(catalog: "/OpenAction 10 0 R", extra: "<< /S /JavaScript /JS (" + sentinel + ") >>"));
        check(scripts.Compatibility.Status == "unsupported" && scripts.Compatibility.MissingEngineFeatures.Contains("active.action")
            && !JsonSerializer.Serialize(scripts).Contains(sentinel, StringComparison.Ordinal),
            "active action is rejected without exporting its source");
        var xfa = Discover(Fixture.Make(xfa: "<template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\"><subform/></template>"));
        check(xfa.Compatibility.Status == "unsupported" && xfa.Compatibility.MissingEngineFeatures.Contains("xfa.configuredAcroform"),
            "generic configuration grants no XFA exception");
        var dataset = Discover(Change(pdf =>
        {
            var xml = "<xfa:datasets xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\"><xfa:data><record>" + sentinel + "</record></xfa:data></xfa:datasets>";
            var packets = new PdfArray(); packets.Add(new PdfString("datasets")); packets.Add(new PdfStream(Encoding.UTF8.GetBytes(xml)));
            PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject().Put(PdfName.XFA, packets);
        }));
        check(dataset.Compatibility.Prefilled && dataset.Targets.Count == 0 && !JsonSerializer.Serialize(dataset).Contains(sentinel, StringComparison.Ordinal),
            "populated dataset is observed only as a refusal flag");
        var malformed = Discover(Encoding.ASCII.GetBytes("not a PDF"));
        check(malformed.Compatibility.Status == "invalid" && malformed.Compatibility.ExitCode == 2 && malformed.Targets.Count == 0,
            "malformed input cannot produce a partial configuration draft");
        var outside = Discover(Change(pdf => Field(pdf).Put(PdfName.Rect, new PdfArray(new double[] { -1, 10, 99, 30 }))));
        check(outside.Compatibility.Status == "unsupported" && outside.Targets.Count == 0,
            "widget outside actual page bounds cannot be approved by configuration");
    }

    private static PdfDictionary Field(PdfDocument pdf)
        => PdfAcroForm.GetAcroForm(pdf, false)!.GetField("group.amountA").GetPdfObject();

    private static PdfDictionary Font(PdfDocument pdf)
        => PdfAcroForm.GetAcroForm(pdf, false)!.GetPdfObject().GetAsDictionary(PdfName.DR).GetAsDictionary(PdfName.Font).GetAsDictionary(new PdfName("Helvetica"));

    private const string FontSentinel = "UNTRUSTED_FONT_PROGRAM_SENTINEL_24901";

    internal static IEnumerable<(string Name, byte[] Bytes)> MalformedFontStructures()
    {
        yield return ("descriptor-string", Change(pdf => Font(pdf).Put(PdfName.FontDescriptor, new PdfString(FontSentinel))));
        yield return ("descriptor-stream", Change(pdf =>
        {
            var descriptor = new PdfStream(Encoding.ASCII.GetBytes(FontSentinel));
            descriptor.MakeIndirect(pdf); Font(pdf).Put(PdfName.FontDescriptor, descriptor);
        }));
        foreach (var key in new[] { PdfName.FontFile, PdfName.FontFile2, PdfName.FontFile3 })
            yield return ("non-stream-" + key.GetValue(), ChangeDescriptor(descriptor => descriptor.Put(key, new PdfString(FontSentinel))));
        yield return ("multiple-malformed-program-keys", ChangeDescriptor(descriptor =>
        {
            descriptor.Put(PdfName.FontFile, new PdfString(FontSentinel));
            descriptor.Put(PdfName.FontFile2, new PdfDictionary());
        }));
        yield return ("stream-plus-malformed-program-key", ChangeDescriptor(descriptor =>
        {
            descriptor.Put(PdfName.FontFile2, new PdfStream(Encoding.ASCII.GetBytes(FontSentinel)));
            descriptor.Put(PdfName.FontFile3, new PdfString(FontSentinel));
        }));
        yield return ("multiple-stream-program-keys", ChangeDescriptor(descriptor =>
        {
            descriptor.Put(PdfName.FontFile, new PdfStream(Encoding.ASCII.GetBytes(FontSentinel)));
            descriptor.Put(PdfName.FontFile2, new PdfStream(Encoding.ASCII.GetBytes(FontSentinel)));
        }));
    }

    private static byte[] ChangeDescriptor(Action<PdfDictionary> change)
        => Change(pdf =>
        {
            var descriptor = new PdfDictionary(); change(descriptor);
            foreach (var key in descriptor.KeySet())
                if (descriptor.Get(key, true) is PdfStream program) program.MakeIndirect(pdf);
            Font(pdf).Put(PdfName.FontDescriptor, descriptor);
        });

    private static byte[] Change(Action<PdfDocument> change)
    {
        using var output = new MemoryStream();
        using (var input = new MemoryStream(Fixture.Make()))
        using (var pdf = new PdfDocument(new PdfReader(input), new PdfWriter(output))) change(pdf);
        return output.ToArray();
    }
}
