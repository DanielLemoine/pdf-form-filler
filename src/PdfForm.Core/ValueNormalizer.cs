using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PdfForm.Core;

public sealed record NormalizationResult(NormalizedValue Value, IReadOnlyList<Finding> Findings);

public static class ValueNormalizer
{
    public static NormalizationResult Normalize(KeyDefinition key, JsonElement input, bool supplied)
    {
        var findings = new List<Finding>();
        void Error(string code, string message) => findings.Add(new(code, "data", message, key.Key));
        if (!supplied) return new(new(key.Kind, ValuePresence.Missing, true, Boolean: key.Kind == "checkbox" ? false : null), []);
        if (key.Kind == "checkbox" && input.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new(new(key.Kind, ValuePresence.Supplied, true, Boolean: input.GetBoolean()), []);
        if (input.ValueKind != JsonValueKind.String)
            return new(new(key.Kind, ValuePresence.Supplied, false), [new("E_VALUE_TYPE", "data", "Value must use the declared string or Boolean type.", key.Key)]);
        string value = input.GetString()!;
        if (value.Length == 0) return new(new(key.Kind, ValuePresence.Blank, true, Boolean: key.Kind == "checkbox" ? false : null), []);
        if (value.EnumerateRunes().Count() > Limits.MaxTextScalars)
            return new(new(key.Kind, ValuePresence.Supplied, false), [new("E_TEXT_LIMIT", "data", "Value exceeds the scalar length limit.", key.Key)]);
        if (key.Strip is { } strip) value = string.Concat(value.EnumerateRunes().Where(c => !strip.EnumerateRunes().Contains(c)).Select(c => c.ToString()));
        if (key.Transform == "upper") value = value.ToUpperInvariant();
        // A normalized empty string still represents a clear, not an invented value.
        if (value.Length == 0) return new(new(key.Kind, ValuePresence.Blank, true, Boolean: key.Kind == "checkbox" ? false : null), []);
        int length = value.EnumerateRunes().Count();
        if (length > Math.Min(Limits.MaxTextScalars, key.MaxLength ?? Limits.MaxTextScalars)) Error("E_TEXT_LENGTH", "Value exceeds its declared length limit.");
        if (key.Pattern is { } pattern)
        {
            try { if (!ProfileParser.Match(value, pattern)) Error("E_PATTERN", "Value does not match the required format."); }
            catch (RegexMatchTimeoutException) { Error("E_PATTERN_TIMEOUT", "Value pattern exceeded its execution budget."); }
        }
        long? cents = null; DateOnly? date = null; bool? boolean = null;
        switch (key.Kind)
        {
            case "money":
                if (!TryParseCents(value, out long amount)) Error("E_MONEY", "Money must use exact ASCII decimal syntax.");
                else if (amount < 0 && key.AllowNegative != true) Error("E_MONEY_NEGATIVE", "This amount cannot be negative.");
                else cents = amount;
                break;
            case "date":
                if (!ProfileParser.Match(value, "[0-9]{4}-[0-9]{2}-[0-9]{2}") || !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    Error("E_DATE", "Date must be an actual Gregorian day in yyyy-MM-dd format.");
                else date = parsedDate;
                break;
            case "checkbox":
                if (value == "true") boolean = true;
                else if (value == "false") boolean = false;
                else Error("E_CHECKBOX", "Checkbox string must be exactly true or false.");
                break;
            case "oneOf":
                if (key.Options is null || !key.Options.ContainsKey(value)) Error("E_OPTION", "Value is not a declared option.");
                break;
            case "split":
                if (key.Parts is null || length != key.Parts.Sum()) Error("E_SPLIT_LENGTH", "Value length does not match split components.");
                break;
        }
        return new(new(key.Kind, ValuePresence.Supplied, findings.Count == 0, value, cents, date, boolean), findings.ToArray());
    }

    public static bool TryParseCents(string value, out long cents)
    {
        cents = 0;
        if (!ProfileParser.Match(value, "-?[0-9]{1,13}(\\.[0-9]{1,2})?")) return false;
        bool negative = value[0] == '-';
        var unsigned = negative ? value[1..] : value;
        var parts = unsigned.Split('.');
        long dollars = long.Parse(parts[0], CultureInfo.InvariantCulture);
        long fraction = parts.Length == 1 ? 0 : int.Parse(parts[1].PadRight(2, '0'), CultureInfo.InvariantCulture);
        cents = checked(dollars * 100 + fraction);
        if (negative && cents != 0) cents = -cents;
        return true;
    }

    public static (string Dollars, string Cents) FormatMoney(long cents, bool thousands, string zero)
    {
        if (cents == 0 && zero == "blank") return ("", "");
        ulong absolute = cents < 0 ? (ulong)(-(cents + 1)) + 1 : (ulong)cents;
        string dollars = (absolute / 100).ToString(thousands ? "N0" : "0", CultureInfo.InvariantCulture);
        if (cents < 0) dollars = "-" + dollars;
        return (dollars, (absolute % 100).ToString("00", CultureInfo.InvariantCulture));
    }
    public static bool Equivalent(NormalizedValue left, NormalizedValue right)
    {
        if (!left.IsValid || !right.IsValid || left.Kind != right.Kind) return false;
        if (!left.IsPopulated || !right.IsPopulated) return !left.IsPopulated && !right.IsPopulated;
        return left.Kind switch
        {
            "money" => left.Cents == right.Cents,
            "date" => left.Date == right.Date,
            "checkbox" => left.Boolean == right.Boolean,
            _ => string.Equals(left.Text, right.Text, StringComparison.Ordinal)
        };
    }
}
