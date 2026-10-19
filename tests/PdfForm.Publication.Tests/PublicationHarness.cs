using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PdfForm.Publication;

internal sealed record HarnessCase(string Id, Action<string> Body);
internal sealed record HarnessRuntime(string Version, string FrameworkDescription, string ProcessArchitecture, string OSArchitecture);
internal sealed record HarnessIdentity(ulong Volume, ulong Number);
internal sealed record HarnessCaseResult(string Id, string Status, string Phase, string DirectoryCreationState,
    string IdentityState, bool BodyStarted, string? FailureCode = null, string? Checkpoint = null);
internal sealed record HarnessCounts(int Selected, int Executed, int Passed, int Failed, int Skipped);
internal sealed record HarnessTerminal
{
    public string Schema { get; init; } = "pdfform.publication-tests/1";
    public string Suite { get; init; } = "publication-synthetic";
    public required string AttemptId { get; init; }
    public required string Platform { get; init; }
    public required HarnessRuntime Runtime { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset CompletedUtc { get; init; }
    public string AttemptCreationState { get; init; } = "created";
    public string AttemptIdentityState { get; init; } = "verified";
    public required HarnessIdentity AttemptIdentity { get; init; }
    public required string[] SelectedCaseIds { get; init; }
    public required string SelectionSha256 { get; init; }
    public required string[] ExecutedCaseIds { get; init; }
    public required string[] FailedCaseIds { get; init; }
    public required string[] SkippedCaseIds { get; init; }
    public required HarnessCaseResult[] CaseResults { get; init; }
    public required HarnessCounts Counts { get; init; }
    public required string Status { get; init; }
    public string? RunFailureCode { get; init; }
    // This is the intended process result, contingent on successfully finalizing this record.
    // Wrappers must also observe the actual process exit and a complete matching terminal record.
    public required int RequestedExitCode { get; init; }
}
internal sealed record HarnessOutcome(int ExitCode, string? AttemptId, string CreationState,
    string IdentityState, bool TerminalWritten, HarnessTerminal? Terminal);
internal enum HarnessPoint
{
    BeforeRootMetadata, BeforeAttemptCreate, AfterAttemptCreated, BeforeAttemptIdentity,
    BeforeCaseCreate, AfterCaseCreated, BeforeCaseIdentity, BeforeCaseBody, AfterCaseRecorded, BeforeTerminalWrite
}
internal enum HarnessError
{
    Arguments, Root, Selection, AttemptCreate, AttemptIdentity, BootstrapWrite, CaseSetup,
    CaseExecution, Assertion, Interrupted, LogWrite, Accounting, ManifestWrite, AttemptChanged
}
internal sealed class HarnessFailure(HarnessError error) : Exception
{
    internal HarnessError Error { get; } = error;
}

/// <summary>
/// Test-only lifecycle and evidence, not product publication. No implicit root, cleanup,
/// directory scanning, overwrite, or hostile-namespace containment claim.
/// </summary>
internal static class PublicationHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static HarnessOutcome Run(string[] args, IReadOnlyList<HarnessCase> selection,
        TextWriter? stdout = null, TextWriter? stderr = null, Action<HarnessPoint, string?>? fault = null)
    {
        stdout ??= Console.Out; stderr ??= Console.Error;
        var started = DateTimeOffset.UtcNow;
        var runtime = new HarnessRuntime(Environment.Version.ToString(), RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.OSArchitecture.ToString());
        string? attemptId = null, attemptPath = null;
        string creation = "not_attempted", identityState = "not_attempted";
        FileIdentity? identity = null;
        HarnessError? runError = null;
        var results = new List<HarnessCaseResult>();
        var currentPhase = HarnessError.Arguments;
        HarnessCase[] selected = [];
        try
        {
            // All lexical checks precede metadata; no fallback to the working directory.
            var root = NormalizeRootArgument(args);
            currentPhase = HarnessError.Selection;
            selected = selection.ToArray();
            if (!ValidSelection(selected)) throw new HarnessFailure(HarnessError.Selection);
            currentPhase = HarnessError.Root;
            fault?.Invoke(HarnessPoint.BeforeRootMetadata, null);
            AdmitExistingRoot(root);
            attemptId = Guid.NewGuid().ToString("N");
            attemptPath = Path.Combine(root, "attempt-" + attemptId);
            currentPhase = HarnessError.AttemptCreate;
            fault?.Invoke(HarnessPoint.BeforeAttemptCreate, attemptId);
            creation = "attempted_unconfirmed";
            NativeFileSystem.CreateDirectoryExclusive(attemptPath);
            creation = "created";
            // Exactly one flushed bootstrap marker; never retry an uncertain write.
            currentPhase = HarnessError.BootstrapWrite;
            WriteLine(stdout, "PDFFORM_TEST_ATTEMPT " + attemptId);
            currentPhase = HarnessError.AttemptIdentity;
            fault?.Invoke(HarnessPoint.AfterAttemptCreated, attemptId);
            identityState = "unavailable";
            fault?.Invoke(HarnessPoint.BeforeAttemptIdentity, attemptId);
            identity = NativeFileSystem.Identity(attemptPath);
            if (!identity.Value.Directory) throw new HarnessFailure(HarnessError.AttemptIdentity);
            identityState = "verified";
            currentPhase = HarnessError.Interrupted;
            foreach (var item in selected)
            {
                var result = RunCase(attemptPath, item, fault);
                results.Add(result);
                try
                {
                    WriteLine(stdout, (result.Status == "passed" ? "PASS " : "FAIL ") + item.Id +
                        (result.FailureCode is null ? "" : " code=" + result.FailureCode) +
                        (result.Checkpoint is null ? "" : " checkpoint=" + result.Checkpoint));
                }
                catch { throw new HarnessFailure(HarnessError.LogWrite); }
                fault?.Invoke(HarnessPoint.AfterCaseRecorded, item.Id);
            }
        }
        catch (Exception error) { runError = error is HarnessFailure known ? known.Error : currentPhase; }

        // Unknown/unverified attempt paths are preserved, never used as an alternate evidence destination.
        if (creation != "created" || identity is null || identityState != "verified" || attemptPath is null || attemptId is null)
        {
            var code = runError ?? HarnessError.AttemptIdentity;
            ReportFailure(stderr, code, attemptId, creation, identityState, "unavailable");
            int exit = code is HarnessError.Arguments or HarnessError.Root or HarnessError.Selection ? 2 : 4;
            return new(exit, attemptId, creation, identityState, false, null);
        }

        foreach (var item in selected.Skip(results.Count))
            results.Add(new(item.Id, "skipped", "not_started", "not_attempted", "not_attempted", false, "E_HARNESS_ABORTED"));
        var terminal = MakeTerminal(attemptId, identity.Value, runtime, started, selected, results, runError);
        try
        {
            WriteLine(stdout, $"Publication synthetic suite: {terminal.Counts.Passed} passed, {terminal.Counts.Failed} failed, {terminal.Counts.Skipped} skipped. Native and broader lifecycle qualification remain separate.");
        }
        catch
        {
            runError = HarnessError.LogWrite;
            terminal = MakeTerminal(attemptId, identity.Value, runtime, started, selected, results, runError);
        }
        try
        {
            FinalizeTerminal(attemptPath, identity.Value, terminal, fault);
            return new(terminal.RequestedExitCode, attemptId, creation, identityState, true, terminal);
        }
        catch (Exception error)
        {
            var code = error is HarnessFailure known ? known.Error : HarnessError.ManifestWrite;
            ReportFailure(stderr, code, attemptId, creation, identityState, "write_failed");
            return new(4, attemptId, creation, identityState, false, terminal);
        }
    }

