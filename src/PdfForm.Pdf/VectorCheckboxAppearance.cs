using System.Text;
using iText.Kernel.Pdf;
using PdfForm.Core;
using static PdfForm.Pdf.VerifiedFormRenderer;

namespace PdfForm.Pdf;

internal enum CheckboxMark { Cross, Circle }

/// <summary>Reviewed resource-free marks for 28 exact-template targets, not a general checkbox style API.</summary>
internal static class VectorCheckboxAppearance
{
    internal static CheckboxMark ResolveConfiguredMark(string token) => token switch
    {
        "cross-10-v1" => CheckboxMark.Cross,
        "circle-12-v1" => CheckboxMark.Circle,
        _ => throw new VerifiedFillException("E_CHECKBOX_VECTOR_STRATEGY", "The configured checkbox mark is unsupported.", 3)
    };

    private sealed record Target(string On, int Page, CheckboxMark Mark);
    private static readonly IReadOnlyDictionary<string, Target> Form941Targets = Form941();
    private static readonly IReadOnlyDictionary<string, Target> ScheduleBTargets = Enumerable.Range(0, 4)
        .ToDictionary(i => $"topmostSubform[0].Page1[0].c1_1[{i}]", i => new Target((i + 1).ToString(), 1, CheckboxMark.Cross), StringComparer.Ordinal);

    internal static CheckboxMark Resolve(string templateSha256, string field, string on, WidgetInspection widget)
    {
        var targets = templateSha256 == ApprovedTemplates.Form941Sha256 ? Form941Targets
            : templateSha256 == ApprovedTemplates.ScheduleBSha256 ? ScheduleBTargets : null;
        Require(ProductionProfileRegistry.AdoptionScope == "bounded_private_development" && targets is not null
            && targets.TryGetValue(field, out _), "E_CHECKBOX_VECTOR_SCOPE", "Vector checkbox rendering is restricted to exact reviewed targets.", 3);
        var target = targets![field];
        double size = target.Mark == CheckboxMark.Circle ? 12 : 10;
        Require(target.On == on && target.Page == widget.Page && widget.Rotation == 0
            && Math.Abs(widget.Rectangle[2] - widget.Rectangle[0] - size) < .002
            && Math.Abs(widget.Rectangle[3] - widget.Rectangle[1] - size) < .002,
            "E_CHECKBOX_VECTOR_GEOMETRY", "The checkbox export state or widget geometry is outside the reviewed policy.", 3);
        return target.Mark;
    }

    // Generic entry consumes already parsed, exact target facts from the sealed
    // configuration. Supported painting families remain the reviewed 10x10
    // cross and 12x12 circle; no geometry scaling or inferred mark is allowed.
    internal static CheckboxMark ResolveBound(string templateSha256, string configuredTemplateSha256,
        string field, string configuredTarget, string on, string configuredOn, int configuredPage,
        IReadOnlyList<double> configuredRectangle, WidgetInspection widget, CheckboxMark mark)
    {
        Require(templateSha256 == configuredTemplateSha256 && field == configuredTarget && on == configuredOn
            && !string.IsNullOrEmpty(on) && on != "Off" && configuredPage > 0 && widget.Page == configuredPage
            && widget.OnStates.Contains(on, StringComparer.Ordinal)
            && configuredRectangle.Count == 4 && configuredRectangle.All(double.IsFinite)
            && widget.Rectangle.SequenceEqual(configuredRectangle),
            "E_CHECKBOX_VECTOR_SCOPE", "Checkbox policy does not match the exact template, target, state, page and rectangle.", 3);
        RequireSupportedGeometry(widget, mark);
        return mark;
    }

    internal static void RequireSupportedGeometry(WidgetInspection widget, CheckboxMark mark)
    {
        double size = mark == CheckboxMark.Circle ? 12 : 10;
        Require(mark is CheckboxMark.Cross or CheckboxMark.Circle && widget.GeometryValid && widget.Rotation == 0
            && widget.Rectangle.Count == 4 && widget.Rectangle.All(double.IsFinite)
            && Math.Abs(widget.Rectangle[2] - widget.Rectangle[0] - size) < .002
            && Math.Abs(widget.Rectangle[3] - widget.Rectangle[1] - size) < .002,
            "E_CHECKBOX_VECTOR_GEOMETRY", "Only the reviewed 10x10 cross and 12x12 circle geometries are supported.", 3);
    }

    internal static PdfStream Create(WidgetInspection widget, CheckboxMark mark, bool on)
    {
        RequireSupportedGeometry(widget, mark);
        // Literal authoring is deliberately separate from semantic command/operand verification below.
        string content = !on ? "q\nQ\n" : mark == CheckboxMark.Cross
            ? "q\n0 G\n0 g\n1.25 w\n0 J\n0 j\n2 2 m\n8 8 l\nS\n2 8 m\n8 2 l\nS\nQ\n"
            : CircleContent();
        var stream = new PdfStream(Encoding.ASCII.GetBytes(content));
        stream.Put(PdfName.Type, PdfName.XObject);
        stream.Put(PdfName.Subtype, PdfName.Form);
        stream.Put(PdfName.FormType, new PdfNumber(1));
        stream.Put(PdfName.BBox, new PdfArray(new double[] { 0, 0, mark == CheckboxMark.Circle ? 12 : 10, mark == CheckboxMark.Circle ? 12 : 10 }));
        stream.Put(PdfName.Matrix, new PdfArray(new double[] { 1, 0, 0, 1, 0, 0 }));
        return stream;
    }

