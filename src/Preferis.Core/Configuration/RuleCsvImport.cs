using System.Text;
using System.Text.RegularExpressions;

namespace Preferis.Core.Configuration;

/// <param name="Rules">New rules, one per profile in the order the profiles first appear. They are not in the configuration yet.</param>
/// <param name="ImportedLines">Lines that became a condition of one of <paramref name="Rules"/>.</param>
/// <param name="Skipped">Lines that did not, in file order. A header line is not among them.</param>
public sealed record RuleImportResult(IReadOnlyList<Rule> Rules, int ImportedLines, IReadOnlyList<SkippedLine> Skipped);

/// <param name="Line">Line of the file the record starts on (1-based).</param>
public sealed record SkippedLine(int Line, string Reason);

/// <summary>
/// Rules from a CSV of "URL or domain, profile name" lines, as other tools export them (docs/Configuration.md §2.12).
/// </summary>
public static partial class RuleCsvImport
{
    public static RuleImportResult Import(PreferisConfig config, string csv)
    {
        var groups = new List<(Profile Profile, List<PatternCondition> Conditions)>();
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
        foreach (var (profile, conditions) in groups)
        {
            // Rule names follow the profile names' rule: an existing rule of the same name gets a number.
            var name = ProfileNames.Unique($"Imported Rule for {profile.Name.Trim()}", names);
            var id = IdGenerator.ForName(name, "rule", ids);
            names.Add(name);
            ids.Add(id);
            rules.Add(new Rule
            {
                Id = id,
                Name = name,
                Enabled = true,
                Condition = conditions.Count == 1 ? conditions[0] : new OrCondition { Conditions = [.. conditions] },
                Actions = [new OpenAction { Profiles = [profile.Id] }],
            });
        }
        return new RuleImportResult(rules, imported, skipped);
    }

    /// <summary>Adds the line to its profile's group. Returns why it cannot be imported, or null.</summary>
    private static string? Add(PreferisConfig config, List<(Profile Profile, List<PatternCondition> Conditions)> groups, List<string> fields)
    {
        if (fields.Count < 2 || fields[0].Length == 0 || fields[1].Length == 0)
        {
            return "A URL or domain and a profile name are needed.";
        }
        var (pattern, name) = (fields[0], fields[1]);
        if (config.Profiles.FirstOrDefault(p => ProfileNames.Same(p.Name, name)) is not { } profile)
        {
            return $"No profile is named “{name}”.";
        }
        if (ToCondition(pattern) is not { } condition)
        {
            return $"“{pattern}” is neither a URL (it would start with a scheme such as https://) nor a domain.";
        }

        var index = groups.FindIndex(g => ReferenceEquals(g.Profile, profile));
        if (index < 0)
        {
            groups.Add((profile, []));
            index = groups.Count - 1;
        }
        var conditions = groups[index].Conditions;
        // Both kinds of condition compare without case here: hosts always, URLs by default.
        if (conditions.Any(c => c.GetType() == condition.GetType() && string.Equals(c.Pattern, condition.Pattern, StringComparison.OrdinalIgnoreCase)))
        {
            return $"“{pattern}” is already in the rule for “{profile.Name.Trim()}”.";
        }
        conditions.Add(condition);
        return null;
    }

    /// <summary>A <c>url</c> condition when the pattern starts with a scheme, a <c>host</c> condition otherwise; both wildcards.</summary>
    private static PatternCondition? ToCondition(string pattern)
    {
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
        // A host has no path, port or spaces; such a pattern would never match one.
        return pattern.Any(c => c is '/' or ':' || char.IsWhiteSpace(c))
            ? null
            : new HostCondition { Match = MatchKind.Wildcard, Pattern = pattern };
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
