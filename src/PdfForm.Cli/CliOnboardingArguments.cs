using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using PdfForm.Core;

namespace PdfForm.Cli;

public enum CliOnboardingOperation { Check, Configure, SaveConfig, SampleInput, CatalogFill, CatalogValidate }

// Parsed paths stay in the command adapter. They are never diagnostic payloads or configuration authority.
public sealed record CliOnboardingRequest(CliOnboardingOperation Operation,
    [property: JsonIgnore] string? Pdf, [property: JsonIgnore] string? Catalog,
    [property: JsonIgnore] string? Draft, [property: JsonIgnore] string? Data,
    [property: JsonIgnore] string? Output, bool Flatten = false, bool Reviewed = false);

public sealed record CliOnboardingParse(bool Handled, CliOnboardingRequest? Request, int ExitCode,
    string Status, Finding? Finding = null);

public static class CliOnboardingArguments
{
    public static CliOnboardingParse Parse(IReadOnlyList<string> supplied)
    {
        ArgumentNullException.ThrowIfNull(supplied);
        if (supplied.Count == 0) return Unhandled();
        string command = supplied[0];
        bool catalogSelection = supplied.Any(a => a == "--catalog");
        bool handled = command is "check" or "configure" or "save-config" or "sample-input" ||
            command is "fill" or "validate" && catalogSelection;
        if (!handled) return Unhandled();
        if (supplied.Count > 32 || supplied.Any(a => a is null || a.Length > 4096)) return Invalid();
        var args = supplied.Where(a => a != "--verbose").ToArray();
        if (args.Contains("--force", StringComparer.Ordinal))
            return command == "check" ? Invalid()
                : Failure(3, "unsupported", "E_FORCE_UNQUALIFIED", "Replacement publication is not qualified.");

        bool positionalPdf = command is "check" or "configure" or "save-config" or "fill";
        int firstOption = positionalPdf ? 2 : 1;
        string? pdf = null;
        if (positionalPdf)
        {
            if (args.Length < 2 || !PathValue(args[1])) return Invalid();
            pdf = args[1];
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool reviewed = false, flatten = false;
        for (int i = firstOption; i < args.Length; i++)
        {
            string option = args[i];
            if (option == "--reviewed" && command == "save-config")
            { if (reviewed) return Invalid(); reviewed = true; continue; }
            if (option == "--flatten" && command == "fill")
            { if (flatten) return Invalid(); flatten = true; continue; }
            bool allowed = command switch
            {
                "check" => option == "--catalog",
                "configure" => option == "--out",
                "save-config" => option is "--draft" or "--out",
                "sample-input" => option is "--catalog" or "--out",
                "fill" => option is "--catalog" or "--data" or "--out",
                "validate" => option is "--catalog" or "--data" or "--pdf",
                _ => false
            };
            if (!allowed || i + 1 >= args.Length || !PathValue(args[i + 1]) || !values.TryAdd(option, args[++i])) return Invalid();
        }
        string? Get(string key) => values.GetValueOrDefault(key);
        bool complete = command switch
        {
            "check" => true,
            "configure" => Get("--out") is not null,
            "save-config" => Get("--draft") is not null && Get("--out") is not null,
            "sample-input" => Get("--catalog") is not null && Get("--out") is not null,
            "fill" => Get("--catalog") is not null && Get("--data") is not null && Get("--out") is not null,
            "validate" => Get("--catalog") is not null && Get("--data") is not null,
            _ => false
        };
        if (!complete) return Invalid();
        if (command == "save-config" && !reviewed)
            return Failure(1, "configuration_needed", "E_CONFIGURATION_REVIEW_REQUIRED", "Review the draft and supply --reviewed before saving configuration.");
        var operation = command switch
        {
            "check" => CliOnboardingOperation.Check,
            "configure" => CliOnboardingOperation.Configure,
            "save-config" => CliOnboardingOperation.SaveConfig,
            "sample-input" => CliOnboardingOperation.SampleInput,
            "fill" => CliOnboardingOperation.CatalogFill,
            _ => CliOnboardingOperation.CatalogValidate
        };
        return new(true, new(operation, pdf ?? Get("--pdf"), Get("--catalog"), Get("--draft"), Get("--data"), Get("--out"), flatten, reviewed), 0, "parsed");
    }

    private static bool PathValue(string value) => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("--", StringComparison.Ordinal);
    private static CliOnboardingParse Unhandled() => new(false, null, 0, "not_handled");
    private static CliOnboardingParse Invalid() => Failure(2, "invalid", "E_ARGUMENTS", "Command arguments are missing, duplicated or unsupported.");
    private static CliOnboardingParse Failure(int exit, string status, string code, string message) => new(true, null, exit, status, new(code, "syntax", message));
}

public static class CliDraftKeys
{
    private static readonly UTF8Encoding ExactUtf8 = new(false, true);

    public static string FromCanonicalField(string canonicalField)
    {
        ArgumentException.ThrowIfNullOrEmpty(canonicalField);
        if (canonicalField.Length > 4096) throw new ArgumentException("Canonical field exceeds the supported name bound.", nameof(canonicalField));
        // No display-label, business-meaning, case, whitespace or Unicode-normalization inference.
        return "field_" + Convert.ToHexStringLower(SHA256.HashData(ExactUtf8.GetBytes(canonicalField)));
    }

    public static IReadOnlyDictionary<string, string> Build(IEnumerable<string> canonicalFields)
    {
        ArgumentNullException.ThrowIfNull(canonicalFields);
        var names = canonicalFields.Take(Limits.MaxKeysOrTargets + 1).ToArray();
        if (names.Length > Limits.MaxKeysOrTargets) throw new ArgumentException("Canonical field count exceeds the supported bound.", nameof(canonicalFields));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in names.Order(StringComparer.Ordinal))
            if (!result.TryAdd(FromCanonicalField(name), name))
                throw new ArgumentException("Duplicate or conflicting canonical field identity.", nameof(canonicalFields));
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result);
    }
}
