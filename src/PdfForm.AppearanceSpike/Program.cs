using System.Text.Json;

namespace PdfForm.AppearanceSpike;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static int Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--fixture-root" || args[2] != "--out")
        {
            Console.Error.WriteLine("Usage: PdfForm.AppearanceSpike --fixture-root <appearance-fixtures> --out <new-directory>");
            return 2;
        }
        var fixtureRoot = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) || File.Exists(output))
        {
            Console.Error.WriteLine("E_SPIKE_OUTPUT_EXISTS");
            return 2;
        }
        try
        {
            var manifest = JsonSerializer.Deserialize<FixtureManifest>(
                File.ReadAllText(Path.Combine(fixtureRoot, "fixture-manifest.json")), JsonOptions)
                ?? throw new SpikeFailure("E_FIXTURE_MANIFEST");
            if (manifest.Schema != "pdfform.appearance-spike-fixture/1") throw new SpikeFailure("E_FIXTURE_SCHEMA");
            Directory.CreateDirectory(output);
            var runner = new AppearanceRunner(fixtureRoot, output, manifest);
            var report = runner.Run();
            File.WriteAllText(Path.Combine(output, "spike-report.json"), JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine($"Appearance spike: {report.Decision}; {report.Checks.Count(c => c.Status == "pass")} passed, {report.Checks.Count(c => c.Status == "fail")} failed. See spike-report.json.");
            return report.Checks.Any(c => c.Status == "fail") ? 4 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex is SpikeFailure sf ? sf.Code : "E_SPIKE_UNEXPECTED");
            return 4;
        }
    }
}
