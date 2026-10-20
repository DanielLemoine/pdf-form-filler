// Test-only early dispatcher for the existing PdfForm.Cli.Tests project.
// The admitted outer owner supplies path/native-identity checks and resource limits.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PdfForm.StandaloneSyntheticFixtures;

public static class FixtureTestMode
{
    private const int FontBytes = 393692;
    private const string FontHash = "9caef765d2e891c10dd73658894f01e660fe0c1c83e0bda7d6edf561d8f623d4";

    // Test-only argv: --test-only-third-fixture FONT_PATH VARIANT OUTPUT_PATH
    public static bool TryHandle(string[] args, TextWriter stdout, out int exitCode)
    {
        exitCode = 2;
        if (args.Length == 0 || args[0] != "--test-only-third-fixture") return false;
        bool createAttempted = false, created = false, complete = false;
        try
        {
            // All lexical checks precede file access. Numeric enum aliases are not commands.
            if (args.Length != 4 || !IsCanonicalLocalPath(args[1]) || !IsCanonicalLocalPath(args[3]) ||
                Path.GetFileName(args[1]) != "LiberationSerif-Regular.ttf" ||
                !Enum.TryParse<WorkshopVariant>(args[2], false, out var variant) ||
                !Enum.IsDefined(typeof(WorkshopVariant), variant) || variant.ToString() != args[2])
                throw new InvalidOperationException();
            string fileName = Path.GetFileName(args[3]);
            if (fileName != ThirdWorkshopFixtureGenerator.SuggestedFileName(variant) &&
                !(variant == WorkshopVariant.Blank && fileName == "third-form.pdf"))
                throw new InvalidOperationException();

            byte[] font = new byte[FontBytes];
            using (var input = new FileStream(args[1], FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length != FontBytes) throw new InvalidOperationException();
                input.ReadExactly(font);
                if (input.ReadByte() != -1 || input.Length != FontBytes) throw new InvalidOperationException();
            }
            if (Convert.ToHexStringLower(SHA256.HashData(font)) != FontHash) throw new InvalidOperationException();
            byte[] pdf = ThirdWorkshopFixtureGenerator.Generate(new(font, FontHash, "root-approved-SIL-OFL-1.1"), variant);
            if (pdf.Length is < 1 or > 4 * 1024 * 1024) throw new InvalidOperationException();
            string expectedHash = Convert.ToHexStringLower(SHA256.HashData(pdf));

            // Parent must already exist. Never create a directory, overwrite or delete a file.
            createAttempted = true;
            using (var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                output.Write(pdf);
                output.Flush(true);
            }
            complete = true;
            stdout.WriteLine(JsonSerializer.Serialize(new { schema = "third-fixture-test-mode/1", status = "created",
                variant = variant.ToString(), fixtureName = fileName, bytes = pdf.Length, sha256 = expectedHash }));
            stdout.Flush();
            exitCode = 0;
        }
        catch
        {
            exitCode = createAttempted ? 4 : 2;
            try
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { schema = "third-fixture-test-mode/1", status = "failed",
                    code = "E_FIXTURE_TEST_MODE", artifactState = complete ? "published" : created ? "staged" : createAttempted ? "unknown" : "none" }));
                stdout.Flush();
            }
            catch { } // The nonzero exit survives diagnostic failure; any output remains intact.
        }
        return true;
    }

    private static bool IsCanonicalLocalPath(string path)
    {
        if (path.Length is < 1 or > 4096 || path.Contains('\0') || !Path.IsPathFullyQualified(path)) return false;
        if (OperatingSystem.IsWindows() &&
            (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' || path.AsSpan(2).Contains(':')))
            return false; // No UNC/device/drive-relative roots or alternate data streams.
        return string.Equals(path, Path.GetFullPath(path), StringComparison.Ordinal);
    }
}
