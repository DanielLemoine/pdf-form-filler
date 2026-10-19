using System.Text;
using PdfForm.Pdf;

internal static class TraversalBudgetTests
{
    internal static void Run(Action<bool, string> check)
    {
        TemplateInspection Inspect(byte[] bytes, int limit) => new PdfTemplateInspector(limit).Inspect(TemplateSnapshot.FromBytes(bytes));
        bool Reached(TemplateInspection report) => report.Pages == 2 && report.Findings.All(f => f.Code != "E_PDF_LIMIT");
        bool Has(TemplateInspection report, string code) => report.Findings.Any(f => f.Code == code);
        void Limited(byte[] bytes, int limit, string label)
        {
            var report = Inspect(bytes, limit);
            check(report.Findings.Count == 1 && Has(report, "E_PDF_LIMIT") && !report.InitialStructureSupported
                && report.TerminalFieldCount == 0 && report.WidgetCount == 0, label);
        }

        foreach (int limit in new[] { -1, 0, 30_001, int.MaxValue })
        {
            bool rejected = false;
            try { _ = new PdfTemplateInspector(limit); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "test seam cannot disable or enlarge traversal budget: " + limit);
        }

        // A merged field visited canonically and on its page consumes two entries, not one distinct object.
        var sharedWidget = Inspect(Graph(), 2);
        check(Reached(sharedWidget) && sharedWidget.TerminalFieldCount == 1 && sharedWidget.WidgetCount == 1,
            "combined canonical and annotation exact boundary");
        Limited(Graph(), 1, "combined canonical and annotation one over boundary");

        const string childWidget = "<< /Subtype /Widget /Parent 5 0 R /Rect [0 0 10 10] >>";
        string Children(string kids) => $"<< /FT /Tx /T (amount) /Kids [{kids}] >>";
        var directChildren = Inspect(Graph(field: Children(Repeat(childWidget, 2)), pageOne: ""), 3);
        check(Reached(directChildren) && directChildren.TerminalFieldCount == 1 && directChildren.WidgetCount == 2,
            "direct widget children count at exact boundary");
        Limited(Graph(field: Children(Repeat(childWidget, 3)), pageOne: ""), 3,
            "direct widget children one over boundary");
        var invalidChildren = Inspect(Graph(field: Children("null null"), pageOne: ""), 3);
        check(Reached(invalidChildren) && Has(invalidChildren, "E_FIELD_TREE"), "invalid children count at exact boundary");
        Limited(Graph(field: Children("null null null"), pageOne: ""), 3, "invalid children one over boundary");

        var invalidRoots = Inspect(Graph(roots: "null null null", pageOne: ""), 3);
        check(Reached(invalidRoots) && Has(invalidRoots, "E_FIELD_TREE"), "invalid roots count at exact boundary");
        Limited(Graph(roots: "null null null null", pageOne: ""), 3, "invalid roots one over boundary");
        var repeatedRoots = Inspect(Graph(roots: "5 0 R 5 0 R"), 3);
        check(Reached(repeatedRoots) && Has(repeatedRoots, "E_FIELD_CYCLE") && repeatedRoots.TerminalFieldCount == 1,
            "duplicate field roots remain invalid and count at exact boundary");
        Limited(Graph(roots: "5 0 R 5 0 R"), 2, "duplicate field roots consume budget");
        var fieldCycle = Inspect(Graph(field: Children("5 0 R"), pageOne: ""), 2);
        check(Reached(fieldCycle) && Has(fieldCycle, "E_FIELD_CYCLE"), "canonical field cycle terminates at exact boundary");
        Limited(Graph(field: Children("5 0 R"), pageOne: ""), 1, "canonical cycle entry consumes budget");
        var repeatedChildren = Graph(field: Children("8 0 R 8 0 R"), pageOne: "8 0 R", extra: [childWidget]);
        var repeatedChildrenReport = Inspect(repeatedChildren, 4);
        check(Reached(repeatedChildrenReport) && Has(repeatedChildrenReport, "E_WIDGET_DUPLICATE"),
            "duplicate child widgets remain invalid at exact boundary");
        Limited(repeatedChildren, 3, "duplicate child widgets consume budget");

        const string annotation = "<< /Subtype /Text /Rect [0 0 10 10] >>";
        var annotationBoundary = Inspect(Graph(roots: "", pageOne: Repeat(annotation, 2), pageTwo: Repeat(annotation, 2)), 4);
        check(Reached(annotationBoundary), "annotation aggregate exact boundary across pages");
        Limited(Graph(roots: "", pageOne: Repeat(annotation, 2), pageTwo: Repeat(annotation, 3)), 4,
            "annotation aggregate one over despite individually bounded pages");
        check(Reached(Inspect(Graph(roots: "", pageOne: "null null", pageTwo: "null null"), 4)),
            "invalid annotations count at aggregate exact boundary");
        Limited(Graph(roots: "", pageOne: "null null", pageTwo: "null null null"), 4,
            "invalid annotations count at aggregate one over boundary");
        var repeatedAnnotation = Inspect(Graph(pageOne: "5 0 R 5 0 R"), 3);
        check(Reached(repeatedAnnotation) && Has(repeatedAnnotation, "E_WIDGET_DUPLICATE"),
            "duplicate page widgets remain invalid at exact boundary");
        Limited(Graph(pageOne: "5 0 R 5 0 R"), 2, "duplicate page widgets consume budget");

        var emptyNodes = Inspect(NameTree("<< /Kids [<< >> << >>] >>"), 3);
        check(Reached(emptyNodes) && emptyNodes.CatalogJavaScriptCount == 1 && Has(emptyNodes, "E_ACTIVE_CONTENT"),
            "empty name-tree nodes count at exact node boundary without granting action support");
        Limited(NameTree("<< /Kids [<< >> << >> << >>] >>"), 3, "empty name-tree nodes one over boundary");
        var malformedNodes = Inspect(NameTree("<< /Kids [null null] >>"), 3);
        check(Reached(malformedNodes) && malformedNodes.CatalogJavaScriptCount == 2,
            "malformed name-tree nodes count at exact node boundary");
        Limited(NameTree("<< /Kids [null null null] >>"), 3, "malformed name-tree nodes one over boundary");
        Limited(NameTree("<< /Kids [8 0 R 8 0 R] >>", "<< >>"), 3, "duplicate name-tree child fails closed");
        Limited(NameTree("8 0 R", "<< /Kids [8 0 R] >>"), 3, "name-tree cycle fails closed");

        var namesBoundary = Inspect(NameTree($"<< /Names [{Pairs(3)}] >>"), 3);
        check(Reached(namesBoundary) && namesBoundary.CatalogJavaScriptCount == 3, "name entries exact boundary");
        Limited(NameTree($"<< /Names [{Pairs(4)}] >>"), 3, "name entries one over boundary");
        var aggregateNames = Inspect(NameTree($"<< /Kids [<< /Names [{Pairs(2)}] >> << /Names [{Pairs(2)}] >>] >>"), 4);
        check(Reached(aggregateNames) && aggregateNames.CatalogJavaScriptCount == 4, "name entries aggregate exact boundary");
        Limited(NameTree($"<< /Kids [<< /Names [{Pairs(2)}] >> << /Names [{Pairs(3)}] >>] >>"), 4,
            "name entries aggregate one over despite individually bounded leaves");
        var mixedNames = Inspect(NameTree($"<< /Names [{Pairs(2)}] /Kids [null] >>"), 3);
        check(Reached(mixedNames) && mixedNames.CatalogJavaScriptCount == 3, "malformed node charges shared name-entry budget");
        Limited(NameTree($"<< /Names [{Pairs(3)}] /Kids [null] >>"), 3, "malformed node exceeds shared name-entry budget");
        var oddNames = Inspect(NameTree("<< /Names [(a) << >> (b)] >>"), 2);
        check(Reached(oddNames) && oddNames.CatalogJavaScriptCount == 2, "odd name array counts unfinished pair conservatively");
        Limited(NameTree("<< /Names [(a) << >> (b) << >> (c)] >>"), 2, "odd name array one over boundary");
        check(Reached(Inspect(NameTreeChain(64), 65)), "empty name tree at depth64 remains bounded and inspectable");
        Limited(NameTreeChain(65), 66, "empty name tree at depth65 fails closed");
    }

