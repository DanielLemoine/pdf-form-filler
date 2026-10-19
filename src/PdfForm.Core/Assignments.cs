using System.Globalization;
using System.Text;

namespace PdfForm.Core;

/// <summary>Payload-only in-memory manifest. Never serialize this to diagnostic channels.</summary>
public sealed record FieldAssignment(string Field, string Key, string Kind, string Text, bool Checked = false, string? On = null)
{
    public bool IsClear => Kind == "checkbox" ? !Checked : Text.Length == 0;
}
public static class AssignmentPlanner
{
    public static IReadOnlyList<FieldAssignment> Build(ValidationReport report)
    {
        if (!report.IsValid) throw new InvalidOperationException("E_ASSIGNMENT_VALIDATION");
        var assignments = new List<FieldAssignment>();
        foreach (var k in report.Profile.Keys)
        {
            var v = report.Values[k.Key];
            switch (k.Kind)
            {
                case "money":
                    var parts = v.IsPopulated ? ValueNormalizer.FormatMoney(v.Cents!.Value, k.Thousands!.Value, k.Zero!) : (Dollars: "", Cents: "");
                    assignments.Add(new(k.Dollars!, k.Key, "text", parts.Dollars));
                    assignments.Add(new(k.Cents!, k.Key, "text", parts.Cents));
                    break;
                case "checkbox": assignments.Add(new(k.Target!, k.Key, "checkbox", "", v.Boolean == true, k.On)); break;
                case "oneOf":
                    foreach (var option in k.Options!)
                        foreach (var target in option.Value) assignments.Add(new(target.Target, k.Key, "checkbox", "", v.IsPopulated && v.Text == option.Key, target.On));
                    break;
                case "split":
                    var runes = (v.Text ?? "").EnumerateRunes().ToArray();
                    foreach (var copy in k.Targets.EnumerateArray())
                    {
                        int offset = 0; int part = 0;
                        foreach (var target in copy.EnumerateArray())
                        {
                            int length = k.Parts![part++];
                            string text = v.IsPopulated ? string.Concat(runes.Skip(offset).Take(length).Select(x => x.ToString())) : "";
                            assignments.Add(new(target.GetString()!, k.Key, "text", text)); offset += length;
                        }
                    }
                    break;
                default:
                    string formatted = !v.IsPopulated ? "" : k.Kind == "date" ? v.Date!.Value.ToString(k.Format, CultureInfo.InvariantCulture) : v.Text!;
                    foreach (var target in k.EnumerateTargets()) assignments.Add(new(target, k.Key, "text", formatted));
                    break;
            }
        }
        if (assignments.Select(x => x.Field).Distinct(StringComparer.Ordinal).Count() != assignments.Count || assignments.Count != report.Profile.EnumerateTargets().Count())
            throw new InvalidOperationException("E_ASSIGNMENT_COVERAGE");
        return assignments.ToArray();
    }
}
