using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiskSnap.Core.Scanning;

public static class ScanRules
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true
    };
    private static readonly IReadOnlyList<ScanRule> DefaultRuleList = Array.AsReadOnly(new[]
    {
        Rule("winsxs", @"Windows\WinSxS", "exclude", false, "Windows component store", "Hard-link heavy, so totals are misleading. Do not delete files by hand. Use Dism.exe /Online /Cleanup-Image /StartComponentCleanup from an elevated prompt."),
        Rule("sysvolinfo", "System Volume Information", "exclude", false, "System Volume Information", "Restore points and shadow copies. Check with vssadmin list shadowstorage; limit via System Protection settings. Usually unreadable even as admin; shows up as unaccounted space."),
        Rule("winupdate", @"Windows\SoftwareDistribution\Download", "track", false, "Windows Update downloads", "Windows Update download cache. Settings > Storage > Temporary files, or Disk Cleanup > Windows Update Cleanup."),
        Rule("windows-old", "Windows.old", "track", false, "Previous Windows installation", "Previous Windows installation. Remove through Disk Cleanup > Previous Windows installation(s)."),
        Rule("hiberfil", "hiberfil.sys", "track", false, "Hibernation file", "powercfg /h off disables hibernation and removes it (also disables Fast Startup)."),
        Rule("pagefile", "pagefile.sys", "track", false, "Virtual memory", "Adjust in System Properties > Advanced > Performance > Virtual memory."),
        Rule("pagefile", "swapfile.sys", "track", false, "Virtual memory", "Adjust in System Properties > Advanced > Performance > Virtual memory."),
        Rule("recycle", "$Recycle.Bin", "track", false, "Recycle Bin", "Empty the Recycle Bin."),
        Rule("temp", @"Windows\Temp", "track", false, "Temporary files", "Settings > Storage > Temporary files."),
        Rule("temp", @"Users\*\AppData\Local\Temp", "track", false, "Temporary files", "Settings > Storage > Temporary files."),
        Rule("vhdx", "*.vhdx", "track", false, "Virtual disk image", "WSL or Docker virtual disk. Grows but does not shrink by itself. For WSL, wsl --shutdown then wsl --manage <distro> --set-sparse true (verify availability), or compact the disk image.")
    });

    public static IReadOnlyList<ScanRule> Defaults => DefaultRuleList;

    public static async Task<IReadOnlyList<ScanRule>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var rules = await JsonSerializer.DeserializeAsync<List<ScanRule>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The rules file does not contain a JSON array.");
        Validate(rules);
        return rules.AsReadOnly();
    }

    public static async Task CreateDefaultFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The rules file must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        await using var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous);
        await WriteDefaultsAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    internal static Task WriteDefaultsAsync(Stream stream, CancellationToken cancellationToken) =>
        JsonSerializer.SerializeAsync(stream, DefaultRuleList, JsonOptions, cancellationToken);

    internal static void Validate(IReadOnlyList<ScanRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) ||
                string.IsNullOrWhiteSpace(rule.Pattern) ||
                string.IsNullOrWhiteSpace(rule.Title) ||
                string.IsNullOrWhiteSpace(rule.Guidance))
            {
                throw new InvalidDataException("Each scan rule must include an id, pattern, title, and guidance.");
            }

            if (!string.Equals(rule.Action, "exclude", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(rule.Action, "track", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Scan rule '{rule.Id}' has unsupported action '{rule.Action}'.");
            }
        }
    }

    internal sealed record CompiledRule(ScanRule Rule, Regex Matcher, string LiteralPrefix, string LiteralSuffix);

    internal static IReadOnlyList<CompiledRule> Compile(IReadOnlyList<ScanRule> rules) => rules
        .Select(rule =>
        {
            var regexPattern = "^" + Regex.Escape(rule.Pattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal) + "$";
            var firstWildcard = rule.Pattern.IndexOfAny(['*', '?']);
            var lastWildcard = rule.Pattern.LastIndexOfAny(['*', '?']);
            var literalPrefix = firstWildcard < 0 ? rule.Pattern : rule.Pattern[..firstWildcard];
            var literalSuffix = lastWildcard < 0 ? string.Empty : rule.Pattern[(lastWildcard + 1)..];
            return new CompiledRule(
                rule,
                new Regex(
                    regexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
                    TimeSpan.FromSeconds(1)),
                literalPrefix,
                literalSuffix);
        })
        .ToArray();

    internal static ScanRule? FindMatch(IReadOnlyList<CompiledRule> rules, string relativePath)
    {
        foreach (var compiledRule in rules)
        {
            if ((compiledRule.LiteralPrefix.Length == 0 ||
                    relativePath.StartsWith(compiledRule.LiteralPrefix, StringComparison.OrdinalIgnoreCase)) &&
                (compiledRule.LiteralSuffix.Length == 0 ||
                    relativePath.EndsWith(compiledRule.LiteralSuffix, StringComparison.OrdinalIgnoreCase)) &&
                compiledRule.Matcher.IsMatch(relativePath))
            {
                return compiledRule.Rule;
            }
        }

        return null;
    }

    private static ScanRule Rule(string id, string pattern, string action, bool measure, string title, string guidance) => new()
    {
        Id = id,
        Pattern = pattern,
        Action = action,
        Measure = measure,
        Title = title,
        Guidance = guidance
    };
}
