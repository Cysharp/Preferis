using Preferis.Platform.Windows;

namespace Preferis.Platform.Tests.Windows;

public class SingleArgumentTests
{
    [Theory]
    [InlineData("\"C:\\Tools\\Preferis.exe\" --single-argument https://example.com/", "https://example.com/")]
    // Whatever the URL contains stays part of it; nothing after the marker is an option.
    [InlineData("\"C:\\Tools\\Preferis.exe\" --single-argument https://example.com/\" --config \"C:\\evil.json", "https://example.com/\" --config \"C:\\evil.json")]
    [InlineData("\"C:\\Tools\\Preferis.exe\" --single-argument https://example.com/a b", "https://example.com/a b")]
    [InlineData("C:\\Tools\\Preferis.exe --single-argument   https://example.com/  ", "https://example.com/")]
    [InlineData("\"C:\\Program Files\\Preferis\\Preferis.exe\" --single-argument \"https://example.com/\"", "https://example.com/")]
    [InlineData("\"C:\\Tools\\Preferis.exe\" --single-argument C:\\Users\\me\\page one.html", "C:\\Users\\me\\page one.html")]
    public void The_url_is_everything_after_the_marker(string commandLine, string expected)
    {
        Assert.Equal(expected, CommandLine.SingleArgument(commandLine));
    }

    [Theory]
    [InlineData("\"C:\\Tools\\Preferis.exe\" https://example.com/")]
    [InlineData("\"C:\\Tools\\Preferis.exe\" --single-argument")]
    [InlineData("\"C:\\Tools\\--single-argument\\Preferis.exe\" https://example.com/")]
    public void Without_the_marker_or_a_url_there_is_none(string commandLine)
    {
        Assert.Null(CommandLine.SingleArgument(commandLine));
    }
}
