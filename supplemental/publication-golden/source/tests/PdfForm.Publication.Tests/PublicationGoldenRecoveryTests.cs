using System.Security.Cryptography;
using System.Text.Json;
using PdfForm.Publication;

// Frozen externally authored package bytes, not SafePublisher output. The raw fixtures and
// external expectations are retained in the source-only successor for independent review.
// This exercises structural recovery and bounded reads, not persistent authenticity or PDF rendering.
internal static class PublicationGoldenRecoveryTests
{
    private const string RequestId = "golden-request-1";
    private const string ToolBuild = "golden-build-1";
    private const int ActualTotal = 1736;
    private const int PaddedCompletionLength = 2048;
    private static readonly byte[] Alpha = Convert.FromHexString("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
    private static readonly byte[] Beta = Convert.FromHexString("202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f");
    private static readonly byte[] Result = Convert.FromBase64String("eyJzY2hlbWEiOiJwZGZmb3JtLnJlc3VsdC8yIiwicmVxdWVzdElkIjoiZ29sZGVuLXJlcXVlc3QtMSIsIm9wZXJhdGlvbiI6InJ1biIsInN0YXR1cyI6Im9rIiwiZXhpdENvZGUiOjAsImFydGlmYWN0U3RhdGUiOiJwdWJsaXNoZWQiLCJ2YWxpZGF0aW9uTGV2ZWxzIjp7InN5bnRheCI6InBhc3NlZCIsInByb2ZpbGUiOiJwYXNzZWQiLCJkYXRhIjoicGFzc2VkIiwiYmluZGluZyI6InBhc3NlZCIsInBhY2tldCI6InBhc3NlZCIsInJlbmRlciI6InBhc3NlZCIsInB1YmxpY2F0aW9uIjoicGFzc2VkIn0sInRvb2wiOnsibmFtZSI6InBkZmZvcm0iLCJ2ZXJzaW9uIjoiZ29sZGVuLXYxIiwiYnVpbGQiOiJnb2xkZW4tYnVpbGQtMSIsImVuZ2luZSI6InN5bnRoZXRpYyIsInJ1bnRpbWUiOiIuTkVUIiwic291cmNlUmVmZXJlbmNlIjoiaW5kZXBlbmRlbnQtZ29sZGVuIn0sInByb2ZpbGVzIjpbeyJpZCI6ImdvbGRlbi1wcm9maWxlIiwicmV2aXNpb24iOiIyMDI2IiwicHJvZmlsZVZlcnNpb24iOjEsInNoYTI1NiI6ImFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWEifV0sInRlbXBsYXRlcyI6W3siaWQiOiJnb2xkZW4tdGVtcGxhdGUiLCJzaGEyNTYiOiJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiIn1dLCJvdXRwdXRzIjpbeyJpZCI6ImFscGhhIiwicGF0aCI6ImZvcm1zL2EucGRmIiwiYnl0ZXMiOjMyLCJzaGEyNTYiOiI2MzBkY2QyOTY2YzQzMzY2OTExMjU0NDhiYmIyNWI0ZmY0MTJhNDljNzMyZGIyYzhhYmMxYjg1ODFiZDcxMGRkIn0seyJpZCI6ImJldGEiLCJwYXRoIjoiZm9ybXMvYi5wZGYiLCJieXRlcyI6MzIsInNoYTI1NiI6IjcyZGJiNzMzNmM3Njc4MDAyM2Y4M2RhNGMzNTVmMmVlZWE4NTczM2IxM2QzNDc3Njk3OTE3NzkwYzEyMjkwODQifV0sImVycm9ycyI6W10sIndhcm5pbmdzIjpbeyJjb2RlIjoiV19HT0xERU4iLCJzdGFnZSI6ImRhdGEiLCJtZXNzYWdlIjoiR29sZGVuIGNvbnRyb2wgd2FybmluZy4iLCJzZXZlcml0eSI6Indhcm5pbmciLCJrZXkiOiJleGFtcGxlLmtleSIsImZpZWxkIjoic3ludGhldGljLWZpZWxkIiwicnVsZUlkIjoiZXhhbXBsZS1ydWxlIn1dLCJzdGF0cyI6eyJjaGVja2VkIjoyfSwiZmlsaW5nUmVhZGluZXNzIjoibm90X2Fzc2Vzc2VkIn0=");
    private static readonly byte[] Completion = Convert.FromBase64String("eyJhcnRpZmFjdHMiOlt7ImlkIjoiYWxwaGEiLCJwYXRoIjoiZm9ybXMvYS5wZGYiLCJieXRlcyI6MzIsInNoYTI1NiI6IjYzMGRjZDI5NjZjNDMzNjY5MTEyNTQ0OGJiYjI1YjRmZjQxMmE0OWM3MzJkYjJjOGFiYzFiODU4MWJkNzEwZGQifSx7ImlkIjoiYmV0YSIsInBhdGgiOiJmb3Jtcy9iLnBkZiIsImJ5dGVzIjozMiwic2hhMjU2IjoiNzJkYmI3MzM2Yzc2NzgwMDIzZjgzZGE0YzM1NWYyZWVlYTg1NzMzYjEzZDM0Nzc2OTc5MTc3OTBjMTIyOTA4NCJ9XSwicmVzdWx0Ijp7InBhdGgiOiJyZXN1bHQuanNvbiIsImJ5dGVzIjoxMTk5LCJzaGEyNTYiOiJlZjIwYWIyNTU2YzExYzNkNzEyNmJlNWM0MThiMTIwYTMxOGU3N2EwMGNkYTdjMDQ3NDBmOTBlZjY0M2VkODllIn0sInRvb2xCdWlsZCI6ImdvbGRlbi1idWlsZC0xIiwicmVxdWVzdElkIjoiZ29sZGVuLXJlcXVlc3QtMSIsInNjaGVtYSI6InBkZmZvcm0uY29tcGxldGlvbi8xIn0=");
    private static readonly ArtifactDescriptor[] ExternalArtifacts =
    [
        new("alpha", "forms/a.pdf", 32, "630dcd2966c4336691125448bbb25b4ff412a49c732db2c8abc1b8581bd710dd"),
        new("beta", "forms/b.pdf", 32, "72dbb7336c76780023f83da4c355f2eeea85733b13d3477697917790c1229084")
    ];
    private static readonly ExpectedArtifact[] Expected = [new("alpha", "forms/a.pdf"), new("beta", "forms/b.pdf")];
    private static readonly PublicationLimits BaseLimits = new(32, 1048576, 2, 8192);

    internal static void Register(Action<string, Action<string>> test)
    {
        test("golden-recovery-external-package", dir =>
        {
            var package = Materialize(dir, Completion);
            // The baseline limit tuple is the external fixture's own published expectation.
            var limits = new PublicationLimits(64, 1048576, 4, 8192);
            Check(64L + 2L * limits.MaxManifestBytes == 16448, "golden-external-reservation-arithmetic");
            var outcome = CompletionProtocol.Reconcile(package, RequestId, ToolBuild, Expected, limits);
            RequireComplete(outcome);
            VerifyUnchanged(package, Completion);
        });

        foreach (var boundary in Boundaries())
            test("golden-recovery-total-cap-" + boundary.Name, dir =>
            {
                var package = Materialize(dir, Completion);
                var limits = BaseLimits with { MaxTotalBytes = ActualTotal + boundary.Delta };
                Check(Alpha.Length + Beta.Length + Result.Length + Completion.Length == ActualTotal &&
                    limits.MaxArtifactBytes == 32 && limits.MaxTotalBytes >= limits.MaxArtifactBytes &&
                    Result.Length <= limits.MaxManifestBytes && Completion.Length <= limits.MaxManifestBytes,
                    "golden-total-other-caps-unmasked");
                Check(limits.MaxTotalBytes < 64L + 2L * limits.MaxManifestBytes, "golden-recovery-uses-actual-not-reservation");
                var outcome = CompletionProtocol.Reconcile(package, RequestId, ToolBuild, Expected, limits);
                if (boundary.Delta < 0) RequireRefused(outcome, "E_OUTPUT_LIMIT"); else RequireComplete(outcome);
                VerifyUnchanged(package, Completion);
            });

        foreach (var boundary in Boundaries())
            test("golden-recovery-result-cap-" + boundary.Name, dir =>
            {
                var package = Materialize(dir, Completion);
                var limits = BaseLimits with { MaxManifestBytes = Result.Length + boundary.Delta };
                Check(limits.MaxManifestBytes >= 256 && Completion.Length < limits.MaxManifestBytes &&
                    ActualTotal < limits.MaxTotalBytes && Alpha.Length <= limits.MaxArtifactBytes && Beta.Length <= limits.MaxArtifactBytes,
                    "golden-result-only-read-cap-discriminates");
                var outcome = CompletionProtocol.Reconcile(package, RequestId, ToolBuild, Expected, limits);
                if (boundary.Delta < 0) RequireRefused(outcome, "E_MANIFEST_LIMIT"); else RequireComplete(outcome);
                VerifyUnchanged(package, Completion);
            });

        foreach (var boundary in Boundaries())
            test("golden-recovery-completion-cap-" + boundary.Name, dir =>
            {
                var padded = new byte[PaddedCompletionLength]; Completion.CopyTo(padded, 0);
                Array.Fill(padded, (byte)' ', Completion.Length, padded.Length - Completion.Length);
                var package = Materialize(dir, padded);
                var limits = BaseLimits with { MaxManifestBytes = PaddedCompletionLength + boundary.Delta };
                Check(padded.AsSpan(0, Completion.Length).SequenceEqual(Completion) &&
                    padded.AsSpan(Completion.Length).ToArray().All(value => value == (byte)' '), "golden-padding-only-json-whitespace");
                Check(Result.Length < limits.MaxManifestBytes && limits.MaxManifestBytes >= 256 &&
                    Alpha.Length + Beta.Length + Result.Length + padded.Length < limits.MaxTotalBytes &&
                    Alpha.Length <= limits.MaxArtifactBytes && Beta.Length <= limits.MaxArtifactBytes,
                    "golden-completion-only-read-cap-discriminates");
                var outcome = CompletionProtocol.Reconcile(package, RequestId, ToolBuild, Expected, limits);
                if (boundary.Delta < 0) RequireRefused(outcome, "E_MANIFEST_LIMIT"); else RequireComplete(outcome);
                VerifyUnchanged(package, padded);
            });
    }

    private static (string Name, int Delta)[] Boundaries() => [("one-under", -1), ("exact", 0), ("one-over", 1)];
    private static void Check(bool condition, string checkpoint)
    {
        if (!condition) throw new PublicationAssertionException(checkpoint);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void RequireComplete(ReconciliationOutcome outcome)
    {
        Check(outcome.IsComplete && outcome.ErrorCode is null, "golden-complete-without-error");
        Check(outcome.Outputs.OrderBy(item => item.Id, StringComparer.Ordinal).SequenceEqual(ExternalArtifacts), "golden-returned-external-descriptors");
    }
    private static void RequireRefused(ReconciliationOutcome outcome, string expectedCode)
    {
        Check(!outcome.IsComplete && outcome.ErrorCode == expectedCode && outcome.Outputs.Count == 0, "golden-specific-refusal-no-outputs");
    }
    private static string Materialize(string directory, byte[] completion)
    {
        VerifyFrozenBytes();
        var package = Path.Combine(directory, "package"); Directory.CreateDirectory(Path.Combine(package, "forms"));
        foreach (var item in new[] { ("forms/a.pdf", Alpha), ("forms/b.pdf", Beta), ("result.json", Result), ("completion.json", completion) })
        {
            using var stream = new FileStream(Path.Combine(package, item.Item1), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(item.Item2);
        }
        VerifyUnchanged(package, completion);
        return package;
    }
    private static void VerifyFrozenBytes()
    {
        Check(Alpha.Length == 32 && Hash(Alpha) == ExternalArtifacts[0].Sha256 && Beta.Length == 32 && Hash(Beta) == ExternalArtifacts[1].Sha256,
            "golden-external-artifact-bytes");
        Check(Result.Length == 1199 && Hash(Result) == "ef20ab2556c11c3d7126be5c418b120a318e77a00cda7c04740f90ef643ed89e",
            "golden-external-result-bytes");
        Check(Completion.Length == 473 && Hash(Completion) == "6f881ad6c41f4c6b024b78fca550d6738535728c6168309bbc457bfcf430ef0e",
            "golden-external-completion-bytes");
    }
    private static void VerifyUnchanged(string package, byte[] completion)
    {
        string Relative(string path) => Path.GetRelativePath(package, path).Replace('\\', '/');
        var files = Directory.GetFiles(package, "*", SearchOption.AllDirectories).Select(Relative).Order(StringComparer.Ordinal).ToArray();
        Check(files.SequenceEqual(new[] { "completion.json", "forms/a.pdf", "forms/b.pdf", "result.json" }), "golden-exact-file-set");
        Check(Directory.GetDirectories(package, "*", SearchOption.AllDirectories).Select(Relative).SequenceEqual(new[] { "forms" }), "golden-exact-directory-set");
        Check(File.ReadAllBytes(Path.Combine(package, "forms/a.pdf")).SequenceEqual(Alpha) &&
            File.ReadAllBytes(Path.Combine(package, "forms/b.pdf")).SequenceEqual(Beta) &&
            File.ReadAllBytes(Path.Combine(package, "result.json")).SequenceEqual(Result) &&
            File.ReadAllBytes(Path.Combine(package, "completion.json")).SequenceEqual(completion), "golden-exact-files-preserved");
        // Independent JSON/hash arithmetic, without product descriptor/manifest creation helpers.
        using var result = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package, "result.json")));
        using var final = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package, "completion.json")));
        Check(result.RootElement.GetProperty("requestId").GetString() == RequestId && final.RootElement.GetProperty("requestId").GetString() == RequestId &&
            result.RootElement.GetProperty("tool").GetProperty("build").GetString() == ToolBuild && final.RootElement.GetProperty("toolBuild").GetString() == ToolBuild,
            "golden-manifest-request-build");
        foreach (var array in new[] { result.RootElement.GetProperty("outputs"), final.RootElement.GetProperty("artifacts") })
        {
            Check(array.GetArrayLength() == 2, "golden-declared-artifact-count");
            foreach (var expected in ExternalArtifacts)
            {
                var item = array.EnumerateArray().Single(entry => entry.GetProperty("id").GetString() == expected.Id);
                var bytes = File.ReadAllBytes(Path.Combine(package, expected.Path));
                Check(item.GetProperty("path").GetString() == expected.Path && item.GetProperty("bytes").GetInt64() == expected.Bytes &&
                    item.GetProperty("sha256").GetString() == expected.Sha256 && bytes.LongLength == expected.Bytes && Hash(bytes) == expected.Sha256,
                    "golden-declared-independent-descriptor");
            }
        }
        var declaredResult = final.RootElement.GetProperty("result");
        Check(declaredResult.GetProperty("path").GetString() == "result.json" && declaredResult.GetProperty("bytes").GetInt64() == 1199 &&
            declaredResult.GetProperty("sha256").GetString() == Hash(Result), "golden-declared-result-byte-anchor");
    }
}
