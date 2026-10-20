using System.Security.Cryptography;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PdfForm.Core;
using PdfForm.Pdf;

// Entire harness is fictional/offline. Derived-input mutations are confined to this friend assembly.
string root = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
string output = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(root, "artifacts", "verified-fill-run-01"));
if (Directory.Exists(output)) { Console.Error.WriteLine("E_TEST_OUTPUT_EXISTS"); return 2; }
Directory.CreateDirectory(output);
iText.Commons.ITextLogManager.SetLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
int passed = 0;
string stage = "startup";
var checkpoint = new List<object>();
void Check(bool condition, string name)
{
    if (!condition) throw new TestFailure(name);
    passed++;
}
void Good(VerifiedFillResult result, string name)
{
    Check(result.ReadyToPublish && result.ExitCode == 0, name + ":" + string.Join(',', result.Findings.Where(x => x.Severity == "error").Select(x => x.Code + "@" + x.Field)));
    Check(result.ValidationLevels["render"] == "passed" && result.ValidationLevels["packet"] == "not_run", name + ":levels");
}
void Reject(byte[] bytes, RenderManifest manifest, string name, bool flat = false)
{
    bool rejected = false;
    try { if (flat) VerifiedFormRenderer.VerifyFlattened(bytes, manifest); else VerifiedFormRenderer.VerifyEditable(bytes, manifest); }
    catch (VerifiedFillException) { rejected = true; }
    Check(rejected, name);
}
void RejectCode(byte[] bytes, RenderManifest manifest, string name, string expected)
{
    string? code = null;
    try { VerifiedFormRenderer.VerifyEditable(bytes, manifest); }
    catch (VerifiedFillException ex) { code = ex.Code; }
    if (code != expected)
    {
        Save("failure-" + name + ".pdf", bytes);
        string observed = code is null ? "NO_REJECTION"
            : Regex.IsMatch(code, @"\AE_[A-Z0-9_]{1,63}\z", RegexOptions.CultureInvariant) ? code : "UNEXPECTED_CODE";
        throw new TestFailure(name, expected, observed);
    }
    Check(true, name);
}
byte[] Json(JsonObject node) => Encoding.UTF8.GetBytes(node.ToJsonString());
JsonObject Data(string file) => JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "samples", file)))!.AsObject();
void Save(string name, byte[] bytes)
{
    using var file = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
    file.Write(bytes);
}
byte[] Mutate(byte[] source, Action<PdfDocument> action)
{
    using var outputBytes = new BoundedPdfMemoryStream();
    using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(source), new PdfWriter(outputBytes)))
    { pdf.SetFlushUnusedObjects(false); action(pdf); }
    return outputBytes.ToArray();
}
Dictionary<string, PdfFormField> Fields(PdfDocument pdf) => PdfAcroForm.GetAcroForm(pdf, false).GetAllFormFields()
    .Where(x => x.Value.IsTerminalFormField()).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);


// Trusted derived fixtures only. Font objects are reused by exact dictionary identity within this one open document.
// The production replacement and complete per-target reopened verification still run for every fixture.
Dictionary<string, PdfFont> FixtureFonts(PdfDocument pdf, RenderManifest manifest)
{
    var fields = Fields(pdf); var result = new Dictionary<string, PdfFont>(StringComparer.Ordinal);
    var cache = new Dictionary<PdfDictionary, PdfFont>(ReferenceEqualityComparer.Instance);
    var acro = PdfAcroForm.GetAcroForm(pdf, false).GetPdfObject();
    var verification = new NativeTextAppearance.VerificationContext();
    foreach (var pair in manifest.Targets.Where(x => x.Value.Assignment.Kind == "text"))
    {
        var target = pair.Value; var field = fields[pair.Key]; var inspected = target.Widget.Effective.Font;
        PdfDictionary dictionary;
        if (target.Font!.Policy == ScheduleBEmbeddedFont.Policy)
        {
            // The inspected source font remains TimesLTStd-Roman. Resolve the separately witnessed output.
            if (manifest.TemplateSha256 != ApprovedTemplates.ScheduleBSha256 || inspected.Name != "TimesLTStd-Roman")
                throw new TestFailure("fixture-source-font-identity");
            dictionary = ScheduleBEmbeddedFont.VerifyDefaults(pdf, field, target.Font, verification);
        }
        else
        {
            if (inspected.Name != target.Font.Name || inspected.Size != NativeTextAppearance.FontSize)
                throw new TestFailure("fixture-font-inspection-identity");
            var resources = VerifiedFormRenderer.Inherited(field.GetPdfObject(), PdfName.DR, acro) as PdfDictionary;
            dictionary = resources?.GetAsDictionary(PdfName.Font)?.GetAsDictionary(new PdfName(inspected.Name!))
                ?? throw new TestFailure("fixture-native-font-resource-missing");
        }
        if (!cache.TryGetValue(dictionary, out var font))
        {
            if (cache.Count >= 32) throw new TestFailure("fixture-font-resource-limit");
            if (target.Font.Policy == ScheduleBEmbeddedFont.Policy) font = PdfFontFactory.CreateFont(dictionary);
            else font = NativeTextAppearance.ResolveFont(manifest.TemplateSha256, pdf, field, inspected, out _);
            NativeTextAppearance.VerifyFont(font, target.Font, verification);
            cache.Add(dictionary, font);
        }
        result.Add(pair.Key, font);
    }
    return result;
}

