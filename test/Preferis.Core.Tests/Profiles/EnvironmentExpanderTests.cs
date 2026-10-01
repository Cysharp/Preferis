using Preferis.Core.Profiles;

namespace Preferis.Core.Tests.Profiles;

public class EnvironmentExpanderTests
{
    private static readonly Dictionary<string, string> Env = new()
    {
        ["LOCALAPPDATA"] = @"C:\Users\me\AppData\Local",
        ["HOME"] = "/Users/me",
    };

    private static string Expand(string input, Platform platform) => EnvironmentExpander.Expand(input, platform, name => Env.GetValueOrDefault(name), "/Users/me");

    [Fact]
    public void Windows_percent_variables_are_expanded()
    {
        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\Foo\foo.exe", Expand(@"%LOCALAPPDATA%\Programs\Foo\foo.exe", Platform.Windows));
    }

    [Fact]
    public void Unknown_windows_variables_are_left_alone()
    {
        Assert.Equal(@"%NOPE%\x", Expand(@"%NOPE%\x", Platform.Windows));
    }

    [Fact]
    public void Unix_dollar_variables_and_tilde_are_expanded()
    {
        Assert.Equal("/Users/me/bin/tool", Expand("$HOME/bin/tool", Platform.MacOS));
        Assert.Equal("/Users/me/bin/tool", Expand("${HOME}/bin/tool", Platform.MacOS));
        Assert.Equal("/Users/me/bin/tool", Expand("~/bin/tool", Platform.MacOS));
        Assert.Equal("/Users/me", Expand("~", Platform.MacOS));
    }

    [Fact]
    public void Tilde_in_the_middle_is_literal()
    {
        Assert.Equal("/opt/a~b", Expand("/opt/a~b", Platform.MacOS));
    }
}
