using System.Text.Json;
using Preferis.Core.Configuration;
using Preferis.Core.Picker;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Picker;

public class PickerMessagesTests
{
    private static PreferisConfig Sample() => ConfigSerializer.Deserialize(Fixtures.ConfigSample);

    private static RoutingDecision.Open Decide(PreferisConfig config, string url)
        => Assert.IsType<RoutingDecision.Open>(new Router(config).Route(RoutingUrl.Parse(url)).Decision);

    [Fact]
    public void Request_is_built_from_an_open_decision()
    {
        var config = Sample();
        var decision = Decide(config, "https://login.microsoftonline.com/common");

        var request = PickerMessages.CreateRequest(config, decision, Platform.MacOS, message: null);

        Assert.Equal("https://login.microsoftonline.com/common", request.Url);
        Assert.Equal(["edge", "chrome-work", "chrome-default"], request.Candidates.Select(c => c.ProfileId));
        var work = request.Candidates[1];
        Assert.Equal("Chrome (Work)", work.Name);
        Assert.Equal("chrome", work.Browser);
        Assert.Equal("Profile 1", work.BrowserProfile?.Directory);
        Assert.Equal("Work", work.BrowserProfile?.Name);
        Assert.False(work.Private);
        Assert.Equal("cursor", request.Options.Position);
        Assert.Null(request.Message);
    }

    [Fact]
    public void Request_uses_the_platform_specific_browser_id_and_private_flag()
    {
        var config = Sample();
        var decision = new RoutingDecision.Open(RoutingUrl.Parse("https://x.example.com/"), ["system-browser", "edge-private"], OpenSource.Explicit, null);

        var request = PickerMessages.CreateRequest(config, decision, Platform.MacOS, message: "設定ファイルを読み込めませんでした");

        Assert.Equal("safari", request.Candidates[0].Browser);
        Assert.True(request.Candidates[1].Private);
        Assert.Equal("設定ファイルを読み込めませんでした", request.Message);
    }

    [Fact]
    public void Request_round_trips_through_json()
    {
        var config = Sample();
        var request = PickerMessages.CreateRequest(config, Decide(config, "https://login.microsoftonline.com/"), Platform.Windows, null);

        var json = PickerMessages.Serialize(request);
        var parsed = PickerMessages.DeserializeRequest(json);

        Assert.NotNull(parsed);
        Assert.Equal(request.Url, parsed.Url);
        Assert.Equal(request.Candidates.Select(c => c.ProfileId), parsed.Candidates.Select(c => c.ProfileId));
        Assert.Contains("\"profileId\": \"chrome-work\"", json);
        Assert.DoesNotContain("version", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_valid_selection_is_accepted()
    {
        var config = Sample();
        var request = PickerMessages.CreateRequest(config, Decide(config, "https://login.microsoftonline.com/"), Platform.Windows, null);

        var response = PickerMessages.TryParseResponse("""{ "result": "selected", "profileId": "chrome-work", "remember": true }""", request);

        Assert.NotNull(response);
        Assert.Equal(PickerResult.Selected, response.Result);
        Assert.Equal("chrome-work", response.ProfileId);
        Assert.True(response.Remember);
    }

    [Fact]
    public void Cancellation_is_accepted()
    {
        var config = Sample();
        var request = PickerMessages.CreateRequest(config, Decide(config, "https://login.microsoftonline.com/"), Platform.Windows, null);

        var response = PickerMessages.TryParseResponse("""{ "result": "cancelled" }""", request);

        Assert.NotNull(response);
        Assert.Equal(PickerResult.Cancelled, response.Result);
        Assert.Null(response.ProfileId);
    }

    [Theory]
    [InlineData("""{ "result": "selected", "profileId": "not-a-candidate" }""")]
    [InlineData("""{ "result": "selected" }""")]
    [InlineData("""{ "result": "banana" }""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Invalid_responses_are_rejected(string json)
    {
        var config = Sample();
        var request = PickerMessages.CreateRequest(config, Decide(config, "https://login.microsoftonline.com/"), Platform.Windows, null);

        Assert.Null(PickerMessages.TryParseResponse(json, request));
    }

    [Fact]
    public void Response_serialization_uses_the_documented_shape()
    {
        var json = PickerMessages.Serialize(new PickerResponse { Result = PickerResult.Selected, ProfileId = "edge", Remember = false });
        using var document = JsonDocument.Parse(json);

        Assert.Equal("selected", document.RootElement.GetProperty("result").GetString());
        Assert.Equal("edge", document.RootElement.GetProperty("profileId").GetString());
    }
}