    private static HarnessCaseResult RunCase(string attemptPath, HarnessCase item, Action<HarnessPoint, string?>? fault)
    {
        string creation = "not_attempted", identity = "not_attempted";
        bool bodyStarted = false;
        try
        {
            var directory = Path.Combine(attemptPath, item.Id);
            fault?.Invoke(HarnessPoint.BeforeCaseCreate, item.Id);
            creation = "attempted_unconfirmed";
            NativeFileSystem.CreateDirectoryExclusive(directory);
            creation = "created";
            fault?.Invoke(HarnessPoint.AfterCaseCreated, item.Id);
            identity = "unavailable";
            fault?.Invoke(HarnessPoint.BeforeCaseIdentity, item.Id);
            if (!NativeFileSystem.Identity(directory).Directory) throw new IOException();
            identity = "verified";
            fault?.Invoke(HarnessPoint.BeforeCaseBody, item.Id);
            bodyStarted = true;
            item.Body(directory);
            return new(item.Id, "passed", "test", creation, identity, true);
        }
        catch (Exception error)
        {
            var code = !bodyStarted ? HarnessError.CaseSetup : error is PublicationAssertionException ? HarnessError.Assertion : HarnessError.CaseExecution;
            return new(item.Id, "failed", bodyStarted ? "test" : "setup", creation, identity, bodyStarted,
                Code(code), bodyStarted && error is PublicationAssertionException assertion ? assertion.Checkpoint : null);
        }
    }

