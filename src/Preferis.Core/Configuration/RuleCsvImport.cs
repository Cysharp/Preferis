using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Preferis.Core.Routing;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Configuration;

/// <param name="Rules">New rules, one per profile set in first appearance order. They are not in the configuration yet.</param>
/// <param name="ImportedLines">Lines that became a condition of one of <paramref name="Rules"/>.</param>
/// <param name="Skipped">Lines that did not, in file order. A header line is not among them.</param>
public sealed record RuleImportResult(IReadOnlyList<Rule> Rules, int ImportedLines, IReadOnlyList<SkippedLine> Skipped);

/// <param name="Line">Line of the file the record starts on (1-based).</param>
public sealed record SkippedLine(int Line, string Reason);

/// <summary>
/// Rules from a CSV of "pattern, profile name, ..." lines, as other tools export them (docs/Configuration.md §2.12).
/// </summary>
public static partial class RuleCsvImport
{
    public static RuleImportResult Import(PreferisConfig config, string csv)
    {
        var groups = new List<(List<Profile> Profiles, List<Condition> Conditions)>();
        var skipped = new List<SkippedLine>();
        var imported = 0;
        var records = Records(csv);
        for (var i = 0; i < records.Count; i++)
        {
            var (line, fields) = records[i];
            if (Add(config, groups, fields) is not { } reason)
            {
                imported++;
            }
            // A first line that does not import is the header ("URL,Profile"); nobody needs to be told about it.
            else if (i > 0)
            {
                skipped.Add(new SkippedLine(line, reason));
            }
        }

        var ids = config.Rules.Select(r => r.Id).ToList();
        var names = config.Rules.Select(r => r.Name).ToList();
        var rules = new List<Rule>();
        foreach (var (profiles, conditions) in groups)
        {
            // Rule names follow the profile names' rule: an existing rule of the same name gets a number.
            var name = ProfileNames.Unique(L("Imported Rule for {0}", Names(profiles)), names);
            var id = IdGenerator.ForName(name, "rule", ids);
            names.Add(name);
            ids.Add(id);
            rules.Add(new Rule
            {
                Id = id,
                Name = name,
                Enabled = true,
                Condition = conditions.Count == 1 ? conditions[0] : new OrCondition { Conditions = [.. conditions] },
                Actions = [new OpenAction { Profiles = profiles.Select(p => p.Id).ToList() }],
            });
        }
        return new RuleImportResult(rules, imported, skipped);
    }

    /// <summary>Adds the line to its profile set's group. Returns why it cannot be imported, or null.</summary>
    private static string? Add(PreferisConfig config, List<(List<Profile> Profiles, List<Condition> Conditions)> groups, List<string> fields)
    {
        if (fields.Count < 2 || fields.Any(f => f.Length == 0))
        {
            return L("A pattern and at least one profile name are needed. Profile columns cannot be empty.");
        }
        var pattern = fields[0];
        var selected = new HashSet<Profile>();
        foreach (var name in fields.Skip(1))
        {
            if (config.Profiles.FirstOrDefault(p => ProfileNames.Same(p.Name, name)) is not { } profile)
            {
                return L("No profile is named “{0}”.", name);
            }
            selected.Add(profile);
        }
        if (ToCondition(pattern) is not { } condition)
        {
            return L("“{0}” is not a URL, domain, host:port, or regular expression starting with /.", pattern);
        }
        if (condition is UrlCondition { Match: MatchKind.Regex } regex &&
            (regex.Pattern.Length == 0 || PatternMatcher.ErrorOf(regex.Match, regex.Pattern, regex.IgnoreCase) is not null))
        {
            return L("“{0}” is not a valid regular expression.", pattern);
        }

        // The picker uses configuration order, so CSV column order does not create a different group.
        var profiles = config.Profiles.Where(selected.Contains).ToList();
        var index = groups.FindIndex(g => g.Profiles.SequenceEqual(profiles));
        if (index < 0)
        {
            groups.Add((profiles, []));
            index = groups.Count - 1;
        }
        var conditions = groups[index].Conditions;
        if (conditions.Any(c => SameCondition(c, condition)))
        {
            return L("“{0}” is already in the rule for “{1}”.", pattern, Names(profiles));
        }
        conditions.Add(condition);
        return null;
    }

    private static string Names(IEnumerable<Profile> profiles) => string.Join(", ", profiles.Select(p => p.Name.Trim()));

