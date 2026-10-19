using System.Globalization;

namespace PdfForm.Core;

public static class PacketValidator
{
    public static ParseResult<JobDocument> ParseJob(ReadOnlySpan<byte> bytes)
    {
        var parsed = StrictJson.Parse<JobDocument>(bytes, "job");
        if (!parsed.IsValid) return parsed;
        var job = parsed.Value!;
        var findings = new List<Finding>();
        void Error(string code, string message) => findings.Add(new(code, "syntax", message));
        if (job.Schema != "pdfform.job/2") Error("E_JOB_SCHEMA", "Job schema must be pdfform.job/2.");
        if (!ProfileParser.Match(job.RequestId, "[A-Za-z0-9_-]{1,64}")) Error("E_REQUEST_ID", "Request ID must use the bounded ASCII grammar.");
        if (job.Policy != "irs-941-packet/1") Error("E_JOB_POLICY", "Job policy is unsupported.");
        if (job.Documents.Length is < 1 or > 2 || job.Documents.Count(x => x.Alias == "941") != 1 || job.Documents.Count(x => x.Alias == "941sb") > 1 || job.Documents.Any(x => x.Alias is not ("941" or "941sb")))
            Error("E_JOB_DOCUMENTS", "Job requires one Form 941 and at most one Schedule B.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "result.json", "completion.json" };
        foreach (var member in job.Documents)
        {
            if (new[] { member.Pdf, member.Profile, member.Data }.Any(string.IsNullOrWhiteSpace)) Error("E_JOB_SOURCE", "Source references must be nonempty.");
            if (!IsSafeRelativeOutput(member.Output) || !names.Add(member.Output.Replace('\\', '/'))) Error("E_JOB_OUTPUT", "Output path is unsafe, reserved or collides with another output.");
        }
        if (job.Packet is { } packet)
        {
            if (!job.Flatten) Error("E_PACKET_FLATTEN", "A packet requires flattened member outputs.");
            if (!IsSafeRelativeOutput(packet.Output) || !names.Add(packet.Output.Replace('\\', '/'))) Error("E_JOB_OUTPUT", "Packet output path is unsafe, reserved or collides.");
            var selected = new HashSet<(string, int)>();
            foreach (var selection in packet.Pages)
            {
                int maxPages = selection.Document == "941" ? 3 : selection.Document == "941sb" ? 1 : 0;
                if (!job.Documents.Any(x => x.Alias == selection.Document) || selection.Pages.Length == 0 || selection.Pages.Any(p => p < 1 || p > maxPages || !selected.Add((selection.Document, p))))
                    Error("E_PACKET_PAGES", "Packet page selection is unknown, empty, duplicated or out of range.");
            }
            if (!selected.Contains(("941", 1)) || !selected.Contains(("941", 2)) || (job.Documents.Any(x => x.Alias == "941sb") && !selected.Contains(("941sb", 1))))
                Error("E_PACKET_PAGES", "Packet omits a required form page.");
        }
        return new(findings.Count == 0 ? job : null, findings.ToArray());
    }

    public static bool IsSafeRelativeOutput(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':') || path.Any(char.IsControl)) return false;
        return path.Replace('\\', '/').Split('/').All(x => x.Length > 0 && x is not ("." or "..") && !x.EndsWith('.') && !x.EndsWith(' ') && x.IndexOfAny(['<', '>', '"', '|', '?', '*']) < 0 && !IsDeviceName(x));
    }
    private static bool IsDeviceName(string segment)
    {
        string stem = segment.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || ProfileParser.Match(stem, "(?:COM|LPT)[1-9]");
    }

    public static IReadOnlyList<Finding> Validate(JobDocument job, IReadOnlyDictionary<string, ValidationReport> reports)
    {
        var findings = new List<Finding>();
        void Error(string code, string message, string? key = null) => findings.Add(new(code, "packet", message, key));
        if (!reports.TryGetValue("941", out var form)) { Error("E_PACKET_INPUT", "Form 941 validation is required."); return findings.ToArray(); }
        if (reports.Count != job.Documents.Length || job.Documents.Any(x => !reports.ContainsKey(x.Alias))) Error("E_PACKET_INPUT", "Validated document set does not match the job.");
        if (form.Profile.Id != "irs-941") Error("E_PACKET_PROFILE", "Form alias is bound to the wrong profile.");
        bool hasSchedule = reports.TryGetValue("941sb", out var schedule);
        if ((form.Data.Context.LiabilityReporting == "semiweekly") != hasSchedule) Error("E_PACKET_SCHEDULE_B", "Schedule B must be present exactly for semiweekly reporting.");
        if (!hasSchedule) return findings.ToArray();
        if (schedule!.Profile.Id != "irs-941sb") Error("E_PACKET_PROFILE", "Schedule B alias is bound to the wrong profile.");
        if (form.Data.Context != schedule.Data.Context) Error("E_PACKET_CONTEXT", "Document filing contexts do not agree.");
        foreach (string key in new[] { "ein", "name", "quarter" })
            if (form.Values.TryGetValue(key, out var a) && schedule.Values.TryGetValue(key, out var b) && a.IsValid && b.IsValid && !ValueNormalizer.Equivalent(a, b))
                Error("E_PACKET_IDENTITY", "Document identity or quarter does not agree.", key);
        if (form.Values.TryGetValue("line12.totalTaxes", out var total) && schedule.Values.TryGetValue("total", out var scheduleTotal) && total.IsValid && scheduleTotal.IsValid && !ValueNormalizer.Equivalent(total, scheduleTotal))
            Error("E_PACKET_TOTAL", "Schedule B quarter total does not agree with Form 941 line 12.", "total");
        findings.AddRange(ValidateCalendar(schedule));
        return findings.ToArray();
    }

    public static IReadOnlyList<Finding> ValidateCalendar(ValidationReport schedule)
    {
        var findings = new List<Finding>();
        var context = schedule.Data.Context;
        int year = int.Parse(context.TaxYear, CultureInfo.InvariantCulture);
        int firstMonth = (int.Parse(context.Quarter, CultureInfo.InvariantCulture) - 1) * 3 + 1;
        for (int month = 1; month <= 3; month++)
            for (int day = DateTime.DaysInMonth(year, firstMonth + month - 1) + 1; day <= 31; day++)
            {
                string key = $"month{month}.day{day:00}";
                if (schedule.Values.TryGetValue(key, out var value) && value.IsPopulated)
                    findings.Add(new("E_CALENDAR_DAY", "packet", "A nonexistent calendar day must be omitted or blank.", key));
            }
        return findings.ToArray();
    }
}