    private static HarnessTerminal MakeTerminal(string attemptId, FileIdentity identity, HarnessRuntime runtime, DateTimeOffset started,
        IReadOnlyList<HarnessCase> selected, IReadOnlyList<HarnessCaseResult> results, HarnessError? error)
    {
        var ids = selected.Select(item => item.Id).ToArray();
        var executed = results.Where(item => item.BodyStarted).Select(item => item.Id).ToArray();
        var failed = results.Where(item => item.Status == "failed").Select(item => item.Id).ToArray();
        var skipped = results.Where(item => item.Status == "skipped").Select(item => item.Id).ToArray();
        int passed = results.Count(item => item.Status == "passed");
        bool success = error is null && passed == ids.Length && failed.Length == 0 && skipped.Length == 0;
        return new()
        {
            AttemptId = attemptId, Platform = OperatingSystem.IsWindows() ? "windows" : "linux", Runtime = runtime,
            StartedUtc = started, CompletedUtc = DateTimeOffset.UtcNow, AttemptIdentity = new(identity.Volume, identity.Number),
            SelectedCaseIds = ids, SelectionSha256 = SelectionHash(ids), ExecutedCaseIds = executed, FailedCaseIds = failed,
            SkippedCaseIds = skipped, CaseResults = results.ToArray(), Counts = new(ids.Length, executed.Length, passed, failed.Length, skipped.Length),
            Status = success ? "passed" : "failed", RunFailureCode = error is null ? null : Code(error.Value),
            RequestedExitCode = error is not null ? 4 : success ? 0 : 1
        };
    }

