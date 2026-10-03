using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Core.Picker;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Hosting;

/// <summary>The Router with the configuration read from a real file, as it is on a machine.</summary>
public sealed class RoutingServiceConfigFileTests : IDisposable
{
    private const string Chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
    private const string Safari = "/Applications/Safari.app/Contents/MacOS/Safari";

    private readonly string _root = Directory.CreateTempSubdirectory("preferis-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private FakeHost HostReading(string configJson)
    {
        var paths = new ConfigPaths(Path.Combine(_root, "local.json"), Path.Combine(_root, "config.json"), Path.Combine(_root, "logs"));
        File.WriteAllText(paths.DefaultConfigPath, configJson);
        var host = new FakeHost().WithBrowser("chrome", Chrome, "/Applications/Google Chrome.app").WithBrowser("safari", Safari, "/Applications/Safari.app");
        host.ConfigStore = new ConfigLoader(paths, HostPlatform.MacOS, _ => null, _root);
        return host;
    }

    // Valid JSON that the Router cannot route with: it recovers as from a file it cannot read (docs/Design.md §8 step 1)
    // rather than failing on the null halfway through and dropping the URL.
    [Theory]
    [InlineData("""{ "profiles": null }""")]
    [InlineData("""{ "rules": null }""")]
    [InlineData("""{ "catchAll": null }""")]
    [InlineData("""{ "catchAll": { "actions": [ null ] } }""")]
    [InlineData("""{ "rules": [ null ] }""")]
    [InlineData("""{ "profiles": [ { "id": null, "name": "Edge", "browser": "edge" } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": { "type": "or", "conditions": null }, "actions": [ { "type": "none" } ] } ] }""")]
    public void A_config_with_null_where_a_value_is_needed_offers_the_detected_browsers_with_the_reason(string json)
    {
        var host = HostReading(json);
        host.PickerAnswer = _ => new PickerResponse { Result = PickerResult.Selected, ProfileId = "browser:chrome" };

        var result = host.CreateService().Handle(RoutingUrl.Parse("https://example.com/"));

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        var request = Assert.Single(host.PickerRequests);
        Assert.Contains("config.json", request.Message);
        Assert.Equal(["browser:chrome", "browser:safari"], request.Candidates.Select(c => c.ProfileId));
        Assert.Equal(Chrome, Assert.Single(host.Launched).Path);
        Assert.Contains(host.Log, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("config.json", StringComparison.Ordinal));
    }
}
