using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchemaScope.Auditing;

public enum AuditScriptStatus
{
    Applied,
    Partial,
    Missing,
    NoDdl
}

public enum AuditVerdict
{
    AtHead,
    BehindHead,
    NotInitialised,
    NoAppliedVersionInRange
}

public sealed record AuditObjectResult(
    string Kind,
    string? Schema,
    string? Parent,
    string Name,
    string Status,
    string Detail);

public sealed record AuditScriptResult(
    int Version,
    string Label,
    string File,
    AuditScriptStatus Status,
    int ObjectsPresent,
    int ObjectsTotal,
    int ParseErrors,
    IReadOnlyList<AuditObjectResult> Mismatches);

public sealed record AuditTotals(
    int Scripts,
    int Applied,
    int Partial,
    int Missing,
    int NoDdl,
    int WithParseErrors);

public sealed record JournalEntry(
    string ScriptName,
    string? Version,
    DateTime? AppliedAtUtc,
    bool Success);

public sealed record JournalFinding(
    string ScriptName,
    string? Label,
    string Detail);

public sealed record JournalAudit(
    string Provider,
    string Table,
    int Entries,
    IReadOnlyList<JournalFinding> JournaledButNotApplied,
    IReadOnlyList<JournalFinding> AppliedButNotJournaled,
    IReadOnlyList<JournalFinding> UnmatchedJournalEntries,
    IReadOnlyList<JournalFinding> FailedJournalEntries)
{
    public bool HasLies => JournaledButNotApplied.Count > 0;
    public bool IsClean =>
        JournaledButNotApplied.Count == 0
        && AppliedButNotJournaled.Count == 0
        && UnmatchedJournalEntries.Count == 0
        && FailedJournalEntries.Count == 0;
}

public sealed record AuditReport(
    string Tool,
    string ToolVersion,
    DateTime ScannedAtUtc,
    string Server,
    string Database,
    string ScriptsFolder,
    string Scheme,
    int StartFrom,
    int Head,
    bool PartialScan,
    AuditVerdict Verdict,
    int CurrentVersion,
    string? CurrentVersionLabel,
    AuditTotals Totals,
    IReadOnlyList<AuditScriptResult> Pending,
    IReadOnlyList<AuditScriptResult> Drift,
    IReadOnlyList<AuditScriptResult> Scripts,
    JournalAudit? Journal = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public int ToExitCode(bool strict)
    {
        if (Journal is { HasLies: true })
        {
            return 1;
        }
        if (strict && Journal is { IsClean: false })
        {
            return 1;
        }
        return Verdict switch
        {
            AuditVerdict.AtHead => strict && Drift.Count > 0 ? 1 : 0,
            _ => 1
        };
    }
}

public sealed record SingleVerifyReport(
    string Tool,
    string ToolVersion,
    DateTime ScannedAtUtc,
    string Server,
    string Database,
    int Version,
    string Label,
    string File,
    string Verdict,
    int ObjectsPresent,
    int ObjectsTotal,
    int Differs,
    int Missing,
    int ParseErrors,
    IReadOnlyList<AuditObjectResult> Mismatches)
{
    public string ToJson() => JsonSerializer.Serialize(this, AuditReport.JsonOptions);
}
