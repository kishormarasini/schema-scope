using SchemaScope.Parsing;

namespace SchemaScope.Verification;

public enum VerificationStatus
{
    Matches,
    Differs,
    Missing
}

public sealed record VerificationResult
{
    public required DdlObject Object { get; init; }
    public required VerificationStatus Status { get; init; }
    public required string Detail { get; init; }
    public string? DbDumpPath { get; init; }
    public string? FileDumpPath { get; init; }
}

public sealed class VerificationReport
{
    public int Version { get; init; }
    public required IReadOnlyList<VerificationResult> Results { get; init; }
    public IReadOnlyList<string> ParseWarnings { get; init; } = Array.Empty<string>();

    public int OkCount      => Results.Count(r => r.Status == VerificationStatus.Matches);
    public int DiffersCount => Results.Count(r => r.Status == VerificationStatus.Differs);
    public int MissingCount => Results.Count(r => r.Status == VerificationStatus.Missing);

    public VerificationVerdict Verdict
    {
        get
        {
            if (Results.Count == 0)           return VerificationVerdict.NoObjects;
            if (DiffersCount == 0 && MissingCount == 0) return VerificationVerdict.FullyApplied;
            if (OkCount == 0 && DiffersCount == 0)      return VerificationVerdict.NotApplied;
            return VerificationVerdict.Partial;
        }
    }
}

public enum VerificationVerdict
{
    NoObjects,
    FullyApplied,
    NotApplied,
    Partial
}
