using System.Globalization;
using System.Text;
using DiskSnap.Core.Comparison;
using DiskSnap.Core.Formatting;
using DiskSnap.Core.Snapshots;

namespace DiskSnap.Core.Reporting;

public static class ComparisonReportGenerator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] ScopeCaveats =
    [
        "Sizes are allocated bytes; the small-files aggregate can combine files and threshold crossings.",
        "Unknown file observations may reflect folding, exclusions, or skipped paths; any lower bound assumes ordinary-file folding.",
        "Cloud-placeholder directories are not enumerated."
    ];

    public static string Generate(SnapshotComparison comparison, ReportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        options ??= new ReportOptions();
        ArgumentOutOfRangeException.ThrowIfNegative(options.TopGrowerRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxUtf8Bytes);

        var allGrowers = comparison.TopGrowers()
            .OrderByDescending(Rank)
            .ThenBy(node => node.Path, StringComparer.Ordinal)
            .ToArray();
        var rows = allGrowers.Take(options.TopGrowerRows).ToList();
        var ancestorSections = rows.Take(10).Select(BuildAncestorSection).ToList();
        var excludedRules = comparison.Nodes
            .Where(node => node.OlderNode?.Kind == "x" && node.OlderNode.FileCount is null ||
                           node.NewerNode?.Kind == "x" && node.NewerNode.FileCount is null)
            .SelectMany(node => new[] { node.OlderNode, node.NewerNode })
            .Where(node => node?.Kind == "x" && node.FileCount is null && node.RuleId is not null)
            .Select(node => node!.RuleId!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var omittedRowsForLimit = Math.Max(0, allGrowers.Length - rows.Count);
        var omittedRowsForCap = 0;
        var omittedAncestorsForCap = 0;
        var caveatRuleLimit = excludedRules.Length;
        var shortenScopeCaveats = false;

        string Compose()
        {
            var parts = new List<string>
            {
                BuildHeader(comparison),
                BuildSummary(comparison),
                BuildGrowerTable(rows, omittedRowsForLimit),
                string.Join("\n\n", ancestorSections),
                BuildCaveats(comparison, excludedRules, caveatRuleLimit, shortenScopeCaveats),
                BuildOmissionNote(omittedAncestorsForCap, omittedRowsForCap, caveatRuleLimit, excludedRules.Length, shortenScopeCaveats)
            };
            return string.Join("\n\n", parts.Where(part => part.Length > 0)) + "\n";
        }

        var output = Compose();
        while (Utf8Length(output) > options.MaxUtf8Bytes && ancestorSections.Count > 0)
        {
            ancestorSections.RemoveAt(ancestorSections.Count - 1);
            omittedAncestorsForCap++;
            output = Compose();
        }

        while (Utf8Length(output) > options.MaxUtf8Bytes && rows.Count > 0)
        {
            rows.RemoveAt(rows.Count - 1);
            omittedRowsForCap++;
            output = Compose();
        }

        while (Utf8Length(output) > options.MaxUtf8Bytes && caveatRuleLimit > 0)
        {
            caveatRuleLimit--;
            output = Compose();
        }

        if (Utf8Length(output) > options.MaxUtf8Bytes && !shortenScopeCaveats)
        {
            shortenScopeCaveats = true;
            output = Compose();
        }

        if (Utf8Length(output) > options.MaxUtf8Bytes)
            throw new InvalidOperationException("The report's required header, summary, and caveats exceed the configured UTF-8 byte limit.");

        return output;
    }

    private static string BuildHeader(SnapshotComparison comparison)
    {
        var older = comparison.Older.Header;
        var newer = comparison.Newer.Header;
        var olderUsed = older.VolumeTotal - older.VolumeFree;
        var newerUsed = newer.VolumeTotal - newer.VolumeFree;
        return string.Join("\n",
            "# DiskSnap comparison report",
            "",
            "## Comparison",
            $"- Drive: {Escape(older.Drive)}",
            $"- Volume serial: {Escape(older.VolumeSerial)}",
            $"- Older snapshot (UTC): {FormatUtc(older.Utc)}",
            $"- Newer snapshot (UTC): {FormatUtc(newer.Utc)}",
            $"- Directory threshold (older / newer): {FormatSize(older.DirectoryThreshold)} / {FormatSize(newer.DirectoryThreshold)}",
            $"- File threshold (older / newer): {FormatSize(older.FileThreshold)} / {FormatSize(newer.FileThreshold)}",
            $"- Older volume: used {FormatSize(olderUsed)}, free {FormatSize(older.VolumeFree)}, unaccounted {FormatSize(olderUsed - comparison.Root.OlderSize!.Value)}",
            $"- Newer volume: used {FormatSize(newerUsed)}, free {FormatSize(newer.VolumeFree)}, unaccounted {FormatSize(newerUsed - comparison.Root.NewerSize!.Value)}");
    }

    private static string BuildSummary(SnapshotComparison comparison)
    {
        var counts = comparison.Nodes.GroupBy(node => node.Presence).ToDictionary(group => group.Key, group => group.Count());
        var newCount = counts.GetValueOrDefault(NodePresence.New);
        var removedCount = counts.GetValueOrDefault(NodePresence.Removed);
        var unknownCount = counts.GetValueOrDefault(NodePresence.Unknown);
        return string.Join("\n",
            "## Summary",
            $"- Total delta: {FormatSigned(comparison.Root.Delta)}",
            $"- New nodes: {newCount.ToString(Invariant)}",
            $"- Removed nodes: {removedCount.ToString(Invariant)}",
            $"- Unknown-presence nodes: {unknownCount.ToString(Invariant)}");
    }

    private static string BuildGrowerTable(IReadOnlyList<ComparisonNode> rows, int omittedForLimit)
    {
        var lines = new List<string>
        {
            "## Top growers",
            "",
            "| Rank | Path | Older | Newer | Delta | Delta % | Flags |",
            "| ---: | --- | --- | --- | --- | ---: | --- |"
        };
        for (var index = 0; index < rows.Count; index++)
        {
            var node = rows[index];
            lines.Add($"| {(index + 1).ToString(Invariant)} | {Escape(node.Path)} | {FormatSize(node.OlderSize)} | {FormatSize(node.NewerSize)} | {FormatNodeDelta(node)} | {FormatPercent(node.PercentChange)} | {Escape(string.Join(", ", GetFlags(node)))} |");
        }
        if (omittedForLimit > 0)
            lines.Add($"\n_{omittedForLimit.ToString(Invariant)} top-grower rows omitted by the configured row limit._");
        return string.Join("\n", lines);
    }

    private static string BuildAncestorSection(ComparisonNode grower)
    {
        var chain = new Stack<ComparisonNode>();
        for (var current = grower; current is not null; current = current.Parent)
            chain.Push(current);

        var lines = new List<string> { $"### Ancestors: {Escape(grower.Path)}" };
        while (chain.Count > 0)
        {
            var node = chain.Pop();
            var path = node.Path.Length == 0 ? "(drive root)" : node.Path;
            var line = $"- {Escape(path)}: {FormatNodeDelta(node)}";
            var guidance = GetGuidance(node);
            if (guidance.Length > 0)
                line += $"; guidance: {Escape(guidance)}";
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    private static string BuildCaveats(SnapshotComparison comparison, IReadOnlyList<string> ruleIds, int ruleLimit, bool shortenScope)
    {
        var olderSkipped = comparison.Older.Header.Skipped.Count.ToString(Invariant);
        var newerSkipped = comparison.Newer.Header.Skipped.Count.ToString(Invariant);
        var lines = new List<string>
        {
            "## Caveats",
            $"- Skipped folders: older {olderSkipped}; newer {newerSkipped}. Sample paths are not included."
        };

        if (ruleIds.Count > 0)
        {
            var shown = ruleIds.Take(ruleLimit).Select(Escape).ToArray();
            var omitted = ruleIds.Count - shown.Length;
            var suffix = omitted > 0 ? $"; {omitted.ToString(Invariant)} rule ids omitted" : string.Empty;
            lines.Add($"- Unmeasured excluded rule ids: {(shown.Length > 0 ? string.Join(", ", shown) : "none")}{suffix}.");
        }
        else
        {
            lines.Add("- Unmeasured excluded rule ids: none.");
        }

        if (shortenScope)
        {
            lines.Add("- Scope caveats shortened; 3 notes omitted.");
        }
        else
        {
            lines.AddRange(ScopeCaveats.Select(note => $"- {note}"));
        }
        return string.Join("\n", lines);
    }

    private static string BuildOmissionNote(int ancestors, int rows, int ruleLimit, int ruleCount, bool scopeShortened)
    {
        var items = new List<string>();
        if (ancestors > 0)
            items.Add($"{ancestors.ToString(Invariant)} ancestor sections");
        if (rows > 0)
            items.Add($"{rows.ToString(Invariant)} top-grower rows");
        if (ruleCount > ruleLimit)
            items.Add($"{(ruleCount - ruleLimit).ToString(Invariant)} excluded-rule ids");
        if (scopeShortened)
            items.Add("3 scope caveats");
        return items.Count == 0 ? string.Empty : $"## Truncation\n- Omitted to fit the UTF-8 byte limit: {string.Join(", ", items)}.";
    }

    private static string[] GetFlags(ComparisonNode node)
    {
        var flags = new List<string>();
        if (node.Presence == NodePresence.New)
            flags.Add("new");
        if (node.Observation == FileObservation.NewlyObserved)
            flags.Add("newly observed");
        if (node.OlderNode?.Kind == "g" || node.NewerNode?.Kind == "g")
            flags.Add("small files");

        flags.AddRange(new[] { node.OlderNode?.RuleId, node.NewerNode?.RuleId }
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => $"rule:{id}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal));

        if (HasFlag(node, "cloud"))
            flags.Add("cloud");
        if (HasFlag(node, "hardlink"))
            flags.Add("hardlink");
        return flags.ToArray();
    }

    private static bool HasFlag(ComparisonNode node, string flag) =>
        (node.OlderNode?.Flags?.Contains(flag, StringComparer.Ordinal) ?? false) ||
        (node.NewerNode?.Flags?.Contains(flag, StringComparer.Ordinal) ?? false);

    private static string GetGuidance(ComparisonNode node) => string.Join("; ",
        node.OlderGuidance.Concat(node.NewerGuidance)
            .Select(rule => $"{rule.Title} ({rule.Id}): {rule.Guidance}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal));

    private static long Rank(ComparisonNode node) => node.Delta is long delta
        ? Math.Abs(delta)
        : node.NewerSize ?? 0;

    private static string FormatNodeDelta(ComparisonNode node)
    {
        if (node.Delta is long delta)
            return FormatSigned(delta);
        if (node.FoldedFileChangeLowerBound is long bound)
        {
            var signed = node.Observation == FileObservation.NoLongerObserved ? -bound : bound;
            return $"at least {FormatSigned(signed)} (assuming ordinary-file folding)";
        }
        return "unknown";
    }

    private static string FormatSigned(long? bytes)
    {
        if (!bytes.HasValue)
            return "unknown";
        var sign = bytes.Value > 0 ? "+" : bytes.Value < 0 ? "-" : string.Empty;
        var magnitude = Math.Abs(bytes.Value);
        var signedBytes = bytes.Value.ToString("+#,0;-#,0;0", Invariant);
        return $"{sign}{SizeFormatter.Format(magnitude, Invariant)} ({signedBytes} bytes)";
    }

    private static string FormatPercent(double? percent) =>
        percent.HasValue ? $"{percent.Value.ToString("+0.##;-0.##;+0", Invariant)}%" : "unknown";

    private static string FormatSize(long? bytes) => bytes.HasValue
        ? $"{SizeFormatter.Format(bytes, Invariant)} ({bytes.Value.ToString("#,0", Invariant)} bytes)"
        : "unknown";

    private static string FormatUtc(string value)
    {
        var timestamp = DateTimeOffset.Parse(value, Invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", Invariant);
    }

    private static string Escape(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character == '\r') result.Append("\\r");
            else if (character == '\n') result.Append("\\n");
            else if (character == '\t') result.Append("\\t");
            else if (char.IsControl(character)) result.Append("\\u").Append(((int)character).ToString("X4", Invariant));
            else if (character == '&') result.Append("&amp;");
            else if (character == '<') result.Append("&lt;");
            else if (character == '>') result.Append("&gt;");
            else if (character is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '#' or '+' or '-' or '.' or '!' or '|')
                result.Append('\\').Append(character);
            else result.Append(character);
        }
        return result.ToString();
    }

    private static int Utf8Length(string value) => Encoding.UTF8.GetByteCount(value);
}
