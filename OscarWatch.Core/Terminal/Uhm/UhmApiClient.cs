using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OscarWatch.Core.Models;
using OscarWatch.Core.Net;

namespace OscarWatch.Core.Terminal.Uhm;

public enum UhmErrorKind
{
    None,
    MissingToken,
    InvalidUrl,
    Unauthorised,
    Forbidden,
    NotEnabled,
    Validation,
    RateLimited,
    Network,
    Server
}

public sealed record UhmResult(bool Success, UhmErrorKind Error, string Message, int StatusCode, TimeSpan? RetryAfter = null)
{
    public static UhmResult Ok(int statusCode) => new(true, UhmErrorKind.None, "", statusCode);
}

public sealed record UhmHeardReport(
    string Satellite,
    string HeardTx,
    string HeardRx,
    DateTime HeardAtUtc,
    string? DecodedMessage,
    string? Client);

public sealed record UhmHeardResult(UhmResult Result, bool Stored);

public sealed record UhmMatchResult(UhmResult Result, bool Matched, bool Mutual, DateTime? HeardAtUtc);

public sealed record UhmActiveResult(UhmResult Result, IReadOnlyList<string> Callsigns);

public sealed record UhmStatusResult(UhmResult Result, string? Message);

public sealed record UhmMeResult(UhmResult Result, string? Callsign);

