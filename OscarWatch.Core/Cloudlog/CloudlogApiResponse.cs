using System.Text.Json;
using System.Text.Json.Serialization;

namespace OscarWatch.Core.Cloudlog;

internal sealed class CloudlogApiResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("imported_count")]
    public int? ImportedCount { get; set; }

    [JsonPropertyName("messages")]
    public JsonElement Messages { get; set; }
}