    private static bool SameCondition(Condition left, Condition right) => (left, right) switch
    {
        // Regex escapes such as \d and \D differ even when the regex matches without case.
        (PatternCondition a, PatternCondition b) => a.GetType() == b.GetType() && a.Match == b.Match &&
            string.Equals(a.Pattern, b.Pattern, a.Match == MatchKind.Regex ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase),
        (AndCondition a, AndCondition b) => a.Conditions.Count == b.Conditions.Count &&
            a.Conditions.Zip(b.Conditions).All(pair => SameCondition(pair.First, pair.Second)),
        _ => false,
    };

    private static Condition? ToCondition(string pattern)
    {
        // Only the first slash is a marker. A final slash is part of the expression, not a closing delimiter.
        if (pattern.StartsWith('/'))
        {
            return new UrlCondition { Match = MatchKind.Regex, Pattern = pattern[1..] };
        }
        if (Scheme().Match(pattern) is { Success: true } scheme)
        {
            var rest = pattern[scheme.Length..];
            if (rest.Length == 0)
            {
                return null;
            }
            // A normalized URL always has a path ("https://example.com/"), so a pattern without one would match nothing:
            // it means the whole site.
            return new UrlCondition { Match = MatchKind.Wildcard, Pattern = rest.IndexOfAny(['/', '?', '#']) < 0 ? pattern + "/*" : pattern };
        }
        var colon = pattern.LastIndexOf(':');
        if (colon >= 0)
        {
            var host = pattern[..colon];
            if (host.Length == 0 || host.Any(c => c is '/' or '@' or '#' or '\\' || char.IsWhiteSpace(c)) ||
                !ushort.TryParse(pattern[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            {
                return null;
            }
            if (host.Contains(':'))
            {
                if (!host.StartsWith('[') || !host.EndsWith(']') ||
                    !Uri.TryCreate("http://" + host, UriKind.Absolute, out var ipv6) || ipv6.HostNameType != UriHostNameType.IPv6)
                {
                    return null;
                }
                // RoutingUrl uses System.Uri's compressed IPv6 host as well.
                host = ipv6.Host;
            }

            // Host matching keeps IDN handling and wildcard semantics; the URL condition checks only the authority port.
            return new AndCondition
            {
                Conditions =
                [
                    new HostCondition { Match = MatchKind.Wildcard, Pattern = host },
                    new UrlCondition { Match = MatchKind.Regex, Pattern = PortPattern(port) },
                ],
            };
        }
        // A host has no path or spaces; such a pattern would never match one.
        return pattern.Any(c => c is '/' or ':' || char.IsWhiteSpace(c))
            ? null
            : new HostCondition { Match = MatchKind.Wildcard, Pattern = pattern };
    }

    private static string PortPattern(ushort port)
    {
        var authority = "[A-Za-z][A-Za-z0-9+.-]*://[^/?#]*:" + port.ToString(CultureInfo.InvariantCulture);
        // System.Uri removes these schemes' default ports from AbsoluteUri. A default branch must exclude explicit
        // non-default ports, while allowing user information and bracketed IPv6 hosts.
        var schemes = port switch
        {
            80 => "http|ws",
            443 => "https|wss",
            21 => "ftp",
            70 => "gopher",
            119 => "nntp",
            23 => "telnet",
            389 => "ldap",
            808 => @"net\.tcp",
            _ => null,
        };
        if (schemes is not null)
        {
            authority += $"""|(?:{schemes})://(?:[^/?#@]*@)?(?:\[[^\]]+\]|[^:/?#@]+)""";
        }
        return "^(?:" + authority + ")(?:[/?#]|$)";
    }

    /// <summary>
    /// The records of an RFC 4180 CSV with the line each starts on: quoted fields may hold commas, line breaks and doubled
    /// quotes. Fields are trimmed, empty lines dropped, a byte order mark ignored.
    /// </summary>
    private static List<(int Line, List<string> Fields)> Records(string csv)
    {
        var text = (csv.StartsWith('﻿') ? csv[1..] : csv).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var start = 1;
        var quoted = false;

        void EndField()
        {
            fields.Add(field.ToString().Trim());
            field.Clear();
        }

        void EndRecord()
        {
            EndField();
            if (fields is not [""])
            {
                records.Add((start, fields));
            }
            fields = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                line++;
            }
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
                continue;
            }
            switch (c)
            {
                // A quote opens a quoted field only at its start (spaces before it are dropped with the trimming).
                case '"' when field.ToString().Trim().Length == 0:
                    field.Clear();
                    quoted = true;
                    break;
                case ',':
                    EndField();
                    break;
                case '\n':
                    EndRecord();
                    start = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            EndRecord();
        }
        return records;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*://")]
    private static partial Regex Scheme();
}