    internal static string NormalizeRootArgument(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0])) throw new HarnessFailure(HarnessError.Arguments);
        var path = args[0];
        try
        {
            if (path.Length > 4096 || !Path.IsPathFullyQualified(path)) throw new HarnessFailure(HarnessError.Root);
            if (OperatingSystem.IsWindows() && !IsDriveRooted(path) || OperatingSystem.IsLinux() && path.StartsWith("//", StringComparison.Ordinal))
                throw new HarnessFailure(HarnessError.Root);
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (OperatingSystem.IsWindows() && !IsDriveRooted(full)) throw new HarnessFailure(HarnessError.Root);
            var root = Path.GetPathRoot(full)!;
            if (full == root || full[root.Length..].Split(Path.DirectorySeparatorChar).Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.Any(c => c < 32 || ":*?\"<>|/\\".Contains(c)) || IsDeviceName(segment))) throw new HarnessFailure(HarnessError.Root);
            return full;
        }
        catch (HarnessFailure) { throw; }
        catch { throw new HarnessFailure(HarnessError.Root); }
    }
    private static bool IsDeviceName(string segment)
    {
        var name = segment.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) &&
            (name[3] is >= '0' and <= '9' or '¹' or '²' or '³');
    }
    private static bool IsDriveRooted(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
    private static void AdmitExistingRoot(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            if (drive.DriveType != DriveType.Fixed || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                throw new HarnessFailure(HarnessError.Root);
        }
        else if (!OperatingSystem.IsLinux()) throw new HarnessFailure(HarnessError.Root);
        string? current = root;
        while (current is not null)
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                throw new HarnessFailure(HarnessError.Root);
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool ValidSelection(IReadOnlyList<HarnessCase> selected) => selected.Count is > 0 and <= 1024 &&
        selected.All(item => item is not null && IsCaseId(item.Id) && item.Body is not null) &&
        selected.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == selected.Count;
    private static bool IsCaseId(string id) => id is not null && id.Length is > 0 and <= 128 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    internal static string SelectionHash(IEnumerable<string> ids) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\n', ids) + "\n"))).ToLowerInvariant();

    internal static bool ValidTerminal(HarnessTerminal terminal)
    {
        try
        {
            var ids = terminal.SelectedCaseIds; var results = terminal.CaseResults;
            if (terminal.Schema != "pdfform.publication-tests/1" || terminal.Suite != "publication-synthetic" ||
                terminal.AttemptId.Length != 32 || terminal.AttemptId.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
                terminal.Platform is not ("windows" or "linux") || terminal.AttemptCreationState != "created" || terminal.AttemptIdentityState != "verified" ||
                terminal.CompletedUtc < terminal.StartedUtc || terminal.AttemptIdentity is null ||
                !ValidRuntime(terminal.Runtime) || terminal.RunFailureCode is not null && !ValidRunFailure(terminal.RunFailureCode) || ids.Length is < 1 or > 1024 || ids.Any(id => !IsCaseId(id)) ||
                ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length || terminal.SelectionSha256 != SelectionHash(ids) ||
                !ids.SequenceEqual(results.Select(item => item.Id))) return false;
            foreach (var item in results)
            {
                if (item.Status == "passed")
                {
                    if (item.Phase != "test" || !item.BodyStarted || item.DirectoryCreationState != "created" || item.IdentityState != "verified" ||
                        item.FailureCode is not null || item.Checkpoint is not null) return false;
                }
                else if (item.Status == "skipped")
                {
                    if (item.Phase != "not_started" || item.BodyStarted || item.DirectoryCreationState != "not_attempted" || item.IdentityState != "not_attempted" ||
                        item.FailureCode != "E_HARNESS_ABORTED" || item.Checkpoint is not null || terminal.RunFailureCode is null) return false;
                }
                else if (item.Status == "failed")
                {
                    if (item.Phase is not ("setup" or "test") || item.BodyStarted != (item.Phase == "test") ||
                        item.DirectoryCreationState is not ("not_attempted" or "attempted_unconfirmed" or "created") ||
                        item.IdentityState is not ("not_attempted" or "unavailable" or "verified") ||
                        item.BodyStarted && (item.DirectoryCreationState != "created" || item.IdentityState != "verified") ||
                        item.DirectoryCreationState != "created" && item.IdentityState != "not_attempted" ||
                        item.Checkpoint is not null && (!item.BodyStarted || !ValidCheckpoint(item.Checkpoint)) ||
                        item.FailureCode != (item.BodyStarted ? item.Checkpoint is not null ? Code(HarnessError.Assertion) : Code(HarnessError.CaseExecution) : Code(HarnessError.CaseSetup))) return false;
                }
                else return false;
            }
            var executed = results.Where(item => item.BodyStarted).Select(item => item.Id).ToArray();
            var failed = results.Where(item => item.Status == "failed").Select(item => item.Id).ToArray();
            var skipped = results.Where(item => item.Status == "skipped").Select(item => item.Id).ToArray();
            int passed = results.Count(item => item.Status == "passed");
            if (!terminal.ExecutedCaseIds.SequenceEqual(executed) || !terminal.FailedCaseIds.SequenceEqual(failed) || !terminal.SkippedCaseIds.SequenceEqual(skipped) ||
                terminal.Counts != new HarnessCounts(ids.Length, executed.Length, passed, failed.Length, skipped.Length)) return false;
            bool success = terminal.RunFailureCode is null && passed == ids.Length;
            return terminal.Status == (success ? "passed" : "failed") &&
                terminal.RequestedExitCode == (terminal.RunFailureCode is not null ? 4 : success ? 0 : 1);
        }
        catch { return false; }
    }
    private static bool ValidRuntime(HarnessRuntime runtime) => runtime is not null &&
        !string.IsNullOrWhiteSpace(runtime.Version) && runtime.Version.Length <= 128 &&
        !string.IsNullOrWhiteSpace(runtime.FrameworkDescription) && runtime.FrameworkDescription.Length <= 256 &&
        Enum.GetNames<Architecture>().Contains(runtime.ProcessArchitecture, StringComparer.Ordinal) &&
        Enum.GetNames<Architecture>().Contains(runtime.OSArchitecture, StringComparer.Ordinal);
    private static bool ValidCheckpoint(string checkpoint) => checkpoint.Length is > 0 and <= 96 &&
        checkpoint.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    private static bool ValidRunFailure(string code) => code is "E_HARNESS_INTERRUPTED" or "E_HARNESS_LOG_WRITE";
    // The expected set/token/platform are independent invocation authority, never derived from the receipt.
    internal static bool IsPassingReceipt(HarnessTerminal? terminal, int observedExitCode, IReadOnlyList<string> expectedCaseIds,
        string expectedAttemptId, string expectedPlatform) => observedExitCode == 0 && terminal is not null &&
        ValidTerminal(terminal) && terminal.Status == "passed" && terminal.RequestedExitCode == 0 &&
        terminal.AttemptId == expectedAttemptId && terminal.Platform == expectedPlatform && terminal.SelectedCaseIds.SequenceEqual(expectedCaseIds);

    internal static void FinalizeTerminal(string attemptPath, FileIdentity expectedIdentity, HarnessTerminal terminal,
        Action<HarnessPoint, string?>? fault = null)
    {
        if (!ValidTerminal(terminal) || !expectedIdentity.Directory ||
            terminal.AttemptIdentity != new HarnessIdentity(expectedIdentity.Volume, expectedIdentity.Number) ||
            Path.GetFileName(attemptPath) != "attempt-" + terminal.AttemptId) throw new HarnessFailure(HarnessError.Accounting);
        // This is a point-in-time identity observation, not a hostile-directory writer guarantee.
        if (!PathGuard.SameIdentity(NativeFileSystem.Identity(attemptPath), expectedIdentity)) throw new HarnessFailure(HarnessError.AttemptChanged);
        fault?.Invoke(HarnessPoint.BeforeTerminalWrite, null);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(terminal, JsonOptions);
        using var stream = new FileStream(Path.Combine(attemptPath, "terminal.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(flushToDisk: true);
    }
    private static void WriteLine(TextWriter writer, string text) { writer.WriteLine(text); writer.Flush(); }
    private static void ReportFailure(TextWriter stderr, HarnessError error, string? attemptId, string creation, string identity, string terminal)
    {
        try { WriteLine(stderr, Code(error) + " attempt=" + (attemptId ?? "none") + " creation=" + creation + " identity=" + identity + " terminal=" + terminal); }
        catch { /* No fallback channel/path and no retry of an uncertain diagnostic write. */ }
    }
    private static string Code(HarnessError error) => error switch
    {
        HarnessError.Arguments => "E_HARNESS_ARGUMENTS", HarnessError.Root => "E_HARNESS_ROOT", HarnessError.Selection => "E_HARNESS_SELECTION",
        HarnessError.AttemptCreate => "E_HARNESS_ATTEMPT_CREATE", HarnessError.AttemptIdentity => "E_HARNESS_ATTEMPT_IDENTITY",
        HarnessError.BootstrapWrite => "E_HARNESS_BOOTSTRAP_WRITE", HarnessError.CaseSetup => "E_HARNESS_CASE_SETUP",
        HarnessError.CaseExecution => "E_HARNESS_CASE_EXECUTION", HarnessError.Assertion => "E_HARNESS_ASSERTION",
        HarnessError.Interrupted => "E_HARNESS_INTERRUPTED", HarnessError.LogWrite => "E_HARNESS_LOG_WRITE",
        HarnessError.Accounting => "E_HARNESS_ACCOUNTING", HarnessError.ManifestWrite => "E_HARNESS_MANIFEST_WRITE",
        HarnessError.AttemptChanged => "E_HARNESS_ATTEMPT_CHANGED", _ => "E_HARNESS_INTERNAL"
    };
}
