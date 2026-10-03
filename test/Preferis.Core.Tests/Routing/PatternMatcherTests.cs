using Preferis.Core.Configuration;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Routing;

public class PatternMatcherTests
{
    [Theory]
    [InlineData(MatchKind.Regex, @"^login\.microsoftonline\.com$")]
    [InlineData(MatchKind.Regex, "(unclosed")]
    [InlineData(MatchKind.Regex, "[z-a]")]
    [InlineData(MatchKind.Regex, @"\p{NoSuchCategory}")]
    [InlineData(MatchKind.Regex, "a{2,1}")]
    [InlineData(MatchKind.Regex, "*")]
    [InlineData(MatchKind.Regex, "")]
    // Constructs the non-backtracking engine does not support: valid, compiled with the backtracking one.
    [InlineData(MatchKind.Regex, @"(?<=example)\.com")]
    [InlineData(MatchKind.Regex, @"(a)\1")]
    [InlineData(MatchKind.Regex, "(?>a+)b")]
    [InlineData(MatchKind.Regex, "(a)?(?(1)b|c)")]
    [InlineData(MatchKind.Wildcard, "*.example.[com")]
    [InlineData(MatchKind.Wildcard, "(")]
    [InlineData(MatchKind.Exact, "(")]
    [InlineData(MatchKind.StartsWith, "[")]
    [InlineData(MatchKind.EndsWith, ")")]
    [InlineData(MatchKind.Contains, "\\")]
    [InlineData((MatchKind)42, "x")]
    public void ErrorOf_finds_the_error_Create_reports(MatchKind kind, string pattern)
    {
        foreach (var ignoreCase in new[] { true, false })
        {
            Assert.Equal(PatternMatcher.Create(kind, pattern, ignoreCase).Error, PatternMatcher.ErrorOf(kind, pattern, ignoreCase));
        }
    }
}