/// <summary>
/// Client for the OscarWatch.org UHM (You Heard Me) API at <c>/api/v1/uhm</c>. Uses the same Bearer
/// token and base URL as satellite status reporting.
/// </summary>
public sealed class UhmApiClient
{
    public const string BasePath = "/api/v1/uhm";
    public const int MaxDecodedMessageLength = 1024;
    public const int MaxClientLength = 128;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public UhmApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? OscarWatchHttpClients.Create(TimeSpan.FromSeconds(20));
    }

    public async Task<UhmHeardResult> PostHeardAsync(
        SatelliteStatusSettings settings,
        UhmHeardReport report,
        CancellationToken cancellationToken = default)
    {
        var body = new HeardBody
        {
            Satellite = report.Satellite.Trim(),
            HeardTx = report.HeardTx.Trim().ToUpperInvariant(),
            HeardRx = report.HeardRx.Trim().ToUpperInvariant(),
            HeardAt = FormatUtc(report.HeardAtUtc),
            DecodedMessage = Truncate(report.DecodedMessage, MaxDecodedMessageLength),
            Client = Truncate(report.Client, MaxClientLength)
        };

        var (result, _) = await SendAsync(
            settings,
            HttpMethod.Post,
            BasePath + "/heard",
            JsonSerializer.Serialize(body, JsonOptions),
            authenticated: true,
            cancellationToken).ConfigureAwait(false);

        return new UhmHeardResult(result, result.Success && result.StatusCode == (int)HttpStatusCode.Created);
    }

    public async Task<UhmMatchResult> GetMatchAsync(
        SatelliteStatusSettings settings,
        string satellite,
        string callsignTx,
        string callsignRx,
        string? decodedMessage = null,
        CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder("/match?satellite=")
            .Append(Uri.EscapeDataString(satellite.Trim()))
            .Append("&callsign_tx=").Append(Uri.EscapeDataString(callsignTx.Trim().ToUpperInvariant()))
            .Append("&callsign_rx=").Append(Uri.EscapeDataString(callsignRx.Trim().ToUpperInvariant()));
        var message = Truncate(decodedMessage, MaxDecodedMessageLength);
        if (message is not null)
            query.Append("&decoded_message=").Append(Uri.EscapeDataString(message));

        var (result, json) = await SendAsync(settings, HttpMethod.Get, BasePath + query, null, true, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success || json is null)
            return new UhmMatchResult(result, false, false, null);

        var root = json.RootElement;
        return new UhmMatchResult(
            result,
            ReadBool(root, "matched"),
            ReadBool(root, "mutual"),
            ReadUtc(root, "heard_at"));
    }

    public async Task<UhmActiveResult> GetActiveAsync(
        SatelliteStatusSettings settings,
        CancellationToken cancellationToken = default)
    {
        var (result, json) = await SendAsync(settings, HttpMethod.Get, BasePath + "/active", null, true, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success || json is null)
            return new UhmActiveResult(result, []);

        var list = new List<string>();
        if (json.RootElement.TryGetProperty("callsigns", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in calls.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(c.GetString()))
                    list.Add(c.GetString()!.Trim().ToUpperInvariant());
            }
        }

        return new UhmActiveResult(result, list);
    }

    public async Task<UhmStatusResult> GetStatusAsync(
        SatelliteStatusSettings settings,
        CancellationToken cancellationToken = default)
    {
        var (result, json) = await SendAsync(settings, HttpMethod.Get, BasePath + "/status", null, false, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success || json is null)
            return new UhmStatusResult(result, null);

        var message = json.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : null;
        return new UhmStatusResult(result, string.IsNullOrWhiteSpace(message) ? null : message.Trim());
    }

    /// <summary>Confirms the token with <c>/api/v1/me</c> and returns the account callsign when present.</summary>
    public async Task<UhmMeResult> GetMeAsync(
        SatelliteStatusSettings settings,
        CancellationToken cancellationToken = default)
    {
        var (result, json) = await SendAsync(settings, HttpMethod.Get, "/api/v1/me", null, true, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success || json is null)
            return new UhmMeResult(result, null);

        return new UhmMeResult(result, FindCallsign(json.RootElement));
    }

    private static string? FindCallsign(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        if (root.TryGetProperty("callsign", out var c) && c.ValueKind == JsonValueKind.String)
            return c.GetString()?.Trim().ToUpperInvariant();
        foreach (var name in new[] { "data", "user" })
        {
            if (root.TryGetProperty(name, out var inner))
            {
                var found = FindCallsign(inner);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private async Task<(UhmResult Result, JsonDocument? Json)> SendAsync(
        SatelliteStatusSettings settings,
        HttpMethod method,
        string pathAndQuery,
        string? jsonBody,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        var token = settings.ApiToken?.Trim() ?? "";
        if (authenticated && token.Length == 0)
            return (new UhmResult(false, UhmErrorKind.MissingToken, "API token is required.", 0), null);

        var root = (settings.BaseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(root + pathAndQuery, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return (new UhmResult(false, UhmErrorKind.InvalidUrl, "Base URL is not a valid http(s) address.", 0), null);

        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (authenticated)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (jsonBody is not null)
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var code = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
                return (BuildError(response, body), null);

            JsonDocument? json = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    json = JsonDocument.Parse(body);
                }
                catch (JsonException)
                {
                    return (new UhmResult(false, UhmErrorKind.Server, "Invalid JSON response.", code), null);
                }
            }

            return (UhmResult.Ok(code), json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return (new UhmResult(false, UhmErrorKind.Network, ex.Message, 0), null);
        }
    }

    private static UhmResult BuildError(HttpResponseMessage response, string body)
    {
        var code = (int)response.StatusCode;
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => UhmErrorKind.Unauthorised,
            HttpStatusCode.Forbidden => UhmErrorKind.Forbidden,
            HttpStatusCode.NotFound => UhmErrorKind.NotEnabled,
            HttpStatusCode.UnprocessableEntity => UhmErrorKind.Validation,
            HttpStatusCode.TooManyRequests => UhmErrorKind.RateLimited,
            _ => UhmErrorKind.Server
        };

        TimeSpan? retryAfter = null;
        if (kind == UhmErrorKind.RateLimited)
        {
            var header = response.Headers.RetryAfter;
            if (header?.Delta is { } delta)
                retryAfter = delta;
            else if (header?.Date is { } date)
                retryAfter = date - DateTimeOffset.UtcNow;
            if (retryAfter is null || retryAfter < TimeSpan.FromSeconds(1))
                retryAfter = TimeSpan.FromSeconds(30);
        }

        return new UhmResult(false, kind, ExtractMessage(body), code, retryAfter);
    }

    /// <summary>First validation error when present, otherwise the top-level message.</summary>
    internal static string ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in errors.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0
                        && prop.Value[0].ValueKind == JsonValueKind.String)
                        return prop.Value[0].GetString() ?? "";
                }
            }

            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? "";
        }
        catch (JsonException)
        {
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 160 ? trimmed : trimmed[..160];
    }

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTime? ReadUtc(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed.ToUniversalTime();
        return null;
    }

    internal static string FormatUtc(DateTime utc) =>
        utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Length <= max ? value : value[..max];
    }

    private sealed class HeardBody
    {
        public string Satellite { get; set; } = "";
        public string HeardTx { get; set; } = "";
        public string HeardRx { get; set; } = "";
        public string HeardAt { get; set; } = "";
        public string? DecodedMessage { get; set; }
        public string? Client { get; set; }
    }
}
