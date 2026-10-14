using System.Net;
using System.Text;
using System.Text.Json;
using OscarWatch.Core.Models;
using OscarWatch.Core.Terminal.Uhm;

namespace OscarWatch.Tests.Terminal;

public sealed class UhmApiClientTests
{
    private static SatelliteStatusSettings Settings => new() { BaseUrl = "https://oscarwatch.org/", ApiToken = "tok" };

    [Fact]
    public async Task Post_heard_sends_snake_case_body_and_reports_stored()
    {
        var handler = new StubHandler("""{"stored":true,"server_time_utc":"2026-07-07T15:04:01Z"}""", HttpStatusCode.Created);
        var client = new UhmApiClient(new HttpClient(handler));

        var result = await client.PostHeardAsync(Settings, new UhmHeardReport(
            "ISS", "g0abc", "mm9sql", new DateTime(2026, 7, 7, 15, 4, 0, DateTimeKind.Utc), "R73", "OscarWatch-Tracker/1.0"));

        Assert.True(result.Result.Success);
        Assert.True(result.Stored);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("https://oscarwatch.org/api/v1/uhm/heard", handler.LastUri!.ToString());
        Assert.Equal("Bearer tok", handler.LastAuth);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("ISS", body.RootElement.GetProperty("satellite").GetString());
        Assert.Equal("G0ABC", body.RootElement.GetProperty("heard_tx").GetString());
        Assert.Equal("MM9SQL", body.RootElement.GetProperty("heard_rx").GetString());
        Assert.Equal("2026-07-07T15:04:00Z", body.RootElement.GetProperty("heard_at").GetString());
        Assert.Equal("R73", body.RootElement.GetProperty("decoded_message").GetString());
    }

    [Fact]
    public async Task Post_heard_dedup_is_success_not_stored()
    {
        var client = new UhmApiClient(new HttpClient(new StubHandler("""{"stored":false}""", HttpStatusCode.OK)));
        var result = await client.PostHeardAsync(Settings, new UhmHeardReport("ISS", "G0ABC", "MM9SQL", DateTime.UtcNow, null, null));
        Assert.True(result.Result.Success);
        Assert.False(result.Stored);
    }

