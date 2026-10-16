using System.Text.Json;

namespace OscarWatch.Core.Cloudlog;

public static class CloudlogResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(string? body, out bool success, out string? reason)
    {
        success = false;
        reason = null;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            var doc = JsonSerializer.Deserialize<CloudlogApiResponse>(body, Options);
            if (doc is null)
                return false;

            reason = FirstReason(doc);
            success = IsSuccessStatus(doc.Status);
            if (success && doc.ImportedCount is 0)
            {
                success = false;
                if (string.IsNullOrWhiteSpace(reason))
                    reason = "Cloudlog did not import this QSO.";
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? TryParseReason(string? body) =>
        TryParse(body, out _, out var reason) ? reason : null;

    internal static bool IsSuccessStatus(string? status) =>
        string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "created", StringComparison.OrdinalIgnoreCase);

    private static string? FirstReason(CloudlogApiResponse doc)
    {
        var reason = doc.Reason?.Trim();
        if (!string.IsNullOrWhiteSpace(reason))
            return reason;

        if (doc.Messages.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in doc.Messages.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var text = item.GetString()?.Trim();
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        return null;
    }
}