try
{
    stage = "profile-admission";
    var template941 = TemplateSnapshot.Load(Path.Combine(root, "fixtures/appearance/forms/f941.pdf"));
    var templateB = TemplateSnapshot.Load(Path.Combine(root, "fixtures/appearance/forms/f941sb.pdf"));
    var profile941Bytes = File.ReadAllBytes(Path.Combine(root, "profiles/irs-941-2026-03.json"));
    var profileBBytes = File.ReadAllBytes(Path.Combine(root, "profiles/irs-941sb-2024-03.json"));
    var admitted941 = ProductionProfileRegistry.Bind(profile941Bytes, template941.Sha256);
    var admittedB = ProductionProfileRegistry.Bind(profileBBytes, templateB.Sha256);
    Check(admitted941.IsValid && admittedB.IsValid, "exact-private-profile-pairs-admitted");
    var profile941 = admitted941.Value!.ParseDefinition();
    var profileB = admittedB.Value!.ParseDefinition();
    var input941 = Data("941-2026q3-monthly-final-refund.json");
    var inputB = Data("941sb-2026q3.json");
    inputB["values"]!["name"] = "Fictional O'Connor-Smith & Co. É";
    stage = "public-positive-render";
    var ready941 = VerifiedFillPipeline.Prepare(template941, admitted941.Value, Json(input941), false);
    Good(ready941, "941-editable");
    var ready941Flat = VerifiedFillPipeline.Prepare(template941, admitted941.Value, Json(input941), true);
    Good(ready941Flat, "941-flattened");
    var readyB = VerifiedFillPipeline.Prepare(templateB, admittedB.Value!, Json(inputB), false);
    Good(readyB, "schedule-b-editable");
    var readyBFlat = VerifiedFillPipeline.Prepare(templateB, admittedB.Value!, Json(inputB), true);
    Good(readyBFlat, "schedule-b-flattened");
    Check(new[] { readyB, readyBFlat }.All(r => r.Findings.Any(f => f.Code == "W_FONT_EXPLICIT_REPLACEMENT" && f.Severity == "warning")
        && !r.Findings.Any(f => f.Code == "W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED")), "ordinary-name-both-modes-explicit-embedded-policy-warning");
    Check(!ready941.Findings.Any(f => f.Code is "W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED" or "W_FONT_EXPLICIT_REPLACEMENT"), "embedded941-no-text-portability-exception");
    Check(!ready941.Findings.Any(f => f.Code == "W_CHECKBOX_FONT_PORTABILITY_UNQUALIFIED")
        && !readyB.Findings.Any(f => f.Code == "W_CHECKBOX_FONT_PORTABILITY_UNQUALIFIED"), "both-forms-use-resource-free-vector-checkboxes");
    Check(ready941.Stats["targetsVerified"] == 116 && readyB.Stats["targetsVerified"] == 212, "328-targets-verified");
    stage = "create-new-publication";
    foreach (var (name, result) in new[] { ("941.editable.pdf", ready941), ("941.flattened.pdf", ready941Flat), ("schedule-b.editable.pdf", readyB), ("schedule-b.flattened.pdf", readyBFlat) })
    {
        var publish = VerifiedFillPipeline.PublishCreateNew(result, Path.Combine(output, name), []);
        Check(publish.Success && publish.ArtifactState == "published", "create-new-" + name);
        byte[] before = File.ReadAllBytes(Path.Combine(output, name));
        var duplicate = VerifiedFillPipeline.PublishCreateNew(result, Path.Combine(output, name), []);
        Check(!duplicate.Success && duplicate.ErrorCode == "E_DESTINATION_EXISTS"
            && before.SequenceEqual(File.ReadAllBytes(Path.Combine(output, name))), "existing-destination-unchanged-" + name);
        checkpoint.Add(new { name, bytes = before.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(before)) });
    }
    stage = "schedule-b-public-name-repertoire";
    // Real public preparation, including data validation, complete replacement, closed readback and
    // (when requested) flatten verification. Three bounded names cover every admitted code point.
    string admittedCharacters = string.Concat(Enumerable.Range(32, 95).Select(char.ConvertFromUtf32)) + "É";
    string[] nameChunks = Enumerable.Range(0, 3).Select(i => admittedCharacters.Substring(i * 32, 32)).ToArray();
    Check(admittedCharacters.Length == 96 && nameChunks.All(s => s.Length == 32)
        && string.Concat(nameChunks) == admittedCharacters, "name-fixtures-cover-exact96-repertoire");
    for (int chunk = 0; chunk < nameChunks.Length; chunk++)
    {
        var data = Data("941sb-2026q3.json"); data["values"]!["name"] = nameChunks[chunk];
        foreach (bool flatten in new[] { false, true })
        {
            string id = $"schedule-b.name-repertoire-{chunk + 1:D2}." + (flatten ? "flattened" : "editable");
            var prepared = VerifiedFillPipeline.Prepare(templateB, admittedB.Value!, Json(data), flatten);
            Good(prepared, id);
            Check(prepared.Findings.Any(f => f.Code == "W_FONT_EXPLICIT_REPLACEMENT" && f.Severity == "warning")
                && !prepared.Findings.Any(f => f.Code == "W_FONT_UNEMBEDDED_PORTABILITY_UNQUALIFIED"), id + "-explicit-embedded-policy-warning");
            var published = VerifiedFillPipeline.PublishCreateNew(prepared, Path.Combine(output, id + ".pdf"), []);
            Check(published.Success && published.ArtifactState == "published", id + "-verified-create-new-publication");
        }
    }
    var nameTarget = readyB.Candidate!.ManifestForFixture.Targets.Values.Single(t => t.Assignment.Key == "name");
    Save("schedule-b.name-repertoire.assignments.json", JsonSerializer.SerializeToUtf8Bytes(new
    {
        schema = "pdfform.synthetic-public-name-repertoire/1", templateSha256 = templateB.Sha256,
        field = nameTarget.Assignment.Field, page = nameTarget.Widget.Page, rect = nameTarget.Widget.Rectangle,
        repertoire = "U+0020-U+007E plus U+00C9", font = ScheduleBEmbeddedFont.Name, fontSizePt = 10, embeddedProgramSha256 = ScheduleBEmbeddedFont.ProgramSha256, policy = ScheduleBEmbeddedFont.Policy,
        provenance = "Public Prepare and verified create-new publication, each in editable and flattened modes.",
        ordinaryName = inputB["values"]!["name"]!.GetValue<string>(),
        chunks = nameChunks.Select((text, i) => new { id = $"schedule-b.name-repertoire-{i + 1:D2}", text })
    }, new JsonSerializerOptions { WriteIndented = true }));

    stage = "validation-refusals";
    Check(VerifiedFillPipeline.Prepare(template941, admitted941.Value, "{"u8, false).ExitCode == 2, "malformed-data-before-render");
    Check(VerifiedFillPipeline.Prepare(templateB, admitted941.Value, Json(input941), false).Findings.Any(f => f.Code == "E_TEMPLATE_PROFILE"), "cross-template-profile-rejected");
    var bad941 = Data("941-2026q3-monthly-final-refund.json"); bad941["values"]!["name"] = "FICTIONAL 🚀";
    Check(VerifiedFillPipeline.Prepare(template941, admitted941.Value, Json(bad941), false).Findings.Any(f => f.Code == "E_FONT_GLYPH"), "native-missing-glyph-rejected");
    var rejectedNames = new[]
    {
        (Id: "lowercase-accent", Text: "é", Codes: new[] { "E_FONT_GLYPH_UNQUALIFIED" }),
        (Id: "curly-apostrophe", Text: "O\u2019Connor", Codes: new[] { "E_FONT_GLYPH_UNQUALIFIED" }),
        (Id: "combining-accent", Text: "E\u0301", Codes: new[] { "E_FONT_GLYPH", "E_FONT_GLYPH_UNQUALIFIED" }),
        (Id: "emoji", Text: "FICTIONAL 🚀", Codes: new[] { "E_FONT_GLYPH" }),
        (Id: "forty-wide-letters", Text: new string('W', 40), Codes: new[] { "E_TEXT_OVERFLOW", "E_TEXT_MAX_LENGTH" })
    };
    foreach (var negative in rejectedNames)
    {
        var data = Data("941sb-2026q3.json"); data["values"]!["name"] = negative.Text;
        var rejected = VerifiedFillPipeline.Prepare(templateB, admittedB.Value!, Json(data), false);
        Check(!rejected.ReadyToPublish && rejected.Candidate is null
            && rejected.Findings.Any(f => negative.Codes.Contains(f.Code)), "schedule-b-reject-" + negative.Id);
        string destination = Path.Combine(output, "must-not-exist-" + negative.Id + ".pdf");
        Check(!VerifiedFillPipeline.PublishCreateNew(rejected, destination, []).Success && !File.Exists(destination),
            "schedule-b-no-publication-" + negative.Id);
    }
    var longName = Data("941-2026q3-monthly-final-refund.json"); longName["values"]!["name"] = new string('W', 40);
    var tooWide = VerifiedFillPipeline.Prepare(template941, admitted941.Value, Json(longName), false);
    Check(!tooWide.ReadyToPublish && tooWide.Findings.Any(f => f.Code is "E_TEXT_OVERFLOW" or "E_TEXT_MAX_LENGTH"), "fixed10-overflow-no-publish");
    Check(!VerifiedFillPipeline.PublishCreateNew(tooWide, Path.Combine(output, "must-not-exist.pdf"), []).Success
        && !File.Exists(Path.Combine(output, "must-not-exist.pdf")), "unverified-candidate-not-published");
    string diagnostics = JsonSerializer.Serialize(ready941, StrictJson.Options);
    Check(!diagnostics.Contains("EXAMPLE WATER", StringComparison.Ordinal) && !diagnostics.Contains("123456789ABC", StringComparison.Ordinal)
        && !diagnostics.Contains("Candidate", StringComparison.Ordinal) && !diagnostics.Contains("%PDF", StringComparison.Ordinal), "value-free-result-and-private-payload");

    stage = "font-cache-regression";
    // Repeated uses of one immutable font do not repeatedly consume the decompression budget.
    using (var fontPdf = new PdfDocument(VerifiedFormRenderer.Reader(ready941.Candidate!.CopyForFixture())))
    {
        var fontDictionary = PdfAcroForm.GetAcroForm(fontPdf, false).GetPdfObject().GetAsDictionary(PdfName.DR)
            .GetAsDictionary(PdfName.Font).GetAsDictionary(new PdfName("TimesNewRomanPSMT"));
        var fontStream = fontDictionary.GetAsDictionary(PdfName.FontDescriptor).GetAsStream(PdfName.FontFile2);
        var verification = new NativeTextAppearance.VerificationContext();
        string hash = verification.EmbeddedDigest(fontStream);
        for (int repeated = 0; repeated < 512; repeated++)
            if (verification.EmbeddedDigest(fontStream) != hash) throw new TestFailure("font-identity-cache-mismatch");
        Check(true, "read-only-font-resource-decoded-once-with-unchanged-memory-caps");
    }
    stage = "schedule-b-embedded-defaults-and-tampering";
    var bytesB = readyB.Candidate!.CopyForFixture(); var manifestB = readyB.Candidate.ManifestForFixture;
    string populatedB = manifestB.Targets.Single(t => t.Value.Assignment.Key == "name").Key;
    string blankB = manifestB.Targets.First(t => t.Value.Assignment.Kind == "text" && t.Value.Assignment.Text.Length == 0).Key;
    using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytesB)))
    {
        var fields = Fields(pdf); var verification = new NativeTextAppearance.VerificationContext();
        var identities = new HashSet<string>(StringComparer.Ordinal); int verified = 0, blanks = 0;
        foreach (var pair in manifestB.Targets.Where(t => t.Value.Assignment.Kind == "text"))
        {
            var dictionary = ScheduleBEmbeddedFont.VerifyDefaults(pdf, fields[pair.Key], pair.Value.Font!, verification);
            identities.Add(dictionary.GetIndirectReference().ToString()); verified++;
            if (pair.Value.Assignment.Text.Length == 0) { blanks++; continue; }
            var apFonts = fields[pair.Key].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N)
                .GetAsDictionary(PdfName.Resources).GetAsDictionary(PdfName.Font);
            if (apFonts.Size() != 1 || !apFonts.GetAsDictionary(apFonts.KeySet().Single()).GetIndirectReference().Equals(dictionary.GetIndirectReference()))
                throw new TestFailure("schedule-b-AP-default-resource-coherence");
        }
        Check(verified == 208 && blanks > 0 && identities.Count == 1, "schedule-b-single-embedded-font-all208-defaults-including-blanks");
    }
    // Materialize the same field wrappers and serialize without mutation first. This is fixture setup,
    // not a relaxation of the exact-code negative or a new product acceptance rule.
    byte[] roundtripB = Mutate(bytesB, pdf => { _ = Fields(pdf); });
    VerifiedFormRenderer.VerifyEditable(roundtripB, manifestB);
    string[] FieldStateExceptDa(byte[] bytes, string fieldName)
    {
        using var pdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes));
        var field = Fields(pdf)[fieldName].GetPdfObject();
        var widget = Fields(pdf)[fieldName].GetWidgets()[0].GetPdfObject();
        var ap = widget.GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        return [field.Get(PdfName.V)?.ToString() ?? "<absent>", field.Get(PdfName.DV)?.ToString() ?? "<absent>",
            widget.Get(PdfName.Rect)?.ToString() ?? "<absent>", field.Get(PdfName.DR)?.ToString() ?? "<absent>",
            widget.Get(PdfName.DR)?.ToString() ?? "<absent>", Convert.ToHexString(SHA256.HashData(ap.GetBytes())),
            AppearanceResourceIdentity.Compute(ap)];
    }
    byte[] MutateFieldDa(string fieldName, string da, bool expectMissingResource)
    {
        byte[] altered = Mutate(bytesB, pdf => Fields(pdf)[fieldName].GetPdfObject().Put(PdfName.DA, new PdfString(da)));
        using (var pdf = new PdfDocument(VerifiedFormRenderer.Reader(altered)))
            if (Fields(pdf)[fieldName].GetPdfObject().GetAsString(PdfName.DA)?.ToUnicodeString() != da)
                throw new TestFailure("schedule-b-field-DA-mutation-not-persisted");
        if (!FieldStateExceptDa(roundtripB, fieldName).SequenceEqual(FieldStateExceptDa(altered, fieldName)))
            throw new TestFailure("schedule-b-field-DA-mutation-not-isolated");
        var errors = new PdfTemplateInspector().Inspect(TemplateSnapshot.FromBytes(altered)).Findings
            .Where(f => f.Severity == "error").Select(f => f.Code).ToArray();
        if (expectMissingResource ? errors.Length != 1 || errors[0] != "E_FONT_RESOURCE" : errors.Length != 0)
            throw new TestFailure("schedule-b-field-DA-inspection-precondition");
        return altered;
    }
    // Retain the old missing-resource fixture and its earlier gate. A valid same-resource DA-color
    // mutation separately reaches the exact defaults check with AP/resources/value/geometry unchanged.
    foreach (string fieldName in new[] { populatedB, blankB })
    {
        string suffix = fieldName == blankB ? "blank" : "populated";
        RejectCode(MutateFieldDa(fieldName, "/TimesLTStd-Roman 10 Tf 0 g", true),
            manifestB, "reject-schedule-b-field-DA-missing-resource-" + suffix, "E_VERIFY_ACTIVE_CONTENT");
        RejectCode(MutateFieldDa(fieldName, "/PdfFormScheduleB 10 Tf .5 g", false),
            manifestB, "reject-schedule-b-field-DA-" + suffix, "E_VERIFY_FIELD_FONT_DEFAULT");
        RejectCode(Mutate(bytesB, pdf =>
        {
            var native = PdfAcroForm.GetAcroForm(pdf, false).GetPdfObject().GetAsDictionary(PdfName.DR).GetAsDictionary(PdfName.Font)
                .GetAsDictionary(new PdfName("TimesLTStd-Roman"));
            if (native is null) throw new TestFailure("schedule-b-original-resource-preserved-for-mutation");
            var fonts = new PdfDictionary(); fonts.Put(ScheduleBEmbeddedFont.ResourceName, native);
            var resources = new PdfDictionary(); resources.Put(PdfName.Font, fonts);
            Fields(pdf)[fieldName].GetPdfObject().Put(PdfName.DR, resources);
        }), manifestB, "reject-schedule-b-local-DR-" + (fieldName == blankB ? "blank" : "populated"), "E_VERIFY_AP_FONT");
    }
    RejectCode(Mutate(bytesB, pdf => PdfAcroForm.GetAcroForm(pdf, false).GetPdfObject().Put(PdfName.DA, new PdfString("/PdfFormScheduleB 9 Tf 0 g"))),
        manifestB, "reject-schedule-b-AcroForm-default-size", "E_VERIFY_FIELD_FONT_DEFAULT");
    PdfDictionary EmbeddedDictionary(PdfDocument pdf) => PdfAcroForm.GetAcroForm(pdf, false).GetPdfObject()
        .GetAsDictionary(PdfName.DR).GetAsDictionary(PdfName.Font).GetAsDictionary(ScheduleBEmbeddedFont.ResourceName);
    // Use a different valid embedded program, so refusal proves identity rather than merely parse failure.
    RejectCode(Mutate(bytesB, pdf =>
    {
        using var other = new PdfDocument(VerifiedFormRenderer.Reader(ready941.Candidate!.CopyForFixture()));
        var descriptor = PdfAcroForm.GetAcroForm(other, false).GetPdfObject().GetAsDictionary(PdfName.DR).GetAsDictionary(PdfName.Font)
            .GetAsDictionary(new PdfName("TimesNewRomanPSMT")).GetAsDictionary(PdfName.FontDescriptor);
        EmbeddedDictionary(pdf).GetAsDictionary(PdfName.FontDescriptor).Put(PdfName.FontFile2, descriptor.GetAsStream(PdfName.FontFile2).CopyTo(pdf));
    }), manifestB, "reject-schedule-b-different-valid-embedded-program", "E_VERIFY_AP_FONT_PROGRAM");
    RejectCode(Mutate(bytesB, pdf => EmbeddedDictionary(pdf).Put(PdfName.Encoding, new PdfName("MacRomanEncoding"))),
        manifestB, "reject-schedule-b-encoding-drift", "E_VERIFY_AP_FONT");
    RejectCode(Mutate(bytesB, pdf =>
    {
        var widths = EmbeddedDictionary(pdf).GetAsArray(PdfName.Widths);
        if (widths is null || widths.Size() == 0) throw new TestFailure("schedule-b-widths-present");
        widths.Set(0, new PdfNumber(widths.GetAsNumber(0).IntValue() + 1));
    }), manifestB, "reject-schedule-b-width-drift", "E_VERIFY_AP_FONT");
    const string addedMapping = "/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /FixtureIdentity def\n/CMapType 2 def\n1 begincodespacerange\n<00> <FF>\nendcodespacerange\n1 beginbfrange\n<00> <FF> <0000>\nendbfrange\nendcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n";
    RejectCode(Mutate(bytesB, pdf => EmbeddedDictionary(pdf).Put(PdfName.ToUnicode, new PdfStream(Encoding.ASCII.GetBytes(addedMapping)).MakeIndirect(pdf))),
        manifestB, "reject-schedule-b-added-ToUnicode", "E_VERIFY_AP_FONT_MAPPING");
    // An AP-only font change must fail even though every editable DA/DR remains correct.
    RejectCode(Mutate(bytesB, pdf =>
    {
        var ap = Fields(pdf)[populatedB].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        var oldFonts = ap.GetAsDictionary(PdfName.Resources).GetAsDictionary(PdfName.Font);
        var fonts = new PdfDictionary();
        fonts.Put(oldFonts.KeySet().Single(), PdfAcroForm.GetAcroForm(pdf, false).GetPdfObject().GetAsDictionary(PdfName.DR)
            .GetAsDictionary(PdfName.Font).GetAsDictionary(new PdfName("TimesLTStd-Roman")));
        var resources = new PdfDictionary(); resources.Put(PdfName.Font, fonts); ap.Put(PdfName.Resources, resources);
    }), manifestB, "reject-schedule-b-AP-font-with-correct-defaults", "E_VERIFY_AP_FONT");
    stage = "editable-tamper-negatives";
    // Independent corruption checks assert that reopen verification includes blanks/defaults/AP, not only populated /V.
    var bytes941 = ready941.Candidate!.CopyForFixture(); var manifest941 = ready941.Candidate.ManifestForFixture;
    var textTarget = manifest941.Targets.First(t => t.Value.Assignment.Kind == "text" && t.Value.Assignment.Text.Length > 0).Key;
    var blankTarget = manifest941.Targets.First(t => t.Value.Assignment.Kind == "text" && t.Value.Assignment.Text.Length == 0).Key;
    var buttonTarget = manifest941.Targets.First(t => t.Value.Assignment.Kind == "checkbox").Key;
    Reject(Mutate(bytes941, pdf => Fields(pdf)[textTarget].GetPdfObject().Put(PdfName.V, new PdfString("wrong"))), manifest941, "reject-wrong-value");
    Reject(Mutate(bytes941, pdf => Fields(pdf)[blankTarget].GetPdfObject().Put(PdfName.V, new PdfString("stale"))), manifest941, "reject-expected-blank-stale-value");
    Reject(Mutate(bytes941, pdf => Fields(pdf)[textTarget].GetPdfObject().Put(PdfName.DV, new PdfString("stale"))), manifest941, "reject-reset-default");
    Reject(Mutate(bytes941, pdf => Fields(pdf)[textTarget].GetPdfObject().Put(PdfName.RV, new PdfString("stale"))), manifest941, "reject-rich-value");
    Reject(Mutate(bytes941, pdf => Fields(pdf)[textTarget].GetPdfObject().GetAsDictionary(PdfName.Parent).Put(PdfName.DV, new PdfString("stale"))), manifest941, "reject-inherited-default");
    Reject(Mutate(bytes941, pdf => Fields(pdf)[textTarget].GetWidgets()[0].GetPdfObject().Remove(PdfName.AP)), manifest941, "reject-missing-normal-ap");
    Reject(Mutate(bytes941, pdf => Fields(pdf)[buttonTarget].GetWidgets()[0].GetPdfObject().Put(PdfName.AS, new PdfName("bad"))), manifest941, "reject-checkbox-state");
    Reject(Mutate(bytes941, pdf => PdfAcroForm.GetAcroForm(pdf, false).GetPdfObject().Put(PdfName.NeedAppearances, PdfBoolean.TRUE)), manifest941, "reject-viewer-generated-ap");
    Reject(Mutate(bytes941, pdf => pdf.GetPage(1).GetContentStream(0).SetData("q Q"u8.ToArray())), manifest941, "reject-static-page-content-change");
    Reject(Mutate(bytes941, pdf => pdf.GetCatalog().GetPdfObject().Put(PdfName.OpenAction, new PdfDictionary())), manifest941, "reject-catalog-action");
    // Invisibility/geometry mutations preserve decoded text but must never pass the bounded generated-content policy.
    Reject(Mutate(bytes941, pdf =>
    {
        var ap = Fields(pdf)[textTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        ap.SetData("q 0 0 0 0 re W n\n"u8.ToArray().Concat(ap.GetBytes()).Concat("\nQ"u8.ToArray()).ToArray());
    }), manifest941, "reject-zero-area-clip");
    Reject(Mutate(bytes941, pdf =>
    {
        var ap = Fields(pdf)[textTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        ap.SetData("q 1 0 0 0.1 0 0 cm\n"u8.ToArray().Concat(ap.GetBytes()).Concat("\nQ"u8.ToArray()).ToArray());
    }), manifest941, "reject-vertical-glyph-scaling");
    stage = "color-mapping-tamper-negatives";
    string colorTarget = manifest941.Targets.Single(t => t.Value.DateCells is not null).Key;
    byte[] whiteText = Mutate(bytes941, pdf =>
    {
        var ap = Fields(pdf)[colorTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        string content = Encoding.Latin1.GetString(ap.GetBytes());
        var commands = GeneratedContentPolicy.Commands(ap.GetBytes()).ToArray();
        if (commands.Count(c => c[^1].ToString() == "BT") != 1
            || commands.Any(c => c[^1].ToString() is "g" or "rg" or "k"))
            throw new TestFailure("CMYK-white-tamper-date-color-precondition");
        // The engine elides redundant black commands. Insert white before the sole BT, without changing text/position bytes.
        ap.SetData(Encoding.Latin1.GetBytes(content.Replace("BT", "0 0 0 0 k\nBT", StringComparison.Ordinal)));
    });
    string? whiteCode = null;
    try { VerifiedFormRenderer.VerifyEditable(whiteText, manifest941); } catch (VerifiedFillException ex) { whiteCode = ex.Code; }
    Check(whiteCode == "E_VERIFY_AP_COLOR", "reject-same-text-CMYK-white-through-color-model-check");
    byte[] remappedText = Mutate(bytes941, pdf =>
    {
        var ap = Fields(pdf)[textTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        var fonts = ap.GetAsDictionary(PdfName.Resources).GetAsDictionary(PdfName.Font);
        var font = fonts.GetAsDictionary(fonts.KeySet().Single());
        if (font.ContainsKey(PdfName.ToUnicode)) throw new TestFailure("ToUnicode-absence-tamper-precondition");
        // Valid identity CMap leaves the numeric fixture text unchanged; it still violates exact resource identity.
        const string cmap = "/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /FixtureIdentity def\n/CMapType 2 def\n1 begincodespacerange\n<00> <FF>\nendcodespacerange\n1 beginbfrange\n<00> <FF> <0000>\nendbfrange\nendcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n";
        font.Put(PdfName.ToUnicode, new PdfStream(Encoding.ASCII.GetBytes(cmap)).MakeIndirect(pdf));
    });
    string? mappingCode = null;
    try { VerifiedFormRenderer.VerifyEditable(remappedText, manifest941); } catch (VerifiedFillException ex) { mappingCode = ex.Code; }
    Check(mappingCode == "E_VERIFY_AP_FONT_MAPPING", "reject-added-identity-ToUnicode-with-same-ASCII-text");
    string matrixTarget = colorTarget;
    Reject(Mutate(bytes941, pdf =>
    {
        var ap = Fields(pdf)[matrixTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        ap.SetData(ChangeTextMatrices(ap.GetBytes(), .1, 0));
    }), manifest941, "reject-direct-Tm-vertical-scaling");
    string combTarget = manifest941.Targets.First(t => t.Value.Widget.Effective.Flags.Comb && t.Value.Assignment.Text.Length > 0 && t.Value.DateCells is null).Key;
    byte[] wrongSlot = Mutate(bytes941, pdf =>
    {
        var ap = Fields(pdf)[combTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        ap.SetData(ShiftCombPosition(ap.GetBytes(), 3));
    });
    string? wrongSlotCode = null;
    try { VerifiedFormRenderer.VerifyEditable(wrongSlot, manifest941); } catch (VerifiedFillException ex) { wrongSlotCode = ex.Code; }
    Check(wrongSlotCode == "E_VERIFY_COMB_CELL", "reject-shift-within-widget-wrong-comb-slot");
    Reject(Mutate(bytes941, pdf =>
    {
        var ap = Fields(pdf)[textTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        ap.SetData("q /Invisible gs\n"u8.ToArray().Concat(ap.GetBytes()).Concat("\nQ"u8.ToArray()).ToArray());
    }), manifest941, "reject-unqualified-opacity-state");
    Reject(Mutate(bytes941, pdf =>
    {
        var ap = Fields(pdf)[textTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N);
        ap.SetData("q 0 0 1000 1000 re 0 0 1000 1000 re W* n\n"u8.ToArray().Concat(ap.GetBytes()).Concat("\nQ"u8.ToArray()).ToArray());
    }), manifest941, "reject-compound-even-odd-empty-clip");
    Reject(Mutate(bytes941, pdf => Fields(pdf)[textTarget].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP).GetAsStream(PdfName.N)
        .Put(PdfName.OC, new PdfDictionary())), manifest941, "reject-optional-content-visibility");
    Reject(Mutate(bytes941, pdf => Fields(pdf)[textTarget].GetWidgets()[0].GetPdfObject().Put(PdfName.OC, new PdfDictionary())),
        manifest941, "reject-widget-optional-content-visibility");
    stage = "flatten-tamper-negatives";
    var flattenedBytes = ready941Flat.Candidate!.CopyForFixture();
    var flattenedManifest = ready941Flat.Candidate.ManifestForFixture;
    Reject(Mutate(flattenedBytes, pdf =>
    {
        for (int p = 1; p <= pdf.GetNumberOfPages(); p++)
        {
            var page = pdf.GetPage(p); var preserved = new PdfArray();
            for (int i = 0; i < page.GetContentStreamCount(); i++)
            {
                var stream = page.GetContentStream(i);
                if (flattenedManifest.Pages[p - 1].ContentDigests.Contains(VerifiedFormRenderer.Digest(stream.GetBytes()), StringComparer.Ordinal)) preserved.Add(stream);
            }
            page.GetPdfObject().Put(PdfName.Contents, preserved);
        }
    }), flattenedManifest, "reject-flattened-missing-filled-paint", true);
    Reject(Mutate(flattenedBytes, pdf =>
    {
        bool changedTag = false;
        for (int p = 1; p <= pdf.GetNumberOfPages() && !changedTag; p++)
        {
            var page = pdf.GetPage(p);
            for (int i = 0; i < page.GetContentStreamCount() && !changedTag; i++)
            {
                var stream = page.GetContentStream(i); string content = Encoding.Latin1.GetString(stream.GetBytes());
                if (!content.Contains("/Form", StringComparison.Ordinal) || !content.Contains("BDC", StringComparison.Ordinal)) continue;
                stream.SetData(Encoding.Latin1.GetBytes(content.Replace("/Form", "/OC", StringComparison.Ordinal))); changedTag = true;
            }
        }
        if (!changedTag) throw new TestFailure("flatten-tag-tamper-precondition");
    }), flattenedManifest, "reject-optional-content-flatten-BDC", true);
    Reject(bytes941, manifest941, "flatten-check-rejects-editable", true);
    var changed = bytes941.ToArray(); changed[^10] ^= 1;
    Check(!ready941.Candidate.Verify(new MemoryStream(changed)), "publication-reopen-rejects-byte-substitution");

    stage = "vector-semantic-negatives";
    // Semantic vector readback is tested directly, so a digest mismatch cannot mask a geometry-policy gap.
    using (var vectorPdf = new PdfDocument(VerifiedFormRenderer.Reader(bytes941)))
    {
        foreach (var mark in new[] { CheckboxMark.Cross, CheckboxMark.Circle })
        {
            var expected = manifest941.Targets.Values.First(t => t.CheckboxMark == mark);
            var original = Fields(vectorPdf)[expected.Assignment.Field].GetWidgets()[0].GetPdfObject().GetAsDictionary(PdfName.AP)
                .GetAsDictionary(PdfName.N).GetAsStream(new PdfName(expected.OnState!));
            void VectorReject(Action<PdfStream> tamper, string name)
            {
                var stream = VectorCheckboxAppearance.Create(expected.Widget, mark, true); tamper(stream);
                bool rejected = false; try { VectorCheckboxAppearance.Verify(stream, expected, true); } catch (VerifiedFillException) { rejected = true; }
                Check(rejected, "vector-" + mark + "-" + name);
            }
            VectorCheckboxAppearance.Verify(original, expected, true);
            Check(true, "independent-vector-operator-geometry-" + mark);
            VectorReject(a => a.SetData("q Q"u8.ToArray()), "missing-paint");
            VectorReject(a => a.SetData("q 0 0 0 0 re W n\n"u8.ToArray().Concat(a.GetBytes()).Concat("Q"u8.ToArray()).ToArray()), "invisible-clip");
            VectorReject(a => a.SetData(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(a.GetBytes()).Replace(mark == CheckboxMark.Cross ? "0 G" : "0 g", mark == CheckboxMark.Cross ? "1 G" : "1 g", StringComparison.Ordinal))), "white-mark");
            VectorReject(a => a.SetData(Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(a.GetBytes()).Replace(mark == CheckboxMark.Cross ? "8 8 l" : "9.5 6 m", mark == CheckboxMark.Cross ? "10 10 l" : "10 6 m", StringComparison.Ordinal))), "wrong-geometry");
            VectorReject(a => { var resources = new PdfDictionary(); resources.Put(PdfName.Font, new PdfDictionary()); a.Put(PdfName.Resources, resources); }, "font-resource");
            VectorReject(a => a.Put(PdfName.OC, new PdfDictionary()), "optional-content");
            VectorReject(a => a.Put(PdfName.Group, new PdfDictionary()), "transparency-group");
            VectorReject(a => a.Put(PdfName.Matrix, new PdfArray(new double[] { 1, 0, 0, .1, 0, 0 })), "scaled-mark");
            VectorReject(a => a.SetData(a.GetBytes().Concat("0 0 m 10 10 l S"u8.ToArray()).ToArray()), "extra-paint");
        }
    }

    stage = "checkbox-visual-fixtures";
    // Identical text in all paired specimens isolates all 28 On/Off marks for independent dual-renderer pixels.
    foreach (var (name, seed) in new[] { ("941", ready941.Candidate!), ("schedule-b", readyB.Candidate!) })
    {
        var original = seed.ManifestForFixture; byte[] current = seed.CopyForFixture();
        Save("checkboxes." + name + ".assignments.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "pdfform.synthetic-checkbox-visual/1", templateSha256 = original.TemplateSha256,
            scope = "Appearance-only all-on/all-off states; mutually exclusive groups intentionally exercised outside public data validation.",
            placements = original.Targets.Values.Where(t => t.Assignment.Kind == "checkbox").Select(t => new
            { field = t.Assignment.Field, page = t.Widget.Page, rect = t.Widget.Rectangle, on = t.OnState, shape = t.CheckboxMark!.ToString() })
        }, new JsonSerializerOptions { WriteIndented = true }));
        for (int turn = 0; turn < 4; turn++)
        {
            bool on = turn % 2 == 0;
            var manifest = original with { Targets = original.Targets.ToDictionary(p => p.Key,
                p => p.Value with { Assignment = p.Value.Assignment with { Checked = on } }, StringComparer.Ordinal) };
            stage = $"checkbox-visual-{name}-turn-{turn}-replace";
            current = ReplaceFixture(current, manifest);
            stage = $"checkbox-visual-{name}-turn-{turn}-verify-editable";
            VerifiedFormRenderer.VerifyEditable(current, manifest);
            stage = $"checkbox-visual-{name}-turn-{turn}-flatten";
            var flatManifest = FlattenedPaintVerifier.Capture(current, manifest);
            byte[] flat = Mutate(current, pdf => { var form = PdfAcroForm.GetAcroForm(pdf, false); form.SetGenerateAppearance(false); form.FlattenFields(); });
            VerifiedFormRenderer.VerifyFlattened(flat, flatManifest);
            string basename = $"checkboxes.{name}.turn-{turn}.{(on ? "on" : "off")}";
            Save(basename + ".editable.pdf", current); Save(basename + ".flattened.pdf", flat);
            Check(true, "paired-all-checkbox-vector-transition-" + name + "-" + turn);
        }
    }

    stage = "all941-appearance-fixture";
    // Appearance-only all-target specimen: every 941 text widget has its own short numeric sentinel.
    // This intentionally bypasses tax/data consistency in the friend fixture boundary, never public Prepare.
    int sentinel = 0;
    var allTargets = manifest941.Targets.ToDictionary(p => p.Key, p => p.Value with
    {
        Assignment = p.Value.Assignment with
        {
            Text = p.Value.Assignment.Kind == "checkbox" ? "" : (++sentinel).ToString(p.Value.DateCells is null ? "D2" : "D8", CultureInfo.InvariantCulture),
            Checked = true
        }
    }, StringComparer.Ordinal);
    var allManifest = manifest941 with { Targets = allTargets };
    byte[] ReplaceFixture(byte[] input, RenderManifest desired)
    {
        return Mutate(input, pdf =>
        {
            var fonts = FixtureFonts(pdf, desired);
            VerifiedFormRenderer.ReplaceAll(pdf, desired, fonts);
        });
    }
    byte[] allPainted = ReplaceFixture(bytes941, allManifest);
    VerifiedFormRenderer.VerifyEditable(allPainted, allManifest);
    Check(sentinel == 92 && allTargets.Values.Count(t => t.Assignment.Checked && t.Assignment.Kind == "checkbox") == 24,
        "all941-92text-and24checkbox-positive-appearance-coverage");
    Save("941.all-targets.appearance-only.editable.pdf", allPainted);
    var allFlatManifest = FlattenedPaintVerifier.Capture(allPainted, allManifest);
    byte[] allFlat = Mutate(allPainted, pdf => { var form = PdfAcroForm.GetAcroForm(pdf, false); form.SetGenerateAppearance(false); form.FlattenFields(); });
    VerifiedFormRenderer.VerifyFlattened(allFlat, allFlatManifest);
    Save("941.all-targets.appearance-only.flattened.pdf", allFlat);
    var allClearedManifest = allManifest with { Targets = allTargets.ToDictionary(p => p.Key, p => p.Value with
        { Assignment = p.Value.Assignment with { Text = "", Checked = false }, DateCells = null }, StringComparer.Ordinal) };
    byte[] allCleared = ReplaceFixture(allPainted, allClearedManifest);
    VerifiedFormRenderer.VerifyEditable(allCleared, allClearedManifest);
    Check(true, "all941-nonblank-to-clear-normal-appearance-transition");
    Save("941.all-targets.cleared.pdf", allCleared);
    Save("941.all-targets.assignments.json", JsonSerializer.SerializeToUtf8Bytes(new
    {
        schema = "pdfform.synthetic-all-target-appearance/1", scope = "Appearance-only fictional sentinels; not valid tax data or public data-validation acceptance.",
        placements = allTargets.Select(p => new { field = p.Key, text = p.Value.Assignment.Text, type = p.Value.Assignment.Kind,
            page = p.Value.Widget.Page, rect = p.Value.Widget.Rectangle, selected = p.Value.Assignment.Checked })
    }, new JsonSerializerOptions { WriteIndented = true }));

    stage = "full-clear-transitions";
    // Every checkbox exercises both directions repeatedly; every field is cleared by the production replacement implementation.
    foreach (var seed in new[] { ready941.Candidate, readyB.Candidate! })
    {
        var original = seed.ManifestForFixture;
        byte[] current = seed.CopyForFixture();
        for (int turn = 0; turn < 4; turn++)
        {
            bool on = turn % 2 == 0;
            var targets = original.Targets.ToDictionary(p => p.Key, p => p.Value with
            {
                Assignment = p.Value.Assignment with { Text = on ? p.Value.Assignment.Text : "", Checked = on },
                DateCells = on ? p.Value.DateCells : null
            }, StringComparer.Ordinal);
            var manifest = original with { Targets = targets };
            current = Mutate(current, pdf =>
            {
                var fields = Fields(pdf); var fonts = FixtureFonts(pdf, manifest);
                // Add controlled stale value carriers before replacement, proving cleanup rather than omission.
                foreach (var field in fields.Values) { field.GetPdfObject().Put(PdfName.DV, new PdfString("FICTIONAL STALE")); field.GetPdfObject().Put(PdfName.RV, new PdfString("FICTIONAL STALE")); }
                VerifiedFormRenderer.ReplaceAll(pdf, manifest, fonts);
            });
            VerifiedFormRenderer.VerifyEditable(current, manifest);
            Check(true, "full-replacement-and-all-checkbox-transition-" + original.TemplateSha256[..8] + "-" + turn);
        }
        Save(original.TemplateSha256[..8] + ".cleared.pdf", current);
    }

    stage = "date-digit-fixtures";
    // Every digit shape is checked in every reviewed date cell by the renderer and independent AP-geometry readback.
    for (int digit = 0; digit <= 9; digit++)
    {
        var targets = manifest941.Targets.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        string date = targets.Single(p => p.Value.DateCells is not null).Key;
        targets[date] = targets[date] with { Assignment = targets[date].Assignment with { Text = new string((char)('0' + digit), 8) } };
        var expected = manifest941 with { Targets = targets };
        byte[] specimen = Mutate(bytes941, pdf =>
        {
            var field = Fields(pdf)[date];
            var font = NativeTextAppearance.ResolveFont(template941.Sha256, pdf, field, targets[date].Widget.Effective.Font, out _);
            field.SetValue(targets[date].Assignment.Text, font, 10);
            NativeTextAppearance.SetDateAppearance(pdf, field, targets[date], font);
        });
        VerifiedFormRenderer.VerifyEditable(specimen, expected);
        Check(true, "all-ten-digit-shapes-in-all-eight-date-cells-" + digit);
    }
    stage = "embedded-font-specimen";
    // Distributed visibility fixture remains appearance-only. Public name fixtures above separately
    // exercise the exact admitted repertoire through production data, glyph, fit and readback checks.
    CreateEmbeddedFontSpecimen(readyB.Candidate!, output);
    Check(true, "all-printable-ascii-plus-accent-specimen-AP-decoding");
    if (Directory.EnumerateFiles(output).Sum(p => new FileInfo(p).Length) > 128L * 1024 * 1024) throw new TestFailure("output-cap");
    Save("integration-report.json", JsonSerializer.SerializeToUtf8Bytes(new
    {
        schema = "pdfform.synthetic-integration-result/1", passed, failed = 0, artifacts = checkpoint,
        scope = "Bounded private development; synthetic records only. No release, filing, physical-print or supported-host acceptance."
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Verified fill integration: {passed} passed, 0 failed. Independent font-specimen raster review remains required.");
    return 0;
}
catch (TestFailure ex) { Failure("E_TEST_ASSERTION", ex.SafeName, ex); return 1; }
catch (VerifiedFillException ex) { Failure(ex.Code, null, ex); return 1; }
catch (Exception ex) { Failure("E_TEST_INTERNAL", null, ex); return 1; }

void Failure(string code, string? assertion, Exception error)
{
    // Synthetic-only diagnostics: never exception.Message/ToString, supplied values or PDF bytes.
    var report = new { schema = "pdfform.synthetic-integration-failure/1", stage, passed, code, assertion,
        exceptionType = error.GetType().FullName, codeStack = error.StackTrace,
        expectedCode = (error as TestFailure)?.ExpectedCode, actualCode = (error as TestFailure)?.ActualCode };
    try { Save("failure-report.json", JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true })); }
    catch { /* Keep the stable terminal code even if diagnostic storage itself fails. */ }
    Console.Error.WriteLine($"FAIL: {code}; stage={stage}; type={error.GetType().FullName}; assertion={assertion}");
}

static byte[] ShiftCombPosition(byte[] input, double shift)
{
    const string number = @"([-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))";
    var regex = new Regex(number + @"\s+" + number + @"\s+Td\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    string text = Encoding.Latin1.GetString(input);
    var first = regex.Match(text);
    if (!first.Success) return ChangeTextMatrices(input, 1, shift);
    double x = double.Parse(first.Groups[1].Value, CultureInfo.InvariantCulture) + shift;
    string replacement = x.ToString("0.#####", CultureInfo.InvariantCulture) + " " + first.Groups[2].Value + " Td";
    return Encoding.Latin1.GetBytes(text[..first.Index] + replacement + text[(first.Index + first.Length)..]);
}

static byte[] ChangeTextMatrices(byte[] input, double verticalScale, double shift)
{
    const string number = @"([-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+))";
    var regex = new Regex(string.Join(@"\s+", Enumerable.Repeat(number, 6)) + @"\s+Tm\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    string text = Encoding.Latin1.GetString(input); bool changed = false;
    string output = regex.Replace(text, match =>
    {
        double[] values = Enumerable.Range(1, 6).Select(i => double.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
        values[3] = verticalScale; values[4] += shift; changed = true;
        return string.Join(" ", values.Select(v => v.ToString("0.#####", CultureInfo.InvariantCulture))) + " Tm";
    });
    if (!changed) throw new TestFailure("tamper-Tm-not-found");
    return Encoding.Latin1.GetBytes(output);
}

void CreateEmbeddedFontSpecimen(VerifiedFillCandidate source, string output)
{
    var glyphs = Enumerable.Range(32, 95).Select(char.ConvertFromUtf32).Append("É").ToArray();
    var placements = new List<object>(); int index = 0;
    var original = source.ManifestForFixture;
    var targets = original.Targets.ToDictionary(pair => pair.Key, pair =>
    {
        if (pair.Value.Assignment.Kind == "checkbox") return pair.Value;
        string value = index < glyphs.Length ? glyphs[index] : "";
        if (index < glyphs.Length)
            placements.Add(new { field = pair.Key, text = value, codepoint = (int)value[0], page = pair.Value.Widget.Page,
                rect = pair.Value.Widget.Rectangle, exemptFromInk = value == " " });
        index++;
        return pair.Value with { Assignment = pair.Value.Assignment with { Text = value } };
    }, StringComparer.Ordinal);
    var manifest = original with { Targets = targets };
    byte[] editable = Mutate(source.CopyForFixture(), pdf => VerifiedFormRenderer.ReplaceAll(pdf, manifest, FixtureFonts(pdf, manifest)));
    VerifiedFormRenderer.VerifyEditable(editable, manifest);
    var flattenedManifest = FlattenedPaintVerifier.Capture(editable, manifest);
    byte[] flattened = Mutate(editable, pdf =>
    { var form = PdfAcroForm.GetAcroForm(pdf, false); form.SetGenerateAppearance(false); form.FlattenFields(); });
    VerifiedFormRenderer.VerifyFlattened(flattened, flattenedManifest);
    using (var file = new FileStream(Path.Combine(output, "font-specimen.editable.pdf"), FileMode.CreateNew)) file.Write(editable);
    using (var file = new FileStream(Path.Combine(output, "font-specimen.flattened.pdf"), FileMode.CreateNew)) file.Write(flattened);
    using (var file = new FileStream(Path.Combine(output, "font-specimen.assignments.json"), FileMode.CreateNew))
        file.Write(JsonSerializer.SerializeToUtf8Bytes(new { schema = "pdfform.synthetic-font-specimen/1", templateSha256 = original.TemplateSha256,
            font = ScheduleBEmbeddedFont.Name, fontSizePt = 10, embeddedProgramSha256 = ScheduleBEmbeddedFont.ProgramSha256,
            policy = ScheduleBEmbeddedFont.Policy, productionRepertoireUnchanged = true, repertoire = "U+0020-U+007E plus U+00C9",
            provenance = "Distributed derived-input appearance fixture: full replacement, fit, closed readback and flatten checks; public data validation is covered by separate name fixtures.", placements }, new JsonSerializerOptions { WriteIndented = true }));
}

sealed class TestFailure(string safeName, string? expectedCode = null, string? actualCode = null) : Exception
{
    internal string SafeName { get; } = safeName;
    internal string? ExpectedCode { get; } = expectedCode;
    internal string? ActualCode { get; } = actualCode;
}
sealed class TextWitness : IEventListener
{
    private readonly StringBuilder text = new();
    internal string Text => text.ToString();
    public ICollection<EventType> GetSupportedEvents() => new HashSet<EventType> { EventType.RENDER_TEXT };
    public void EventOccurred(IEventData data, EventType type) { if (type == EventType.RENDER_TEXT && data is TextRenderInfo info) text.Append(info.GetText()); }
}
