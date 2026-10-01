using Preferis.Platform.MacOS;

namespace Preferis.Platform.Tests.MacOS;

public class DefaultHandlerRequestTests
{
    // Simulates Launch Services: which schemes Preferis handles, and what each kind of request does.
    private sealed class FakeSystem
    {
        public HashSet<string> Default { get; } = new(StringComparer.Ordinal);
        public List<string> Requested { get; } = [];
        public List<string> Legacy { get; } = [];
        public Func<string, string?> OnRequest { get; set; } = _ => null;
        public Func<string, int> OnLegacy { get; set; } = _ => -54;

        public string? Run(params string[] schemes) => DefaultHandlerRequest.Run(
            schemes,
            Default.Contains,
            scheme => { Requested.Add(scheme); return OnRequest(scheme); },
            scheme => { Legacy.Add(scheme); return OnLegacy(scheme); });
    }

    [Fact]
    public void Schemes_already_handled_are_not_requested_again()
    {
        var system = new FakeSystem();
        system.Default.UnionWith(["http", "https"]);

        Assert.Null(system.Run("http", "https"));
        Assert.Empty(system.Requested);
    }

    [Fact]
    public void Confirming_the_browser_change_once_covers_https_too()
    {
        // macOS switches the default web browser as a whole after the user confirms the first request.
        var system = new FakeSystem();
        system.OnRequest = _ =>
        {
            system.Default.UnionWith(["http", "https"]);
            return null;
        };

        Assert.Null(system.Run("http", "https"));
        Assert.Equal(["http"], system.Requested);
    }

    [Fact]
    public void A_request_that_succeeds_is_trusted_before_launch_services_shows_it()
    {
        var system = new FakeSystem();

        Assert.Null(system.Run("http"));
        Assert.Empty(system.Legacy);
    }

    [Fact]
    public void An_error_is_ignored_when_the_scheme_ended_up_handled_anyway()
    {
        var system = new FakeSystem();
        system.OnRequest = scheme =>
        {
            system.Default.Add(scheme);
            return "The file couldn't be opened.";
        };

        Assert.Null(system.Run("http", "https"));
        Assert.Empty(system.Legacy);
    }

    [Fact]
    public void The_legacy_call_is_tried_when_the_request_did_not_take_effect()
    {
        var system = new FakeSystem();
        system.OnRequest = _ => "declined";
        system.OnLegacy = scheme =>
        {
            system.Default.Add(scheme);
            return 0;
        };

        Assert.Null(system.Run("http"));
        Assert.Equal(["http"], system.Legacy);
    }

    [Fact]
    public void A_failure_is_reported_only_when_the_scheme_is_still_not_handled()
    {
        var system = new FakeSystem();
        system.OnRequest = _ => "The user declined.";

        var error = system.Run("http", "https");

        Assert.Equal("Could not become the default handler for http: The user declined. (fallback LSSetDefaultHandlerForURLScheme returned -54)", error);
        Assert.Equal(["http"], system.Requested);
    }
}
