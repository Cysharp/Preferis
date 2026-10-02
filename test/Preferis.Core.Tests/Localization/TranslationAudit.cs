using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Preferis.Tests.Localization;

/// <param name="HasArguments">Called as a format (<c>L("… {0}", value)</c>) rather than as plain text.</param>
/// <param name="Location">File and line, for the failure message.</param>
internal sealed record SourceText(string Text, bool HasArguments, string Location);

/// <summary>
/// Checks a translation table against the code that uses it (docs/Design.md §9.4): every <c>L("…")</c> in the sources
/// needs a translation, every translation an <c>L("…")</c>, and both must take the same arguments. The texts are read from
/// the sources rather than from a running UI, so the macOS projects, which do not build on Windows, are checked too.
/// Shared by the test projects of the assemblies that have a table.
/// </summary>
internal static partial class TranslationAudit
{
    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Preferis.slnx")))
                {
                    return directory.FullName;
                }
            }
            throw new InvalidOperationException("The repository root (Preferis.slnx) was not found above the test's directory.");
        }
    }

    /// <summary>
    /// Every <c>L("…")</c> in the C# files of <paramref name="projects"/> (folders under <c>src</c>). The tables
    /// themselves (the <c>Localization</c> folders) are not uses. Calls without a single string literal are reported in
    /// <paramref name="problems"/>: their text cannot be checked, and would not be found in the table at run time either.
    /// </summary>
    public static IReadOnlyList<SourceText> FindTexts(IEnumerable<string> projects, List<string> problems)
    {
        var texts = new List<SourceText>();
        foreach (var project in projects)
        {
            var root = Path.Combine(RepositoryRoot, "src", project);
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/Localization/", StringComparison.Ordinal))
                {
                    continue;
                }
                var source = File.ReadAllText(file);
                foreach (Match call in Call().Matches(source))
                {
                    var location = $"{relative}:{LineOf(source, call.Index)}";
                    var literal = Literal().Match(source, call.Index + call.Length);
                    if (!literal.Success || literal.Index != call.Index + call.Length)
                    {
                        problems.Add($"{location}: L must be given one string literal (a format with arguments after it), not an expression.");
                        continue;
                    }
                    texts.Add(new SourceText(Unescape(literal.Groups["text"].Value), literal.Groups["next"].Value == ",", location));
                }
            }
        }
        return texts;
    }

    /// <summary>What is wrong with <paramref name="translations"/> for <paramref name="texts"/>; empty when nothing is.</summary>
    public static List<string> Check(IReadOnlyList<SourceText> texts, IReadOnlyDictionary<string, string> translations)
    {
        var problems = new List<string>();
        foreach (var text in texts)
        {
            if (!translations.TryGetValue(text.Text, out var translation))
            {
                problems.Add($"{text.Location}: no translation for \"{text.Text}\".");
                continue;
            }
            problems.AddRange(CheckPair(text, translation).Select(problem => $"{text.Location}: {problem}"));
        }

        var used = texts.Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(translations.Keys.Where(key => !used.Contains(key)).Select(key => $"The translation of \"{key}\" is not used any more."));
        return problems;
    }

    private static IEnumerable<string> CheckPair(SourceText text, string translation)
    {
        if (translation.Trim().Length == 0)
        {
            yield return $"the translation of \"{text.Text}\" is empty.";
        }

        var english = Placeholders(text.Text);
        var translated = Placeholders(translation);
        if (!text.HasArguments && english.Count > 0)
        {
            yield return $"\"{text.Text}\" has placeholders but no arguments: L shows it as it is.";
        }
        if (!english.SetEquals(translated))
        {
            yield return $"\"{text.Text}\" and its translation \"{translation}\" have different placeholders.";
        }
        if (text.HasArguments)
        {
            foreach (var format in new[] { text.Text, translation })
            {
                string? error = null;
                try
                {
                    _ = string.Format(CultureInfo.InvariantCulture, format, Enumerable.Repeat<object?>("x", 10).ToArray());
                }
                catch (FormatException ex)
                {
                    error = ex.Message;
                }
                if (error is not null)
                {
                    yield return $"\"{format}\" is not a valid format: {error}";
                }
            }
        }
        // Words in braces are kept as they are ("{url}" in the hints of command arguments).
        foreach (var token in Tokens().Matches(text.Text).Select(m => m.Value).Where(t => !translation.Contains(t, StringComparison.Ordinal)))
        {
            yield return $"the translation of \"{text.Text}\" lost {token}.";
        }
    }

    private static HashSet<string> Placeholders(string text)
        => Placeholder().Matches(text.Replace("{{", "", StringComparison.Ordinal)).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;

    private static string Unescape(string literal)
    {
        var text = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] != '\\')
            {
                text.Append(literal[i]);
                continue;
            }
            var c = literal[++i];
            switch (c)
            {
                case 'n':
                    text.Append('\n');
                    break;
                case 't':
                    text.Append('\t');
                    break;
                case 'r':
                    text.Append('\r');
                    break;
                case '0':
                    text.Append('\0');
                    break;
                case 'u':
                    text.Append((char)int.Parse(literal.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }
        return text.ToString();
    }

    // A call of L, not a member or a method named ...L.
    [GeneratedRegex(@"(?<![\w.])L\(")]
    private static partial Regex Call();

    // A regular string literal and what follows it: "," for a format with arguments, ")" for plain text.
    [GeneratedRegex(@"\G""(?<text>(?:[^""\\\r\n]|\\.)*)""\s*(?<next>[,)])")]
    private static partial Regex Literal();

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{[A-Za-z]+\}")]
    private static partial Regex Tokens();
}