    [Fact]
    public async Task Post_heard_omits_empty_optional_fields()
    {
        var handler = new StubHandler("{}", HttpStatusCode.Created);
        await new UhmApiClient(new HttpClient(handler)).PostHeardAsync(
            Settings, new UhmHeardReport("ISS", "G0ABC", "MM9SQL", DateTime.UtcNow, null, ""));
        Assert.DoesNotContain("decoded_message", handler.LastBody!, StringComparison.Ordinal);
        Assert.DoesNotContain("client", handler.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Match_builds_query_and_parses_mutual()
    {
        var handler = new StubHandler(
            """{"matched":true,"mutual":true,"heard_at":"2026-07-07T15:04:00Z"}""", HttpStatusCode.OK);
        var client = new UhmApiClient(new HttpClient(handler));

        var result = await client.GetMatchAsync(Settings, "IO-117", "mm9sql", "G0ABC", "Hello World");

        Assert.True(result.Matched);
        Assert.True(result.Mutual);
        Assert.Equal(new DateTime(2026, 7, 7, 15, 4, 0, DateTimeKind.Utc), result.HeardAtUtc);
        Assert.Equal(
            "https://oscarwatch.org/api/v1/uhm/match?satellite=IO-117&callsign_tx=MM9SQL&callsign_rx=G0ABC&decoded_message=Hello%20World",
            handler.LastUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Match_without_message_is_callsign_pair_only()
    {
        var handler = new StubHandler("""{"matched":false,"mutual":false}""", HttpStatusCode.OK);
        var result = await new UhmApiClient(new HttpClient(handler)).GetMatchAsync(Settings, "ISS", "MM9SQL", "G0ABC");
        Assert.False(result.Matched);
        Assert.DoesNotContain("decoded_message", handler.LastUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_returns_callsigns()
    {
        var handler = new StubHandler("""{"callsigns":["2m0sql","G0ABC"]}""", HttpStatusCode.OK);
        var result = await new UhmApiClient(new HttpClient(handler)).GetActiveAsync(Settings);
        Assert.Equal(["2M0SQL", "G0ABC"], result.Callsigns);
    }

    [Fact]
    public async Task Status_is_public_and_null_message_is_null()
    {
        var handler = new StubHandler("""{"message":null}""", HttpStatusCode.OK);
        var result = await new UhmApiClient(new HttpClient(handler)).GetStatusAsync(new SatelliteStatusSettings());
        Assert.True(result.Result.Success);
        Assert.Null(result.Message);
        Assert.Null(handler.LastAuth);
    }

    [Fact]
    public async Task Me_reads_callsign_from_nested_data()
    {
        var handler = new StubHandler("""{"data":{"callsign":"mm9sql"}}""", HttpStatusCode.OK);
        var result = await new UhmApiClient(new HttpClient(handler)).GetMeAsync(Settings);
        Assert.Equal("MM9SQL", result.Callsign);
        Assert.Equal("https://oscarwatch.org/api/v1/me", handler.LastUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, UhmErrorKind.Unauthorised)]
    [InlineData(HttpStatusCode.Forbidden, UhmErrorKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, UhmErrorKind.NotEnabled)]
    [InlineData(HttpStatusCode.InternalServerError, UhmErrorKind.Server)]
    public async Task Maps_error_codes(HttpStatusCode status, UhmErrorKind expected)
    {
        var client = new UhmApiClient(new HttpClient(new StubHandler("""{"message":"nope"}""", status)));
        var result = await client.GetActiveAsync(Settings);
        Assert.False(result.Result.Success);
        Assert.Equal(expected, result.Result.Error);
        Assert.Equal("nope", result.Result.Message);
    }

    [Fact]
    public async Task Validation_error_shows_first_field_error()
    {
        var body = """{"message":"The given data was invalid.","errors":{"heard_tx":["Your callsign must appear as either the transmitter or receiver."]}}""";
        var client = new UhmApiClient(new HttpClient(new StubHandler(body, HttpStatusCode.UnprocessableEntity)));
        var result = await client.PostHeardAsync(Settings, new UhmHeardReport("ISS", "A1A", "B1B", DateTime.UtcNow, null, null));
        Assert.Equal(UhmErrorKind.Validation, result.Result.Error);
        Assert.StartsWith("Your callsign must appear", result.Result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_honours_retry_after()
    {
        var handler = new StubHandler("{}", HttpStatusCode.TooManyRequests) { RetryAfterSeconds = 45 };
        var result = await new UhmApiClient(new HttpClient(handler)).GetMatchAsync(Settings, "ISS", "A1A", "B1B");
        Assert.Equal(UhmErrorKind.RateLimited, result.Result.Error);
        Assert.Equal(TimeSpan.FromSeconds(45), result.Result.RetryAfter);
    }

    [Fact]
    public async Task Rate_limit_without_header_backs_off_30_seconds()
    {
        var handler = new StubHandler("{}", HttpStatusCode.TooManyRequests);
        var result = await new UhmApiClient(new HttpClient(handler)).GetMatchAsync(Settings, "ISS", "A1A", "B1B");
        Assert.Equal(TimeSpan.FromSeconds(30), result.Result.RetryAfter);
    }

    [Fact]
    public async Task Missing_token_fails_without_request()
    {
        var handler = new StubHandler("{}", HttpStatusCode.OK);
        var result = await new UhmApiClient(new HttpClient(handler)).GetActiveAsync(new SatelliteStatusSettings());
        Assert.Equal(UhmErrorKind.MissingToken, result.Result.Error);
        Assert.Null(handler.LastUri);
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public int? RetryAfterSeconds { get; init; }
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastAuth { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastAuth = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (RetryAfterSeconds is { } s)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(s));
            return response;
        }
    }
}