    private static string Repeat(string value, int count) => string.Join(" ", Enumerable.Repeat(value, count));
    private static string Pairs(int count) => string.Join(" ", Enumerable.Range(0, count)
        .Select(i => $"(script-{i}) << /S /JavaScript /JS (synthetic-no-execution) >>"));
    private static byte[] NameTree(string tree, params string[] nodes) => Graph(roots: "", pageOne: "",
        catalog: $"/Names << /JavaScript {tree} >>", extra: nodes);
    private static byte[] NameTreeChain(int depth) => NameTree("8 0 R", Enumerable.Range(0, depth + 1)
        .Select(i => i == depth ? "<< >>" : $"<< /Kids [{9 + i} 0 R] >>").ToArray());

    // Raw, tiny fixtures retain malformed/repeated references that a high-level PDF writer might normalize.
    // Objects8+ are supplied explicitly; every fixture has two pages and contains only synthetic content.
    private static byte[] Graph(string roots = "5 0 R", string? field = null, string pageOne = "5 0 R",
        string pageTwo = "", string catalog = "", string[]? extra = null)
    {
        var objects = new List<string>
        {
            $"<< /Type /Catalog /Pages 2 0 R /AcroForm 3 0 R {catalog} >>",
            "<< /Type /Pages /Kids [6 0 R 7 0 R] /Count 2 >>",
            $"<< /Fields [{roots}] /DA (/Helvetica 10 Tf) /DR << /Font << /Helvetica 4 0 R >> >> >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            field ?? "<< /Subtype /Widget /FT /Tx /T (amount) /Rect [0 0 10 10] >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [{pageOne}] >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [{pageTwo}] >>"
        };
        if (extra is not null) objects.AddRange(extra);
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var positions = new List<long> { 0 };
        for (int i = 0; i < objects.Count; ++i)
        {
            positions.Add(output.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long position in positions.Skip(1)) Write($"{position:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
}