    internal static void Verify(PdfStream stream, TargetWitness target, bool on)
    {
        Require(target.CheckboxMark is not null, "E_VERIFY_CHECKBOX_POLICY", "The checkbox mark policy is missing.");
        VerifyAppearanceBox(stream, target.Widget);
        Require(PdfName.XObject.Equals(stream.GetAsName(PdfName.Type)) && PdfName.Form.Equals(stream.GetAsName(PdfName.Subtype))
            && stream.GetAsNumber(PdfName.FormType)?.DoubleValue() == 1
            && stream.GetAsArray(PdfName.Matrix)?.ToDoubleArray().SequenceEqual(new double[] { 1, 0, 0, 1, 0, 0 }) == true,
            "E_VERIFY_CHECKBOX_DICTIONARY", "The vector checkbox appearance dictionary is unsupported.");
        double size = target.CheckboxMark == CheckboxMark.Circle ? 12 : 10;
        Require(stream.GetAsArray(PdfName.BBox)?.ToDoubleArray().SequenceEqual(new double[] { 0, 0, size, size }) == true,
            "E_VERIFY_CHECKBOX_BBOX", "The vector checkbox bounding box is not the exact reviewed size.");
        var allowed = new HashSet<PdfName> { PdfName.Type, PdfName.Subtype, PdfName.FormType, PdfName.BBox, PdfName.Matrix,
            PdfName.Length, PdfName.Filter, PdfName.DecodeParms, PdfName.Resources };
        Require(stream.KeySet().All(allowed.Contains) && (!stream.ContainsKey(PdfName.Resources)
            || stream.GetAsDictionary(PdfName.Resources)?.Size() == 0),
            "E_VERIFY_CHECKBOX_RESOURCES", "Vector checkbox marks cannot contain fonts, resources or visibility controls.");
        var commands = GeneratedContentPolicy.Commands(stream.GetBytes()).ToArray();
        int index = 0;
        void Command(string op, params double[] numbers)
        {
            Require(index < commands.Length, "E_VERIFY_CHECKBOX_VECTOR", "A required checkbox vector command is missing.");
            var args = commands[index++];
            Require(args.Count == numbers.Length + 1 && args[^1].ToString() == op,
                "E_VERIFY_CHECKBOX_VECTOR", "A checkbox vector command is outside the reviewed operator sequence.");
            for (int i = 0; i < numbers.Length; i++)
                Require(args[i] is PdfNumber n && double.IsFinite(n.DoubleValue()) && Math.Abs(n.DoubleValue() - numbers[i]) < .000001,
                    "E_VERIFY_CHECKBOX_VECTOR", "A checkbox vector operand differs from the reviewed geometry.");
        }
        Command("q");
        if (on && target.CheckboxMark == CheckboxMark.Cross)
        {
            Command("G", 0); Command("g", 0); Command("w", 1.25); Command("J", 0); Command("j", 0);
            Command("m", 2, 2); Command("l", 8, 8); Command("S");
            Command("m", 2, 8); Command("l", 8, 2); Command("S");
        }
        else if (on) VerifyCircle(Command);
        Command("Q");
        Require(index == commands.Length, "E_VERIFY_CHECKBOX_VECTOR", "The checkbox vector appearance contains extra commands.");
    }

    // Four cubic arcs approximate the adopted radius-3.5 circle, centered at (6,6), without any font.
    private static string CircleContent() => "q\n0 g\n9.5 6 m\n9.5 7.93299662 7.93299662 9.5 6 9.5 c\n4.06700338 9.5 2.5 7.93299662 2.5 6 c\n2.5 4.06700338 4.06700338 2.5 6 2.5 c\n7.93299662 2.5 9.5 4.06700338 9.5 6 c\nh\nf\nQ\n";
    private static void VerifyCircle(Action<string, double[]> command)
    {
        const double center = 6, radius = 3.5, kappa = .5522847498307936;
        double control = radius * kappa, low = center - radius, high = center + radius;
        command("g", [0]); command("m", [high, center]);
        command("c", [high, center + control, center + control, high, center, high]);
        command("c", [center - control, high, low, center + control, low, center]);
        command("c", [low, center - control, center - control, low, center, low]);
        command("c", [center + control, low, high, center - control, high, center]);
        command("h", []); command("f", []);
    }

    private static Dictionary<string, Target> Form941()
    {
        var result = new Dictionary<string, Target>(StringComparer.Ordinal);
        void Add(string prefix, int count, int page, CheckboxMark mark = CheckboxMark.Cross)
        { for (int i = 0; i < count; i++) result.Add($"topmostSubform[0].{prefix}[{i}]", new((i + 1).ToString(), page, mark)); }
        Add("Page1[0].Header[0].ReportForQuarter[0].c1_1", 4, 1);
        Add("Page1[0].Header[0].AggregateReturn[0].c1_2", 3, 1);
        Add("Page1[0].c1_3", 1, 1); Add("Page1[0].c1_4", 2, 1); Add("Page1[0].c1_5", 2, 1);
        Add("Page2[0].c2_1", 3, 2); Add("Page2[0].c2_2", 1, 2); Add("Page2[0].c2_3", 1, 2);
        Add("Page2[0].c2_4", 2, 2); Add("Page2[0].c2_5", 1, 2);
        Add("Page3[0].Line3_ReadOrder[0].c4_1", 4, 3, CheckboxMark.Circle);
        return result;
    }
}
