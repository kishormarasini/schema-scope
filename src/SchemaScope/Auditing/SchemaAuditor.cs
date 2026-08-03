using System.IO;
using SchemaScope.Parsing;
using SchemaScope.Sql;
using SchemaScope.Verification;

namespace SchemaScope.Auditing;

public sealed class SchemaAuditor
{
    private readonly SqlSession _session;
    private readonly VersionScriptLocator _locator;
    private readonly string _defaultSchema;

    public SchemaAuditor(SqlSession session, VersionScriptLocator locator, string defaultSchema)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _defaultSchema = defaultSchema;
    }

    public AuditReport? Run(int startFrom, int endAt, Action<VersionScript, AuditScriptResult>? onScript = null)
    {
        var from = startFrom <= 0 ? 0 : startFrom;
        var scripts = _locator.GetInRange(from, endAt);
        if (scripts.Count == 0)
        {
            return null;
        }

        var extractor = new DdlExtractor();
        var verifier = new VersionVerifier(_session, _defaultSchema);

        var entries = new List<AuditScriptResult>(scripts.Count);
        var highestApplied = 0;

        foreach (var script in scripts)
        {
            var entry = Probe(script, extractor, verifier);
            entries.Add(entry);
            if (entry.Status is AuditScriptStatus.Applied or AuditScriptStatus.NoDdl)
            {
                highestApplied = script.Number;
            }
            onScript?.Invoke(script, entry);
        }

        var (verdict, pending, drift, totals) = Classify(entries, highestApplied, from);

        return new AuditReport(
            Tool: AppInfo.Name,
            ToolVersion: AppInfo.Version,
            ScannedAtUtc: DateTime.UtcNow,
            Server: _session.Server,
            Database: _session.Database,
            ScriptsFolder: _locator.FolderPath,
            Scheme: _locator.Scheme.LabelFormat,
            StartFrom: from,
            Head: scripts[^1].Number,
            PartialScan: from > 1,
            Verdict: verdict,
            CurrentVersion: highestApplied,
            CurrentVersionLabel: highestApplied > 0 ? _locator.Label(highestApplied) : null,
            Totals: totals,
            Pending: pending,
            Drift: drift,
            Scripts: entries);
    }

    internal static (AuditVerdict Verdict,
                     IReadOnlyList<AuditScriptResult> Pending,
                     IReadOnlyList<AuditScriptResult> Drift,
                     AuditTotals Totals) Classify(
        IReadOnlyList<AuditScriptResult> entries, int highestApplied, int startFrom)
    {
        var unapplied = entries
            .Where(e => e.Status is AuditScriptStatus.Partial or AuditScriptStatus.Missing)
            .ToList();

        var drift   = unapplied.Where(e => e.Version < highestApplied).OrderBy(e => e.Version).ToList();
        var pending = unapplied.Where(e => e.Version > highestApplied).OrderBy(e => e.Version).ToList();

        var totals = new AuditTotals(
            Scripts: entries.Count,
            Applied: entries.Count(e => e.Status == AuditScriptStatus.Applied),
            Partial: entries.Count(e => e.Status == AuditScriptStatus.Partial),
            Missing: entries.Count(e => e.Status == AuditScriptStatus.Missing),
            NoDdl: entries.Count(e => e.Status == AuditScriptStatus.NoDdl),
            WithParseErrors: entries.Count(e => e.ParseErrors > 0));

        var head = entries.Count == 0 ? 0 : entries[^1].Version;

        AuditVerdict verdict;
        if (highestApplied == 0)
        {
            verdict = startFrom > 1 ? AuditVerdict.NoAppliedVersionInRange : AuditVerdict.NotInitialised;
        }
        else if (highestApplied == head && pending.Count == 0)
        {
            verdict = AuditVerdict.AtHead;
        }
        else
        {
            verdict = AuditVerdict.BehindHead;
        }

        return (verdict, pending, drift, totals);
    }

    private static AuditScriptResult Probe(VersionScript script, DdlExtractor extractor, VersionVerifier verifier)
    {
        string raw;
        try
        {
            raw = File.ReadAllText(script.File.FullName);
        }
        catch (IOException ex)
        {
            return new AuditScriptResult(
                script.Number, script.Label, script.File.FullName,
                AuditScriptStatus.Missing, 0, 0, 0,
                new[] { new AuditObjectResult("Script", null, null, script.File.Name, "Unreadable", ex.Message) });
        }

        var extraction = extractor.Extract(raw);
        if (extraction.Objects.Count == 0)
        {
            return new AuditScriptResult(
                script.Number, script.Label, script.File.FullName,
                AuditScriptStatus.NoDdl, 0, 0, extraction.ParseErrors.Count,
                Array.Empty<AuditObjectResult>());
        }

        var report = verifier.Verify(script.Number, extraction.Objects, extraction.ParseErrors);

        var mismatches = report.Results
            .Where(r => r.Status != VerificationStatus.Matches)
            .Select(r => new AuditObjectResult(
                r.Object.Kind.ToString(),
                r.Object.Schema,
                r.Object.Parent,
                r.Object.Name,
                r.Status.ToString(),
                r.Detail))
            .ToList();

        var status = report.Verdict switch
        {
            VerificationVerdict.FullyApplied => AuditScriptStatus.Applied,
            VerificationVerdict.NotApplied   => AuditScriptStatus.Missing,
            VerificationVerdict.Partial      => AuditScriptStatus.Partial,
            _                                => AuditScriptStatus.NoDdl
        };

        return new AuditScriptResult(
            script.Number, script.Label, script.File.FullName,
            status, report.OkCount, report.Results.Count, extraction.ParseErrors.Count,
            mismatches);
    }
}
